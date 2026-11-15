using SpoitableToolKit.Modules.Hrs.Devices.Models;

namespace SpoitableToolKit.Modules.Hrs.Events.HeartRate;

/// <param name="Monitor">また届き始めた心拍計（直前の <see cref="HeartRateSignalLostEventParams"/> と同じ）。</param>
/// <param name="Connection">受けている入口。</param>
internal sealed record HeartRateSignalBackEventParams(MonitorIdentity Monitor, HeartRateConnection Connection) : HeartRateEventParams;
