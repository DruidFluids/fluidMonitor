using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fluid.Shared.Protocol;
using Microsoft.Extensions.Logging;

namespace Fluid.Service;

/// <summary>
/// Hosts the named pipe (\\.\pipe\fluid). Each accepted connection gets its
/// own StreamWriter; Broadcast() writes one JSON line to every live writer.
/// Disconnected writers are removed lazily on write failure.
/// </summary>
public sealed class PipeServer : IAsyncDisposable
{
    private readonly ILogger<PipeServer> _log;
    private readonly ConcurrentDictionary<Guid, ClientConnection> _clients = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;

    public PipeServer(ILogger<PipeServer> log) => _log = log;

    public void Start()
    {
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        _log.LogInformation("Pipe server listening on \\\\.\\pipe\\{Name}", PipeProtocol.PipeName);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = CreatePipeServer();
                await server.WaitForConnectionAsync(ct);

                var id = Guid.NewGuid();
                var conn = new ClientConnection(id, server);
                _clients[id] = conn;
                _log.LogInformation("Client {Id} connected ({Count} total)", id, _clients.Count);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Pipe accept failed; retrying");
                server?.Dispose();
                try { await Task.Delay(500, ct); } catch { return; }
            }
        }
    }

    /// <summary>
    /// Creates a pipe server with an ACL that permits Authenticated Users to
    /// connect. Without this, only LocalSystem could read — the widget runs
    /// in the user session and would be locked out.
    /// </summary>
    private static NamedPipeServerStream CreatePipeServer()
    {
        var security = new PipeSecurity();
        // Read+Write for any authenticated user; FullControl for LocalSystem (us).
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeProtocol.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize:  1024,
            outBufferSize: 8192,
            pipeSecurity:  security);
    }

    public async Task BroadcastAsync(SensorSnapshot snap, CancellationToken ct)
    {
        if (_clients.IsEmpty) return;

        var json = JsonSerializer.Serialize(snap, PipeProtocol.JsonOptions) + "\n";
        var bytes = Encoding.UTF8.GetBytes(json);
        var dead = new System.Collections.Generic.List<Guid>();

        foreach (var (id, conn) in _clients)
        {
            try
            {
                if (!conn.Stream.IsConnected) { dead.Add(id); continue; }
                await conn.WriteAsync(bytes, ct);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Client {Id} write failed; dropping", id);
                dead.Add(id);
            }
        }

        foreach (var id in dead)
        {
            if (_clients.TryRemove(id, out var conn))
            {
                conn.Dispose();
                _log.LogInformation("Client {Id} disconnected ({Count} total)", id, _clients.Count);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts.Cancel(); } catch { }
        if (_acceptLoop != null)
        {
            try { await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        }
        foreach (var (_, conn) in _clients) conn.Dispose();
        _clients.Clear();
        _cts.Dispose();
    }

    private sealed class ClientConnection : IDisposable
    {
        public Guid Id { get; }
        public NamedPipeServerStream Stream { get; }
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public ClientConnection(Guid id, NamedPipeServerStream stream)
        {
            Id = id;
            Stream = stream;
        }

        public async Task WriteAsync(byte[] bytes, CancellationToken ct)
        {
            await _writeLock.WaitAsync(ct);
            try
            {
                await Stream.WriteAsync(bytes, ct);
                await Stream.FlushAsync(ct);
            }
            finally { _writeLock.Release(); }
        }

        public void Dispose()
        {
            try { Stream.Disconnect(); } catch { }
            try { Stream.Dispose();    } catch { }
            _writeLock.Dispose();
        }
    }
}
