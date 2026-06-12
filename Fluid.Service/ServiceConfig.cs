using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fluid.Shared.Protocol;
using Microsoft.Extensions.Logging;

namespace Fluid.Service;

/// <summary>
/// Manages the service's TLS certificate, HMAC secret, and settings.
/// Stored in C:\ProgramData\fluidMonitor\service.json.
/// The TLS cert PFX is stored alongside it.
/// </summary>
public sealed class ServiceConfig
{
    private readonly ILogger<ServiceConfig> _log;

    private static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                     "fluidMonitor");
    private static readonly string ConfigPath = Path.Combine(DataDir, "service.json");
    private static readonly string CertPath   = Path.Combine(DataDir, "service.pfx");

    // Public state
    public bool   TcpEnabled        { get; private set; }
    public int    TcpPort           { get; private set; } = TcpProtocol.DefaultPort;
    public string HandshakeKey      { get; private set; } = "";
    public string NetworkAdapterName { get; private set; } = "";
    // v1.20.3: selected disk for the Disk tile (DeviceId/Index, empty = aggregate)
    public string SelectedDiskId    { get; private set; } = "";

    private byte[]?        _hmacSecret;
    private X509Certificate2? _cert;

    // Event raised when config changes (e.g., TCP toggled, key regenerated)
    public event Action? Changed;

    public ServiceConfig(ILogger<ServiceConfig> log) => _log = log;

    public void Load()
    {
        Directory.CreateDirectory(DataDir);

        RawConfig? raw = null;
        try
        {
            if (File.Exists(ConfigPath))
                raw = JsonSerializer.Deserialize<RawConfig>(File.ReadAllText(ConfigPath));
        }
        catch (Exception ex) { _log.LogWarning(ex, "Could not read service.json; regenerating"); }

        bool needsSave = false;

        // Load or generate the TLS certificate
        if (raw?.CertPfxExists == true && File.Exists(CertPath))
        {
            try
            {
                _cert = new X509Certificate2(CertPath,
                    "fluidMonitor",
                    X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.MachineKeySet);
                _log.LogInformation("Loaded existing TLS certificate");
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not load cert; regenerating");
                _cert = null;
            }
        }

        if (_cert == null)
        {
            _cert = GenerateCert();
            needsSave = true;
            _log.LogInformation("Generated new TLS certificate");
        }

        // Load or generate the HMAC secret
        if (raw?.HmacSecret != null)
        {
            try   { _hmacSecret = Convert.FromBase64String(raw.HmacSecret); }
            catch { _hmacSecret = null; }
        }

        if (_hmacSecret == null || _hmacSecret.Length != 32)
        {
            _hmacSecret = RandomNumberGenerator.GetBytes(32);
            needsSave   = true;
            _log.LogInformation("Generated new HMAC secret");
        }

        TcpEnabled         = raw?.TcpEnabled ?? false;
        TcpPort            = raw?.TcpPort    ?? TcpProtocol.DefaultPort;
        NetworkAdapterName = raw?.NetworkAdapter ?? "";
        SelectedDiskId     = raw?.SelectedDiskId ?? "";

        RebuildHandshakeKey();

        if (needsSave) Save();
    }

    /// <summary>
    /// Called by CmdServer when the app requests key regeneration.
    /// Generates a new cert + secret and saves. Callers must restart TcpServer.
    /// </summary>
    public void RegenerateKey()
    {
        _cert = GenerateCert();
        _hmacSecret = RandomNumberGenerator.GetBytes(32);
        RebuildHandshakeKey();
        Save();
        _log.LogInformation("Handshake key regenerated");
        Changed?.Invoke();
    }

    public void SetTcpEnabled(bool enabled)
    {
        if (TcpEnabled == enabled) return;
        TcpEnabled = enabled;
        Save();
        Changed?.Invoke();
    }

    public void SetNetworkAdapter(string adapterName)
    {
        NetworkAdapterName = adapterName;
        Save();
        Changed?.Invoke();
    }

    // v1.21: called by CmdServer when the user picks a disk in Settings.
    // Persists to service.json and notifies listeners (HardwareMonitor
    // re-routes its perf counters live -- no service restart needed).
    public void SetSelectedDisk(string diskId)
    {
        SelectedDiskId = diskId;
        Save();
        Changed?.Invoke();
    }

    public X509Certificate2 GetCertificate()
        => _cert ?? throw new InvalidOperationException("Certificate not loaded");

    public byte[] GetHmacSecret()
        => _hmacSecret ?? throw new InvalidOperationException("HMAC secret not loaded");

    // ------------------------------------------------------------------

    private void RebuildHandshakeKey()
    {
        var fp = TcpProtocol.GetCertFingerprint(_cert!);
        HandshakeKey = TcpProtocol.EncodeHandshakeKey(fp, _hmacSecret!);
    }

    private void Save()
    {
        Directory.CreateDirectory(DataDir);
        File.WriteAllBytes(CertPath, _cert!.Export(X509ContentType.Pfx, "fluidMonitor"));
        var raw = new RawConfig
        {
            TcpEnabled      = TcpEnabled,
            TcpPort         = TcpPort,
            HmacSecret      = Convert.ToBase64String(_hmacSecret!),
            CertPfxExists   = true,
            NetworkAdapter  = NetworkAdapterName,
            SelectedDiskId  = SelectedDiskId
        };
        File.WriteAllText(ConfigPath,
            JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static X509Certificate2 GenerateCert()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=fluidMonitor",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(10));
        // Re-export with exportable key so we can persist the PFX
        return new X509Certificate2(
            cert.Export(X509ContentType.Pfx, "fluidMonitor"),
            "fluidMonitor",
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.MachineKeySet |
            X509KeyStorageFlags.Exportable);
    }

    private sealed class RawConfig
    {
        [JsonPropertyName("tcpEnabled")]      public bool    TcpEnabled    { get; set; }
        [JsonPropertyName("tcpPort")]         public int     TcpPort       { get; set; } = 5199;
        [JsonPropertyName("hmacSecret")]      public string? HmacSecret    { get; set; }
        [JsonPropertyName("certExists")]      public bool    CertPfxExists { get; set; }
        [JsonPropertyName("networkAdapter")]  public string  NetworkAdapter { get; set; } = "";
        [JsonPropertyName("selectedDiskId")]  public string  SelectedDiskId { get; set; } = "";
    }
}
