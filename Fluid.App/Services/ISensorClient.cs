using System;
using Fluid.Shared.Protocol;

namespace Fluid.App.Services;

public interface ISensorClient
{
    event Action<SensorSnapshot>? SnapshotReceived;
    event Action<bool>? ConnectionStateChanged;
}
