using System.Collections.Generic;
using System;

namespace Fluid.Shared.Protocol;

/// <summary>
/// One sample of all sensor data. Service emits one of these per poll tick
/// as a single JSON line over the named pipe.
/// </summary>
public class SensorSnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public CpuInfo Cpu { get; set; } = new();
    public GpuInfo Gpu { get; set; } = new(); // Primary GPU
    public List<GpuInfo> Gpus { get; set; } = new(); // All GPUs
    public RamInfo Ram { get; set; } = new();
    public NetworkInfo Network { get; set; } = new();
    public StorageInfo Storage { get; set; } = new();
}

public class CpuInfo
{
    public string Name { get; set; } = "CPU";
    public float LoadPercent { get; set; }
    public float? TempC { get; set; }
    public float? ClockMhz { get; set; }
}

public class GpuInfo
{
    public string Name { get; set; } = "GPU";
    public float LoadPercent { get; set; }
    public float? TempC { get; set; }
    public float? VramUsedMb { get; set; }
    public float? VramTotalMb { get; set; }
    public float? ClockMhz { get; set; }
}

public class RamInfo
{
    public float  UsedGb         { get; set; }
    public float  TotalGb        { get; set; }
    public float  LoadPercent    { get; set; }
    public int    MemorySpeedMhz { get; set; } = 0;
    public string MemoryType     { get; set; } = "";
}

public class NetworkInfo
{
    public double DownBytesPerSec { get; set; }
    public double UpBytesPerSec { get; set; }
}

public class StorageInfo
{
    public float UsedPercent { get; set; }
    public float FreeGb { get; set; }
    public float TotalGb { get; set; }
    public double ReadBytesPerSec  { get; set; }
    public double WriteBytesPerSec { get; set; }

    // v1.20.3: physical disk identification (when SelectedDiskId is non-empty)
    public string Model  { get; set; } = "";
    public string DiskId { get; set; } = "";

    // v1.24: drive letters living on the selected physical disk (e.g. "C:" or
    // "C: D:"). Resolved by the service. Additive JSON field: old services
    // simply leave it empty and the app falls back to Model.
    public string DriveLetters { get; set; } = "";

    // v1.25: NVMe drive temperature in Celsius, read via the StorNVMe
    // DeviceIoControl protocol (pure user-mode, NO kernel driver of ours).
    // Null when the selected disk isn't NVMe or doesn't report temperature.
    public float? TempC { get; set; }
}
