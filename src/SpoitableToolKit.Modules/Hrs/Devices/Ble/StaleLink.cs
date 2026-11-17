using SpoitableToolKit.Modules.Hrs.Devices.Models;

namespace SpoitableToolKit.Modules.Hrs.Devices.Ble;

/// <summary>掴んだまま線が落ちていて、手放して繋ぎ直す相手（<see cref="LinkTakeover"/>）。</summary>
/// <param name="DownFor">線が落ちてからの長さ（ログに書く）。</param>
internal readonly record struct StaleLink(MonitorIdentity Monitor, TimeSpan DownFor);
