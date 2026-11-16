namespace SpoitableToolKit.Modules.Hrs.Devices.Models;

/// <summary>
/// 見えている心拍計と、最後に広告を受けたときの様子。パネルの「心拍計」タブに出す。
/// <b>同じ心拍計かどうかは <see cref="Monitor"/> で見る</b> — 電波の強さと時刻は広告のたびに変わるので、
/// <see cref="MonitorIdentity"/> には持たせない（持たせると、繋がりを比べる所が広告のたびに「別の心拍計」と見る）。
/// </summary>
/// <param name="SignalDbm">最後に受けた広告の電波の強さ（dBm）。分からなければ null（時計 = HDS など）。</param>
/// <param name="LastSeen">最後に広告を受けた時刻。分からなければ null。</param>
internal readonly record struct MonitorSighting(MonitorIdentity Monitor, int? SignalDbm, DateTimeOffset? LastSeen);
