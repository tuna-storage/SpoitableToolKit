using SpoitableToolKit.Modules.Hrs.Devices.Models;

namespace SpoitableToolKit.Modules.Hrs.Events.HeartRate;

/// <param name="Monitor">切れた心拍計（直前の <see cref="HeartRateConnectedEventParams"/> と同じ）。</param>
/// <param name="Connection">切れるまで受けていた入口。</param>
/// <param name="Reason">なぜ切れたか。</param>
internal sealed record HeartRateDisconnectedEventParams(MonitorIdentity Monitor, HeartRateConnection Connection, HeartRateDisconnectReason Reason) : HeartRateEventParams;
