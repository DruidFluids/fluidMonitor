using System.Text.Json;

namespace Fluid.Shared.Protocol;

public static class PipeProtocol
{
    /// <summary>
    /// Local named pipe path is "\\.\pipe\fluid" — clients connect via
    /// new NamedPipeClientStream(".", PipeName, ...).
    /// </summary>
    public const string PipeName = "fluidMonitor";

    /// <summary>Connect timeout used by the client.</summary>
    public const int ClientConnectTimeoutMs = 2000;

    /// <summary>Reconnect backoff after a disconnect or failed connect.</summary>
    public const int ClientReconnectDelayMs = 2000;

    /// <summary>How often the service samples sensors.</summary>
    public const int ServerPollIntervalMs = 1000;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,                   // one line per snapshot
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
