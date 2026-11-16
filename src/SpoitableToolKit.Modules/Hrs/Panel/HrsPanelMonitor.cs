using SpoitableToolKit.Modules.Hrs.Devices.Models;

namespace SpoitableToolKit.Modules.Hrs.Panel;

/// <summary>「心拍計」タブの 1 行（見えている心拍計 1 台）。JSON の名前と並びは、この宣言の順。</summary>
/// <param name="Address">BLE のアドレス。アドレスの無いもの（時計）は null（0 を書式化すると 00:00:... という嘘になる）。</param>
/// <param name="Signal">最後に受けた広告の電波の強さ（dBm）。分からなければ null。</param>
/// <param name="LastSeen">最後に広告を受けた時刻（ISO 8601）。「〜前」はパネルが手元の時計で数える。分からなければ null。</param>
/// <param name="Connected">
/// いま繋がっている 1 台か。<b>アドレスで見る</b> — 名前の前方一致で見ると、「Polar」に繋がっているときに「Polar H10 …」にも印が付く。
/// </param>
internal sealed record HrsPanelMonitor(string Name, string? Address, int? Signal, string? LastSeen, bool Connected)
{
    public static HrsPanelMonitor From(MonitorSighting seen, MonitorIdentity? connected) => new(
        seen.Monitor.Name,
        seen.Monitor.Address != 0 ? MonitorIdentity.FormatAddress(seen.Monitor.Address) : null,
        seen.SignalDbm,
        seen.LastSeen?.ToString("O"),
        connected is { } here && here.IsSame(seen.Monitor));
}
