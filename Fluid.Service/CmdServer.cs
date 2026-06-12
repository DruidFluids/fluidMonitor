using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fluid.Shared.Protocol;
using Microsoft.Extensions.Logging;

namespace Fluid.Service;

/// <summary>
/// Listens on \\.\pipe\fluidMonitor-cmd for single-shot commands from the app.
/// Each connection: read one command, execute, send one response, close.
/// </summary>
public sealed class CmdServer : IAsyncDisposable
{
    private readonly ILogger<CmdServer> _log;
    private readonly ServiceConfig      _cfg;
    private readonly TcpServer          _tcp;
    private readonly HardwareMonitor    _hw;   // v1.21: for live disk re-routing
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public CmdServer(ServiceConfig cfg, TcpServer tcp, HardwareMonitor hw, ILogger<CmdServer> log)
    {
        _cfg = cfg; _tcp = tcp; _hw = hw; _log = log;
    }

    public void Start()
    {
        _loop = Task.Run(() => LoopAsync(_cts.Token));
        _log.LogInformation("Command pipe listening on {Name}", TcpProtocol.CmdPipeName);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(ct);
                await HandleAsync(pipe, ct);
            }
            catch (OperationCanceledException) { pipe?.Dispose(); return; }
            catch (Exception ex) { _log.LogDebug(ex, "Cmd pipe error"); }
            finally { pipe?.Dispose(); }
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var ps = new PipeSecurity();
        ps.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        ps.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            TcpProtocol.CmdPipeName, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            4096, 4096, ps);
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

        var line = await reader.ReadLineAsync(ct);
        if (string.IsNullOrEmpty(line)) return;

        using var doc = JsonDocument.Parse(line);
        var type = doc.RootElement.GetProperty("type").GetString();

        switch (type)
        {
            case "getConfig":
                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    type            = "config",
                    tcpEnabled      = _cfg.TcpEnabled,
                    tcpPort         = _cfg.TcpPort,
                    handshakeKey    = _cfg.HandshakeKey,
                    networkAdapter  = _cfg.NetworkAdapterName,
                    selectedDiskId  = _cfg.SelectedDiskId
                }));
                break;

            // v1.21: route the Disk tile to a specific physical disk. Persists
            // to service.json and re-routes the perf counters live.
            case "setSelectedDisk":
                var diskId = doc.RootElement.GetProperty("id").GetString() ?? "";
                _cfg.SetSelectedDisk(diskId);
                _hw.ReconfigureDisk();
                await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "ok" }));
                break;

            case "recheckSensors":
                // v1.25: the user just installed (or removed) a CPU-temp sensor
                // driver. LHM enumerates CPU hardware at Open(), so re-open to
                // pick up the change without a service restart. Reply with
                // whether CPU temperature is now available so the app can update
                // the tile and dialog immediately.
                bool cpuTemp = _hw.RecheckSensors();
                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    type = "ok",
                    cpuTempAvailable = cpuTemp
                }));
                break;

            case "setTcpEnabled":
                var enabled = doc.RootElement.GetProperty("enabled").GetBoolean();
                _cfg.SetTcpEnabled(enabled);
                if (enabled && _cfg.TcpEnabled)
                    _tcp.Start();
                else
                    _tcp.Stop();
                await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "ok" }));
                break;

            case "setNetworkAdapter":
                var adapterName = doc.RootElement.GetProperty("name").GetString() ?? "";
                _cfg.SetNetworkAdapter(adapterName);
                await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "ok" }));
                break;

            case "regenerateKey":
                _tcp.Stop();
                _cfg.RegenerateKey();
                if (_cfg.TcpEnabled) _tcp.Start();
                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    type         = "ok",
                    handshakeKey = _cfg.HandshakeKey
                }));
                break;

            default:
                await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "error", msg = "unknown command" }));
                break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts.Cancel(); } catch { }
        if (_loop != null) try { await _loop.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}
