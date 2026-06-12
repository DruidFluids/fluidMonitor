using System;

namespace Fluid.Shared.Protocol;

public class RemoteDevice
{
    public Guid   Id           { get; set; } = Guid.NewGuid();
    public string Name         { get; set; } = "";
    public string IpAddress    { get; set; } = "";
    public int    Port         { get; set; } = TcpProtocol.DefaultPort;
    public string HandshakeKey { get; set; } = "";

    public PopoutSettings Popout { get; set; } = new();
}
