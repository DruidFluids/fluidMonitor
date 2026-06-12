using System;
using System.Collections.Generic;
using System.Linq;
using Fluid.App.Models;
using Fluid.Shared.Protocol;

namespace Fluid.App.Services;

/// <summary>
/// Owns the RemoteTcpClient and SensorState for each configured remote device.
/// Clients are lazily started and never stopped until the device is removed.
/// </summary>
public sealed class DeviceManager : IDisposable
{
    private readonly Dictionary<Guid, (RemoteTcpClient client, SensorState state)> _remotes = new();

    /// <summary>Get or create the SensorState+client for a remote device.</summary>
    public SensorState GetOrCreate(RemoteDevice device)
    {
        if (_remotes.TryGetValue(device.Id, out var existing))
            return existing.state;

        var state  = new SensorState();
        state.ExternalWarnings = device.Popout.Warnings;
        var client = new RemoteTcpClient(device);
        state.Attach(client);
        client.Start();

        _remotes[device.Id] = (client, state);
        return state;
    }

    /// <summary>Remove a device — stops its client connection.</summary>
    public void Remove(Guid deviceId)
    {
        if (_remotes.TryGetValue(deviceId, out var entry))
        {
            entry.client.Dispose();
            _remotes.Remove(deviceId);
        }
    }

    /// <summary>Returns true if the device has an active TCP connection.</summary>
    public bool IsConnected(Guid deviceId)
        => _remotes.TryGetValue(deviceId, out var e) && e.client.IsConnected;

    public void Dispose()
    {
        foreach (var (_, e) in _remotes) e.client.Dispose();
        _remotes.Clear();
    }
}
