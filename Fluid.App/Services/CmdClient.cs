using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Tasks;
using Fluid.Shared.Protocol;

namespace Fluid.App.Services;

/// <summary>
/// Sends single commands to the running fluidsvc CmdServer and reads the response.
/// Each call opens a fresh pipe connection.
/// </summary>
public static class CmdClient
{
    private static async Task<JsonDocument?> SendAsync(object command)
    {
        using var pipe = new NamedPipeClientStream(".", TcpProtocol.CmdPipeName,
            PipeDirection.InOut, PipeOptions.None);
        await pipe.ConnectAsync(3000);

        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);

        await writer.WriteLineAsync(JsonSerializer.Serialize(command));
        var response = await reader.ReadLineAsync();
        return string.IsNullOrEmpty(response) ? null : JsonDocument.Parse(response);
    }

    /// <summary>Returns the current service config (tcpEnabled, port, handshakeKey, networkAdapter).</summary>
    public static async Task<(bool tcpEnabled, int port, string handshakeKey, string networkAdapter)> GetConfigAsync()
    {
        using var doc = await SendAsync(new { type = "getConfig" });
        if (doc == null) throw new IOException("No response from service");
        var r = doc.RootElement;
        return (
            r.GetProperty("tcpEnabled").GetBoolean(),
            r.GetProperty("tcpPort").GetInt32(),
            r.GetProperty("handshakeKey").GetString() ?? "",
            r.TryGetProperty("networkAdapter", out var na) ? na.GetString() ?? "" : ""
        );
    }

    /// <summary>Enable or disable the TCP remote monitoring server.</summary>
    public static async Task SetTcpEnabledAsync(bool enabled)
        => await SendAsync(new { type = "setTcpEnabled", enabled });

    /// <summary>Set the network adapter to monitor (empty = sum all).</summary>
    public static async Task SetNetworkAdapterAsync(string adapterName)
        => await SendAsync(new { type = "setNetworkAdapter", name = adapterName });

    /// <summary>v1.21: route the Disk tile to a physical disk index (empty = all disks aggregate).</summary>
    public static async Task SetSelectedDiskAsync(string diskId)
        => await SendAsync(new { type = "setSelectedDisk", id = diskId });

    /// <summary>
    /// v1.25: ask the service to re-enumerate sensors after the user installed
    /// or removed the optional CPU-temp driver. Returns true if CPU temperature
    /// is now available. Lets the tile light up without a service restart.
    /// </summary>
    public static async Task<bool> RecheckSensorsAsync()
    {
        using var doc = await SendAsync(new { type = "recheckSensors" });
        return doc?.RootElement.TryGetProperty("cpuTempAvailable", out var p) == true && p.GetBoolean();
    }

    /// <summary>Regenerate the TLS cert and HMAC secret. Returns the new Handshake Key.</summary>
    public static async Task<string> RegenerateKeyAsync()
    {
        using var doc = await SendAsync(new { type = "regenerateKey" });
        return doc?.RootElement.GetProperty("handshakeKey").GetString() ?? "";
    }
}
