using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using Fluid.Shared.Protocol;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;

namespace Fluid.Service;

public sealed class HardwareMonitor : IDisposable
{
    private readonly ILogger<HardwareMonitor> _log;
    private readonly Computer _computer;
    private readonly ServiceConfig? _cfg;
    private float  _wmiRamTotalGb   = 0;
    private int    _wmiRamSpeedMhz  = 0;
    private string _wmiRamType      = "";
    private int    _bestGpuPriority = -1; // reset each sample tick
    private readonly UpdateVisitor _visitor = new();

    // Network rate tracking
    private long _lastBytesSent, _lastBytesRecv;
    private DateTime _lastNetSample = DateTime.UtcNow;

    // Disk I/O via PerformanceCounter (far more reliable than LHM Throughput)
    private PerformanceCounter? _diskRead;
    private PerformanceCounter? _diskWrite;
    // v1.20.3: cache selected disk metadata so we can include it in snapshots
    private string _selectedDiskInstance = "_Total";
    private string _diskModel = "";
    private string _diskLetters = ""; // v1.24: e.g. "C:" or "C: D:"
    // v1.25: physical disk index for the selected drive (for NVMe temp reads),
    // a cached temperature, and a throttle so we poll the drive at most ~once
    // every 5s rather than every sample tick (temp barely moves and the IOCTL
    // isn't free).
    private string _diskPhysIndex = "";
    private float? _diskTempC = null;
    private DateTime _lastDiskTempPoll = DateTime.MinValue;

    // CPU effective frequency via PerformanceCounter (matches Task Manager)
    // Effective MHz = BaseFreq × (% Processor Performance / 100)
    private PerformanceCounter? _cpuBaseFreq;
    private PerformanceCounter? _cpuPerfPct;

    public HardwareMonitor(ILogger<HardwareMonitor> log, ServiceConfig cfg)
    {
        _log = log;
        _cfg = cfg;

        _computer = new Computer
        {
            IsCpuEnabled     = true,
            IsGpuEnabled     = true,
            IsMemoryEnabled  = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = true,
        };
        _computer.Open();
        _log.LogInformation("LibreHardwareMonitor opened. Hardware count: {Count}", CountHw(_computer));

        // v1.25: CPU temperature (Tctl/Tdie via AMD SMN / Intel MSR) requires
        // a kernel sensor driver, which Windows blocks for user-mode apps. The
        // default install ships NO driver — CPU load/clock and all other tiles
        // work regardless. The user can OPT IN by installing PawnIO from the
        // app (driver-free until they choose otherwise). RecheckSensors()
        // re-opens LHM live after they install/remove it.
        LogCpuTempAvailability();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Capacity, Speed, SMBIOSMemoryType FROM Win32_PhysicalMemory");
            long totalBytes = 0;
            foreach (ManagementObject obj in searcher.Get())
            {
                totalBytes += Convert.ToInt64(obj["Capacity"]);
                if (_wmiRamSpeedMhz == 0 && obj["Speed"] != null)
                    _wmiRamSpeedMhz = Convert.ToInt32(obj["Speed"]);
                if (_wmiRamType.Length == 0 && obj["SMBIOSMemoryType"] != null)
                    _wmiRamType = Convert.ToInt32(obj["SMBIOSMemoryType"]) switch
                    {
                        34 => "DDR5", 26 => "DDR4", 24 => "DDR3", 22 => "DDR2", 18 => "DDR", _ => ""
                    };
            }
            _wmiRamTotalGb = totalBytes / (1024f * 1024 * 1024);
            _log.LogInformation("WMI RAM: {GB:0.0} GB {Type}-{Speed}", _wmiRamTotalGb, _wmiRamType, _wmiRamSpeedMhz);
        }
        catch (Exception ex) { _log.LogWarning(ex, "WMI RAM query failed; using LHM value"); }

        // Initialize perf counters. Prime them (first NextValue is always 0).
        InitDiskCounters();

        try
        {
            _cpuBaseFreq = new PerformanceCounter("Processor Information", "Processor Frequency", "_Total");
            _cpuPerfPct  = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total");
            _cpuBaseFreq.NextValue();
            _cpuPerfPct.NextValue();
            _log.LogInformation("CPU frequency perf counters initialized");
        }
        catch (Exception ex) { _log.LogWarning(ex, "CPU freq perf counters unavailable; falling back to LHM"); }
    }

    private static int CountHw(Computer c) { int n = 0; foreach (var _ in c.Hardware) n++; return n; }

    // v1.21: lock guarding the disk perf counters so ReconfigureDisk (called
    // from the CmdServer thread) can't dispose them mid-Sample (worker thread).
    private readonly object _diskLock = new();

    // v1.25: lock guarding the LHM Computer so RecheckSensors (CmdServer thread)
    // can't Close/Open it mid-Accept (worker thread Sample()).
    private readonly object _computerLock = new();

    /// <summary>
    /// v1.25: re-open LibreHardwareMonitor so a just-installed (or removed)
    /// CPU-temp sensor driver is picked up without a service restart. LHM
    /// enumerates CPU hardware at Open(), so a Close()+Open() is required for
    /// the new sensors to appear. Returns true if CPU temperature is now
    /// readable. Called by CmdServer on the "recheckSensors" command.
    /// </summary>
    public bool RecheckSensors()
    {
        lock (_computerLock)
        {
            try { _computer.Close(); } catch { }
            try { _computer.Open(); }
            catch (Exception ex) { _log.LogWarning(ex, "Re-opening LHM failed during sensor recheck"); }
        }
        LogCpuTempAvailability();
        return CpuTempAvailable();
    }

    /// <summary>True when an enumerated CPU exposes a temperature sensor.</summary>
    private bool CpuTempAvailable()
    {
        try
        {
            lock (_computerLock)
            {
                foreach (var hw in _computer.Hardware)
                {
                    if (hw.HardwareType != HardwareType.Cpu) continue;
                    foreach (var s in hw.Sensors)
                        if (s.SensorType == SensorType.Temperature) return true;
                }
            }
        }
        catch { }
        return false;
    }

    private void LogCpuTempAvailability()
    {
        if (LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled)
            _log.LogInformation("CPU sensor driver detected (PawnIO {Version}); CPU temperature available",
                LibreHardwareMonitor.PawnIo.PawnIo.Version);
        else
            _log.LogInformation("No CPU sensor driver installed; CPU temperature unavailable " +
                "(load, clock, and all other tiles unaffected). User can opt in from the app.");
    }

    /// <summary>
    /// v1.21: re-route the disk perf counters to the currently configured
    /// SelectedDiskId. Called by CmdServer when the user picks a different
    /// disk in Settings, so the change takes effect without a service restart.
    /// </summary>
    public void ReconfigureDisk()
    {
        lock (_diskLock)
        {
            try { _diskRead?.Dispose();  } catch { }
            try { _diskWrite?.Dispose(); } catch { }
            _diskRead  = null;
            _diskWrite = null;
            _selectedDiskInstance = "_Total";
            _diskModel = "";
            _diskLetters = "";
            _diskPhysIndex = "";
            _diskTempC = null;
            _lastDiskTempPoll = DateTime.MinValue;
        }
        InitDiskCounters();
    }

    // v1.20.3 / v1.21: per-disk routing. SelectedDiskId in _cfg is the perf
    // counter instance prefix (e.g. "0 C:" for first physical disk holding C:).
    // Empty string => use "_Total" aggregate. Extracted from the constructor
    // so ReconfigureDisk can re-run it live.
    private void InitDiskCounters()
    {
        try
        {
            var diskInstance = "_Total";
            try
            {
                var requested = _cfg?.SelectedDiskId ?? "";
                // v1.23.1: semantics finalized -- "" (never configured) defaults
                // to the physical disk hosting the system drive, resolved HERE
                // in the service so the tile gets a real disk + model even if
                // the user never opens Settings (and regardless of whether the
                // app's cmd-pipe push lands). "*" = explicit all-disks aggregate.
                if (requested == "*")
                    requested = "";
                else if (string.IsNullOrEmpty(requested))
                    requested = ResolveSystemDiskIndex(_log);
                if (!string.IsNullOrEmpty(requested))
                {
                    var cat = new PerformanceCounterCategory("PhysicalDisk");
                    var instances = cat.GetInstanceNames();
                    foreach (var inst in instances)
                    {
                        if (inst == "_Total") continue;
                        // Match: instance like "0 C:" should match requested "0"
                        var firstToken = inst.Split(' ')[0];
                        if (firstToken == requested) { diskInstance = inst; break; }
                    }
                    _log.LogInformation("Disk perf counter routed to instance '{Instance}' for DiskId '{Id}'", diskInstance, requested);
                }
            }
            catch (Exception ex) { _log.LogWarning(ex, "Per-disk counter routing failed; falling back to _Total"); }

            var read  = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec",  diskInstance);
            var write = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", diskInstance);
            read.NextValue();
            write.NextValue();
            lock (_diskLock)
            {
                _selectedDiskInstance = diskInstance;
                _diskRead  = read;
                _diskWrite = write;
            }
            _log.LogInformation("Disk perf counters initialized");
        }
        catch (Exception ex) { _log.LogWarning(ex, "Disk perf counters unavailable"); }

        // WMI lookup for selected disk model.
        try
        {
            var requested = _cfg?.SelectedDiskId ?? "";
            if (requested == "*")
                requested = ""; // aggregate: no single model
            else if (string.IsNullOrEmpty(requested))
                requested = ResolveSystemDiskIndex(_log); // v1.23.1: match counter routing
            if (!string.IsNullOrEmpty(requested))
            {
                string model = "", letters = "";
                using var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT Model, Index, DeviceID FROM Win32_DiskDrive WHERE Index = {requested}");
                foreach (System.Management.ManagementObject m in searcher.Get())
                {
                    model = m["Model"]?.ToString() ?? "";
                    // v1.24: walk DiskDrive -> Partitions -> LogicalDisks to
                    // collect the drive letters on this physical disk, so the
                    // tile can show "C:" instead of (or alongside) the model.
                    try
                    {
                        var devId = m["DeviceID"]?.ToString() ?? "";
                        if (devId.Length > 0)
                        {
                            var found = new System.Collections.Generic.List<string>();
                            using var parts = new System.Management.ManagementObjectSearcher(
                                $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{devId}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                            foreach (System.Management.ManagementObject part in parts.Get())
                            {
                                using var logical = new System.Management.ManagementObjectSearcher(
                                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{part["DeviceID"]}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
                                foreach (System.Management.ManagementObject ld in logical.Get())
                                {
                                    var letter = ld["DeviceID"]?.ToString() ?? "";
                                    if (letter.Length > 0) found.Add(letter);
                                }
                            }
                            found.Sort(StringComparer.OrdinalIgnoreCase);
                            letters = string.Join(" ", found);
                        }
                    }
                    catch (Exception lex) { _log.LogWarning(lex, "Drive letter resolution failed"); }
                    break;
                }
                lock (_diskLock) { _diskModel = model; _diskLetters = letters; _diskPhysIndex = requested; }
                if (!string.IsNullOrEmpty(model))
                    _log.LogInformation("Disk model resolved: {Model} ({Letters})", model, letters);
            }
            else
            {
                lock (_diskLock) { _diskPhysIndex = ""; } // aggregate: no single drive temp
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "Disk model WMI lookup failed"); }
    }

    /// <summary>
    /// v1.23.1: WMI association walk LogicalDisk(system drive) -> Partition ->
    /// DiskDrive.Index. Cached after first resolution; the system disk does
    /// not move at runtime.
    /// </summary>
    private static string? _systemDiskIndex;
    private static string ResolveSystemDiskIndex(ILogger log)
    {
        if (_systemDiskIndex != null) return _systemDiskIndex;
        try
        {
            var sysLetter = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
            using var parts = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{sysLetter}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
            foreach (ManagementObject part in parts.Get())
            {
                using var drives = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{part["DeviceID"]}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                foreach (ManagementObject drive in drives.Get())
                {
                    _systemDiskIndex = drive["Index"]?.ToString() ?? "";
                    log.LogInformation("System disk resolved to physical disk {Index}", _systemDiskIndex);
                    return _systemDiskIndex;
                }
            }
        }
        catch (Exception ex) { log.LogWarning(ex, "System disk resolution failed; using aggregate"); }
        _systemDiskIndex = "";
        return "";
    }

    public SensorSnapshot Sample()
    {
        // v1.25: snapshot the hardware list under _computerLock so a concurrent
        // RecheckSensors() (which Close()+Open()s the Computer) can't swap the
        // collection out from under this traversal.
        IHardware[] hardware;
        lock (_computerLock)
        {
            try { _computer.Accept(_visitor); }
            catch (Exception ex) { _log.LogWarning(ex, "Sensor update failed"); }
            hardware = System.Linq.Enumerable.ToArray(_computer.Hardware);
        }

        _bestGpuPriority = -1;
        var snap = new SensorSnapshot { Timestamp = DateTime.UtcNow };

        foreach (var hw in hardware)
        {
            switch (hw.HardwareType)
            {
                case HardwareType.Cpu:
                    snap.Cpu.Name = hw.Name;
                    float maxCoreClock = 0;
                    foreach (var s in hw.Sensors)
                    {
                        if (s.SensorType == SensorType.Load &&
                            s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                            snap.Cpu.LoadPercent = s.Value ?? snap.Cpu.LoadPercent;

                        else if (s.SensorType == SensorType.Temperature &&
                                 (s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                                  s.Name.Contains("CCD",     StringComparison.OrdinalIgnoreCase) ||
                                  s.Name.Contains("Tctl",    StringComparison.OrdinalIgnoreCase)))
                            snap.Cpu.TempC = s.Value;

                        // LHM clock as fallback if PerformanceCounter fails
                        else if (s.SensorType == SensorType.Clock && s.Value.HasValue &&
                                 s.Name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase) &&
                                 s.Value.Value >= 100 && s.Value.Value <= 10000)
                            maxCoreClock = Math.Max(maxCoreClock, s.Value.Value);
                    }

                    // Prefer PerformanceCounter for effective frequency (matches Task Manager).
                    // Effective MHz = base freq × (% perf / 100). Includes boost clocks.
                    try
                    {
                        if (_cpuBaseFreq != null && _cpuPerfPct != null)
                        {
                            var baseFreq = _cpuBaseFreq.NextValue();   // MHz
                            var perfPct  = _cpuPerfPct.NextValue();    // %
                            if (baseFreq > 0 && perfPct > 0)
                            {
                                snap.Cpu.ClockMhz = (float)(baseFreq * perfPct / 100.0);
                            }
                        }
                    }
                    catch { /* fall through to LHM */ }

                    // Fallback to LHM if perf counter didn't produce a value
                    if (!snap.Cpu.ClockMhz.HasValue && maxCoreClock > 0)
                        snap.Cpu.ClockMhz = maxCoreClock;
                    break;

                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel:
                {
                    var gpu = new GpuInfo { Name = hw.Name };
                    foreach (var s in hw.Sensors)
                    {
                        if (s.SensorType == SensorType.Load &&
                            s.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase))
                            gpu.LoadPercent = s.Value ?? gpu.LoadPercent;
                        else if (s.SensorType == SensorType.Temperature &&
                                 !gpu.TempC.HasValue &&
                                 (s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                                  s.Name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase)))
                            gpu.TempC = s.Value;
                        else if (s.SensorType == SensorType.SmallData &&
                                 s.Name.Contains("GPU Memory Used", StringComparison.OrdinalIgnoreCase))
                            gpu.VramUsedMb = s.Value;
                        else if (s.SensorType == SensorType.SmallData &&
                                 s.Name.Contains("GPU Memory Total", StringComparison.OrdinalIgnoreCase))
                            gpu.VramTotalMb = s.Value;
                        else if (s.SensorType == SensorType.Clock &&
                                 !gpu.ClockMhz.HasValue &&
                                 s.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase))
                            gpu.ClockMhz = s.Value;
                    }
                    // Priority: Nvidia(3) > AMD(2) > Intel Arc(1) > Intel integrated(0)
                    int priority = hw.HardwareType switch
                    {
                        HardwareType.GpuNvidia => 3,
                        HardwareType.GpuAmd    => 2,
                        HardwareType.GpuIntel  => hw.Name.Contains("Arc",
                            StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                        _ => 0
                    };
                    // Replace snap.Gpu only if this candidate has higher priority
                    if (snap.Gpu.Name == "GPU" || priority > _bestGpuPriority)
                    {
                        snap.Gpu = gpu;
                        _bestGpuPriority = priority;
                    }
                    break;
                }


                case HardwareType.Memory:
                    // v1.23: LHM 0.9.6 splits memory into multiple hardware
                    // entries that ALL report HardwareType.Memory: "Virtual
                    // Memory", "Total Memory" (physical), and one per DIMM
                    // (SPD via PawnIO, sensor names differ). Pre-0.9.6 there
                    // was a single entry, so this case could blindly assign.
                    // Now: skip virtual explicitly, and only commit values
                    // when this hardware actually produced Used/Available
                    // Data sensors -- DIMM thermal entries produce none and
                    // were zeroing the RAM tile (0.0/64.0, Usage 0%).
                    if (hw.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                        break;
                    float used = 0, avail = 0, pct = 0;
                    foreach (var s in hw.Sensors)
                    {
                        if (s.SensorType == SensorType.Data &&
                            s.Name.Contains("Used", StringComparison.OrdinalIgnoreCase))
                            used = s.Value ?? used;
                        else if (s.SensorType == SensorType.Data &&
                                 s.Name.Contains("Available", StringComparison.OrdinalIgnoreCase))
                            avail = s.Value ?? avail;
                        else if (s.SensorType == SensorType.Load &&
                                 s.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase))
                            pct = s.Value ?? pct;
                    }
                    if (used + avail <= 0)
                        break; // not the physical-memory entry (e.g. a DIMM); don't clobber
                    snap.Ram.UsedGb         = used;
                    snap.Ram.TotalGb        = _wmiRamTotalGb > 0 ? _wmiRamTotalGb : used + avail;
                    snap.Ram.LoadPercent    = _wmiRamTotalGb > 0
                        ? (used / _wmiRamTotalGb) * 100f
                        : pct > 0 ? pct : (used + avail) > 0 ? (used / (used + avail)) * 100f : 0;
                    snap.Ram.MemorySpeedMhz = _wmiRamSpeedMhz;
                    snap.Ram.MemoryType     = _wmiRamType;
                    break;
            }
        }

        // --- System drive free/total ---
        try
        {
            var sysDrive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            if (sysDrive.IsReady)
            {
                snap.Storage.FreeGb  = sysDrive.AvailableFreeSpace / 1024f / 1024f / 1024f;
                snap.Storage.TotalGb = sysDrive.TotalSize          / 1024f / 1024f / 1024f;
                snap.Storage.UsedPercent = snap.Storage.TotalGb > 0
                    ? (1f - snap.Storage.FreeGb / snap.Storage.TotalGb) * 100f : 0f;
            }
        }
        catch { }

        // --- Disk I/O via PerformanceCounter (reliable on all hardware) ---
        try
        {
            lock (_diskLock)
            {
                snap.Storage.ReadBytesPerSec  = _diskRead?.NextValue()  ?? 0;
                snap.Storage.WriteBytesPerSec = _diskWrite?.NextValue() ?? 0;
                snap.Storage.Model  = _diskModel;
                snap.Storage.DriveLetters = _diskLetters;
                snap.Storage.DiskId = _cfg?.SelectedDiskId ?? "";
            }
        }
        catch { }

        // --- NVMe drive temperature (v1.25, driver-free via StorNVMe IOCTL) ---
        // Throttled to ~every 5s. _diskPhysIndex is "" for aggregate mode or a
        // non-resolvable disk, in which case TempC stays null and the tile
        // simply omits the T: row.
        try
        {
            string physIndex;
            lock (_diskLock) { physIndex = _diskPhysIndex; }
            if (!string.IsNullOrEmpty(physIndex))
            {
                if ((DateTime.UtcNow - _lastDiskTempPoll).TotalSeconds >= 5)
                {
                    _diskTempC = NvmeTemperature.Read(physIndex, _log);
                    _lastDiskTempPoll = DateTime.UtcNow;
                }
                snap.Storage.TempC = _diskTempC;
            }
            else
            {
                snap.Storage.TempC = null;
            }
        }
        catch { }

        // --- Network rate ---
        try
        {
            long sent = 0, recv = 0;
            var adapterFilter = _cfg?.NetworkAdapterName ?? "";
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (!string.IsNullOrEmpty(adapterFilter) &&
                    !ni.Name.Equals(adapterFilter, StringComparison.OrdinalIgnoreCase) &&
                    !ni.Description.Equals(adapterFilter, StringComparison.OrdinalIgnoreCase)) continue;
                var s = ni.GetIPv4Statistics();
                sent += s.BytesSent;
                recv += s.BytesReceived;
            }
            var now = DateTime.UtcNow;
            var dt = (now - _lastNetSample).TotalSeconds;
            if (dt > 0 && _lastNetSample != default)
            {
                snap.Network.UpBytesPerSec   = (sent - _lastBytesSent) / dt;
                snap.Network.DownBytesPerSec = (recv - _lastBytesRecv) / dt;
            }
            _lastBytesSent = sent;
            _lastBytesRecv = recv;
            _lastNetSample = now;
        }
        catch { }

        return snap;
    }

    public void Dispose()
    {
        try { _computer.Close();     } catch { }
        try { _diskRead?.Dispose();  } catch { }
        try { _diskWrite?.Dispose(); } catch { }
        try { _cpuBaseFreq?.Dispose(); } catch { }
        try { _cpuPerfPct?.Dispose();  } catch { }
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer c) => c.Traverse(this);
        public void VisitHardware(IHardware h) { h.Update(); foreach (var sub in h.SubHardware) sub.Accept(this); }
        public void VisitSensor(ISensor s) { }
        public void VisitParameter(IParameter p) { }
    }
}
