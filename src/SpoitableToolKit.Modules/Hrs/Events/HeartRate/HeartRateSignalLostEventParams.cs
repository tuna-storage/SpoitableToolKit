using SpoitableToolKit.Modules.Hrs.Devices.Interfaces;
using SpoitableToolKit.Modules.Hrs.Devices.Models;

namespace SpoitableToolKit.Modules.Hrs.Events.HeartRate;

/// <param name="Monitor">繋がったまま黙った心拍計。</param>
/// <param name="Connection">受けている入口。</param>
/// <param name="SilentFor">これだけ黙ったので心拍なしとみなした（その心拍計の <see cref="IHeartRateDevice.SilenceAfter"/>）。</param>
internal sealed record HeartRateSignalLostEventParams(MonitorIdentity Monitor, HeartRateConnection Connection, TimeSpan SilentFor) : HeartRateEventParams;
