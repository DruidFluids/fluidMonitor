using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fluid.Shared.Protocol;

namespace Fluid.App.Services;

/// <summary>
/// Connects to a remote fluidsvc instance over TLS with cert-pinning and
/// HMAC challenge-response authentication. Auto-reconnects on drop.
/// </summary>
public sealed class RemoteTcpClient : ISensorClient, IDisposable
{
    private readonly RemoteDevice _device;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private DateTime _lastSnapshot = DateTime.UtcNow;
    private CancellationTokenSource _connCts = new(); // cancelled by watchdog to force reconnect

    private const int WatchdogSeconds = 30;

    public event Action<SensorSnapshot>? SnapshotReceived;
    public event Action<bool>?           ConnectionStateChanged;
    public bool IsConnected { get; private set; }

    public RemoteTcpClient(RemoteDevice device) => _device = device;

    public void Start()
    {
        _loop = Task.Run(() => LoopAsync(_cts.Token));
        Task.Run(() => WatchdogAsync(_cts.Token));
    }

    private async Task WatchdogAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(WatchdogSeconds), ct); }
            catch (OperationCanceledException) { return; }

            if (IsConnected &&
                (DateTime.UtcNow - _lastSnapshot).TotalSeconds > WatchdogSeconds)
            {
                // No snapshot in 30s — force reconnect
                SetConnected(false);
                _connCts?.Cancel();
            }
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient? tcp = null;
            SslStream? ssl = null;
            try
            {
                // Parse the handshake key
                if (!TcpProtocol.TryDecodeHandshakeKey(_device.HandshakeKey,
                    out var expectedFingerprint, out var hmacSecret))
                {
                    SetConnected(false);
                    await Delay(5000, ct);
                    continue;
                }

                tcp = new TcpClient();
                tcp.SendTimeout = tcp.ReceiveTimeout = 5000;
                await tcp.ConnectAsync(_device.IpAddress, _device.Port, ct);

                ssl = new SslStream(tcp.GetStream(), false,
                    (_, cert, _, _) => ValidateCert(cert, expectedFingerprint));

                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost                     = "fluidMonitor",
                    EnabledSslProtocols            = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                }, ct);

                var writer = new StreamWriter(ssl, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                var reader = new StreamReader(ssl, Encoding.UTF8, leaveOpen: true);

                // Read challenge
                var challengeLine = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(challengeLine))
                    throw new IOException("No challenge received");

                using var cDoc  = JsonDocument.Parse(challengeLine);
                var nonce       = Convert.FromBase64String(cDoc.RootElement.GetProperty("nonce").GetString()!);
                var hmac        = TcpProtocol.ComputeHmac(nonce, hmacSecret);

                // Send auth response
                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    type = "auth",
                    hmac = Convert.ToBase64String(hmac)
                }));

                // Read ok/denied
                var authResp = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(authResp)) throw new IOException("No auth response");
                using var aDoc = JsonDocument.Parse(authResp);
                if (aDoc.RootElement.GetProperty("type").GetString() != "ok")
                    throw new UnauthorizedAccessException("Authentication denied — check Handshake Key");

                SetConnected(true);
                _lastSnapshot = DateTime.UtcNow;

                // Stream snapshots
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null) break;
                    if (line.Length == 0) continue;
                    try
                    {
                        var snap = JsonSerializer.Deserialize<SensorSnapshot>(
                            line, PipeProtocol.JsonOptions);
                        if (snap != null)
                        {
                            _lastSnapshot = DateTime.UtcNow;
                            SnapshotReceived?.Invoke(snap);
                        }
                    }
                    catch { /* skip malformed line */ }
                }
            }
            catch (OperationCanceledException) { return; }
            catch (UnauthorizedAccessException ex)
            {
                // Bad key — no point retrying quickly
                SetConnected(false);
                LastError = ex.Message;
                await Delay(30000, ct);
                continue;
            }
            catch { /* network error — reconnect normally */ }
            finally
            {
                ssl?.Dispose();
                tcp?.Dispose();
                SetConnected(false);
            }

            await Delay(PipeProtocol.ClientReconnectDelayMs, ct);
        }
    }

    private static bool ValidateCert(X509Certificate? cert, byte[] expectedFingerprint)
    {
        if (cert == null) return false;
        var cert2 = new X509Certificate2(cert);
        var actual = TcpProtocol.GetCertFingerprint(cert2);
        return CryptographicOperations.FixedTimeEquals(actual, expectedFingerprint);
    }

    private void SetConnected(bool state)
    {
        if (IsConnected == state) return;
        IsConnected = state;
        ConnectionStateChanged?.Invoke(state);
    }

    private static async Task Delay(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct); } catch (OperationCanceledException) { throw; }
    }

    public string? LastError { get; private set; }

    /// <summary>One-shot test — connect, auth, then close. Returns null on success, error message on failure.</summary>
    public static async Task<string?> TestAsync(RemoteDevice device)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        TcpClient? tcp = null;
        SslStream? ssl = null;
        try
        {
            if (!TcpProtocol.TryDecodeHandshakeKey(device.HandshakeKey,
                out var fp, out var secret))
                return "Invalid Handshake Key format";

            tcp = new TcpClient();
            await tcp.ConnectAsync(device.IpAddress, device.Port, cts.Token);

            ssl = new SslStream(tcp.GetStream(), false,
                (_, cert, _, _) =>
                {
                    if (cert == null) return false;
                    var c = new X509Certificate2(cert);
                    return CryptographicOperations.FixedTimeEquals(
                        TcpProtocol.GetCertFingerprint(c), fp);
                });

            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "fluidMonitor",
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, cts.Token);

            var w = new StreamWriter(ssl, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            var r = new StreamReader(ssl, Encoding.UTF8, leaveOpen: true);

            var cl   = await r.ReadLineAsync(cts.Token);
            using var cj = JsonDocument.Parse(cl!);
            var nonce = Convert.FromBase64String(cj.RootElement.GetProperty("nonce").GetString()!);
            await w.WriteLineAsync(JsonSerializer.Serialize(new
                { type = "auth", hmac = Convert.ToBase64String(TcpProtocol.ComputeHmac(nonce, secret)) }));

            var al = await r.ReadLineAsync(cts.Token);
            using var aj = JsonDocument.Parse(al!);
            return aj.RootElement.GetProperty("type").GetString() == "ok"
                ? null : "Authentication denied — verify the Handshake Key";
        }
        catch (OperationCanceledException) { return "Connection timed out"; }
        catch (Exception ex) { return ex.Message; }
        finally { ssl?.Dispose(); tcp?.Dispose(); }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _loop?.Wait(500); } catch { }
        _cts.Dispose();
    }
}
