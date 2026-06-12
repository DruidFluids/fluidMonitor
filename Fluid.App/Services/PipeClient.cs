using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fluid.Shared.Protocol;

namespace Fluid.App.Services;

/// <summary>
/// Connects to fluidsvc's named pipe, reads newline-delimited SensorSnapshot
/// JSON lines, and raises events. Auto-reconnects on disconnect.
///
/// All events fire on a background thread — consumers must marshal to UI.
/// </summary>
public sealed class PipeClient : ISensorClient, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;

    public event Action<SensorSnapshot>? SnapshotReceived;
    public event Action<bool>?           ConnectionStateChanged;

    private bool _connected;
    public bool IsConnected => _connected;

    public void Start()
    {
        _loopTask = Task.Run(() => LoopAsync(_cts.Token));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeClientStream? client = null;
            try
            {
                client = new NamedPipeClientStream(
                    ".", PipeProtocol.PipeName,
                    PipeDirection.In,           // we only read snapshots
                    PipeOptions.Asynchronous);

                await client.ConnectAsync(PipeProtocol.ClientConnectTimeoutMs, ct);
                SetConnected(true);

                // StreamReader over the pipe — one JSON line per snapshot.
                using var reader = new StreamReader(client);
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null) break;            // server closed our handle
                    if (line.Length == 0) continue;

                    SensorSnapshot? snap;
                    try { snap = JsonSerializer.Deserialize<SensorSnapshot>(line, PipeProtocol.JsonOptions); }
                    catch { continue; }                 // skip malformed line, keep reading

                    if (snap != null) SnapshotReceived?.Invoke(snap);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (TimeoutException)           { /* service not running yet */ }
            catch (IOException)                { /* pipe broken — reconnect */ }
            catch (Exception)                  { /* unknown — reconnect */ }
            finally
            {
                client?.Dispose();
                SetConnected(false);
            }

            try { await Task.Delay(PipeProtocol.ClientReconnectDelayMs, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void SetConnected(bool state)
    {
        if (_connected == state) return;
        _connected = state;
        ConnectionStateChanged?.Invoke(state);
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _loopTask?.Wait(500); } catch { }
        _cts.Dispose();
    }
}
