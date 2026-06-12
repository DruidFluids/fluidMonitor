using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fluid.Shared.Protocol;
using Microsoft.Extensions.Logging;

namespace Fluid.Service;

/// <summary>
/// Listens on TCP port 5199. Each accepted connection goes through:
///   1. TLS handshake (server presents cert, encrypts channel)
///   2. Challenge-response: server sends nonce, client must reply with
///      HMAC-SHA256(nonce, hmacSecret). If invalid → drop.
///   3. Sensor snapshot stream begins.
///
/// Rate limiting: 5 failed auth attempts per IP → 60-second lockout.
/// </summary>
public sealed class TcpServer : IAsyncDisposable
{
    private readonly ILogger<TcpServer>  _log;
    private readonly ServiceConfig       _cfg;
    private readonly CancellationTokenSource _cts = new();

    private TcpListener? _listener;
    private Task?        _acceptLoop;

    private readonly ConcurrentDictionary<string, ClientConn> _clients = new();

    // Rate limiting: IP → (fail count, lockout-until)
    private readonly Dictionary<string, (int fails, DateTime lockedUntil)> _rateLimits = new();
    private readonly object _rateLock = new();

    public TcpServer(ServiceConfig cfg, ILogger<TcpServer> log)
    {
        _cfg = cfg;
        _log = log;
    }

    public void Start()
    {
        _listener = new TcpListener(IPAddress.Any, _cfg.TcpPort);
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        _log.LogInformation("TCP server listening on port {Port}", _cfg.TcpPort);
    }

    public void Stop()
    {
        _listener?.Stop();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient? tcp = null;
            try
            {
                tcp = await _listener!.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleClientAsync(tcp, ct), ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                tcp?.Dispose();
                _log.LogWarning(ex, "TCP accept error");
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient tcp, CancellationToken ct)
    {
        var ip = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";

        // Rate limit check
        if (IsLockedOut(ip))
        {
            _log.LogWarning("Rejected {IP} — rate limited", ip);
            tcp.Dispose();
            return;
        }

        SslStream? ssl = null;
        try
        {
            ssl = new SslStream(tcp.GetStream(), false);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate            = _cfg.GetCertificate(),
                ClientCertificateRequired    = false,
                EnabledSslProtocols          = System.Security.Authentication.SslProtocols.Tls12 |
                                               System.Security.Authentication.SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, ct);

            // Challenge-response auth
            var writer = new StreamWriter(ssl, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            var reader = new StreamReader(ssl, Encoding.UTF8, leaveOpen: true);

            var nonce = RandomNumberGenerator.GetBytes(32);
            await writer.WriteLineAsync(
                JsonSerializer.Serialize(new { type = "challenge", nonce = Convert.ToBase64String(nonce) }));

            var responseLine = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(responseLine))
                throw new InvalidOperationException("No auth response");

            using var doc = JsonDocument.Parse(responseLine);
            var root    = doc.RootElement;
            var msgType = root.GetProperty("type").GetString();
            var hmacB64 = root.GetProperty("hmac").GetString() ?? "";

            if (msgType != "auth") throw new InvalidOperationException("Expected auth message");

            var providedHmac = Convert.FromBase64String(hmacB64);
            var expectedHmac = TcpProtocol.ComputeHmac(nonce, _cfg.GetHmacSecret());

            if (!CryptographicOperations.FixedTimeEquals(providedHmac, expectedHmac))
            {
                RecordFailure(ip);
                await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "denied" }));
                _log.LogWarning("Auth failed from {IP} ({Count} attempts)", ip, GetFailCount(ip));
                return;
            }

            ResetFailCount(ip);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "ok" }));
            _log.LogInformation("Remote client authenticated: {IP}", ip);

            // Register client for broadcasts
            var id   = Guid.NewGuid();
            var conn = new ClientConn(id, ssl, writer);
            _clients[id.ToString()] = conn;
            _log.LogInformation("Remote client {ID} connected ({Count} total)", id, _clients.Count);

            // Keep alive — read loop (client may disconnect silently)
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null) break; // client disconnected
                }
            }
            catch { /* disconnected */ }
            finally
            {
                _clients.TryRemove(id.ToString(), out _);
                conn.Dispose();
                _log.LogInformation("Remote client {ID} disconnected ({Count} total)", id, _clients.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogDebug(ex, "Client {IP} connection failed", ip);
            ssl?.Dispose();
            tcp.Dispose();
        }
    }

    public async Task BroadcastAsync(SensorSnapshot snap, CancellationToken ct)
    {
        if (_clients.IsEmpty) return;
        var json = JsonSerializer.Serialize(snap, PipeProtocol.JsonOptions) + "\n";
        var dead = new List<string>();

        foreach (var (key, conn) in _clients)
        {
            try { await conn.WriteAsync(json, ct); }
            catch { dead.Add(key); }
        }

        foreach (var key in dead)
        {
            if (_clients.TryRemove(key, out var c)) c.Dispose();
        }
    }

    // ------------------------------------------------------------------
    // Rate limiting
    // ------------------------------------------------------------------

    private bool IsLockedOut(string ip)
    {
        lock (_rateLock)
        {
            if (_rateLimits.TryGetValue(ip, out var entry) && entry.fails >= 5)
                return DateTime.UtcNow < entry.lockedUntil;
            return false;
        }
    }

    private void RecordFailure(string ip)
    {
        lock (_rateLock)
        {
            _rateLimits.TryGetValue(ip, out var entry);
            var newFails = entry.fails + 1;
            _rateLimits[ip] = (newFails, DateTime.UtcNow.AddSeconds(60));
        }
    }

    private int GetFailCount(string ip)
    {
        lock (_rateLock) { return _rateLimits.TryGetValue(ip, out var e) ? e.fails : 0; }
    }

    private void ResetFailCount(string ip)
    {
        lock (_rateLock) { _rateLimits.Remove(ip); }
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts.Cancel(); } catch { }
        if (_acceptLoop != null) try { await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        foreach (var (_, c) in _clients) c.Dispose();
        _clients.Clear();
        _listener?.Stop();
        _cts.Dispose();
    }

    private sealed class ClientConn(Guid id, SslStream stream, StreamWriter writer) : IDisposable
    {
        public Guid Id => id;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public async Task WriteAsync(string line, CancellationToken ct)
        {
            await _lock.WaitAsync(ct);
            try { await writer.WriteAsync(line); }
            finally { _lock.Release(); }
        }

        public void Dispose()
        {
            try { writer.Dispose(); } catch { }
            try { stream.Dispose(); } catch { }
            _lock.Dispose();
        }
    }
}
