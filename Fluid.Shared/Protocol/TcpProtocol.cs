using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Fluid.Shared.Protocol;

public static class TcpProtocol
{
    public const int    DefaultPort     = 5199;
    public const string KeyPrefix       = "FM1:";
    public const string CmdPipeName     = "fluidMonitor-cmd";

    /// <summary>
    /// Encodes cert fingerprint + HMAC secret into the user-facing Handshake Key.
    /// Format: FM1:<base64(certSHA256[32] || hmacSecret[32])> = 92 chars total.
    /// </summary>
    public static string EncodeHandshakeKey(byte[] certFingerprint, byte[] hmacSecret)
    {
        var combined = new byte[64];
        certFingerprint.CopyTo(combined, 0);
        hmacSecret.CopyTo(combined, 32);
        return KeyPrefix + Convert.ToBase64String(combined);
    }

    /// <summary>
    /// Decodes a Handshake Key back into fingerprint + HMAC secret.
    /// Returns false if the key is invalid.
    /// </summary>
    public static bool TryDecodeHandshakeKey(string key,
        out byte[] certFingerprint, out byte[] hmacSecret)
    {
        certFingerprint = Array.Empty<byte>();
        hmacSecret      = Array.Empty<byte>();
        try
        {
            if (!key.StartsWith(KeyPrefix, StringComparison.Ordinal)) return false;
            var raw = Convert.FromBase64String(key[KeyPrefix.Length..]);
            if (raw.Length != 64) return false;
            certFingerprint = raw[..32];
            hmacSecret      = raw[32..];
            return true;
        }
        catch { return false; }
    }

    /// <summary>Compute SHA-256 fingerprint of a certificate's raw bytes.</summary>
    public static byte[] GetCertFingerprint(X509Certificate2 cert)
        => SHA256.HashData(cert.RawData);

    /// <summary>Compute HMAC-SHA256 of a challenge nonce using the shared secret.</summary>
    public static byte[] ComputeHmac(byte[] nonce, byte[] secret)
    {
        using var hmac = new HMACSHA256(secret);
        return hmac.ComputeHash(nonce);
    }
}
