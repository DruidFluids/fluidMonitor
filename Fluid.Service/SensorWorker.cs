using System;
using System.Threading;
using System.Threading.Tasks;
using Fluid.Shared.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fluid.Service;

public sealed class SensorWorker : BackgroundService
{
    private readonly HardwareMonitor _hw;
    private readonly PipeServer      _pipe;
    private readonly TcpServer       _tcp;
    private readonly CmdServer       _cmd;
    private readonly ServiceConfig   _cfg;
    private readonly ILogger<SensorWorker> _log;

    public SensorWorker(HardwareMonitor hw, PipeServer pipe, TcpServer tcp,
                        CmdServer cmd, ServiceConfig cfg, ILogger<SensorWorker> log)
    {
        _hw = hw; _pipe = pipe; _tcp = tcp; _cmd = cmd; _cfg = cfg; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("fluidsvc starting");

        // Load config (generates cert/secret on first run)
        _cfg.Load();
        _log.LogInformation("TCP remote monitoring: {Status}", _cfg.TcpEnabled ? "enabled" : "disabled");

        // Start local pipe (always on)
        _pipe.Start();

        // Start TCP server only if user has enabled it
        if (_cfg.TcpEnabled) _tcp.Start();

        // Start command pipe (always on — needed to let app enable TCP)
        _cmd.Start();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snap = _hw.Sample();
                await _pipe.BroadcastAsync(snap, stoppingToken);

                // Only broadcast TCP if enabled (clients connect only when enabled anyway)
                if (_cfg.TcpEnabled)
                    await _tcp.BroadcastAsync(snap, stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { _log.LogError(ex, "Sensor worker error"); }

            try { await Task.Delay(PipeProtocol.ServerPollIntervalMs, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _log.LogInformation("fluidsvc stopping");
        await _tcp.DisposeAsync();
        await _cmd.DisposeAsync();
        await _pipe.DisposeAsync();
        _hw.Dispose();
        await base.StopAsync(cancellationToken);
    }
}
