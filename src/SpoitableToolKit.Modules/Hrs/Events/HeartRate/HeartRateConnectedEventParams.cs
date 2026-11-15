using SpoitableToolKit.Modules.Hrs.Devices.Models;

namespace SpoitableToolKit.Modules.Hrs.Events.HeartRate;

/// <param name="Monitor">繋がった心拍計。</param>
/// <param name="Connection">どの入口で受けているか。</param>
internal sealed record HeartRateConnectedEventParams(MonitorIdentity Monitor, HeartRateConnection Connection) : HeartRateEventParams;
