namespace SpoitableToolKit.Modules.Hrs.Devices.Models;

/// <summary>心拍計 1 台のいまの様子（<see cref="HeartRateManager.Current"/>）。</summary>
internal sealed record HeartRateDeviceState(
    MonitorIdentity Identity,
    HeartRateConnection Connection,
    HeartRateDeviceStatus Status,
    int? Battery);
