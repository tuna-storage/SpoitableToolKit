using SpoitableToolKit.Modules.Hrs.Devices.Models;

namespace SpoitableToolKit.Modules.Hrs.Panel;

/// <summary>「心拍計」タブの 1 行（見えている心拍計 1 台）。JSON の名前と並びは、この宣言の順。</summary>
/// <param name="Address">BLE のアドレス。アドレスの無いもの（時計）は null（0 を書式化すると 00:00:... という嘘になる）。</param>
/// <param name="Signal">最後に受けた広告の電波の強さ（dBm）。分からなければ null（繋がっている 1 台が広告をやめたときも — <see cref="From"/>）。</param>
/// <param name="LastSeen">
/// 最後に広告を受けた時刻（ISO 8601）。「〜前」はパネルが手元の時計で数える。分からなければ null（繋がっている 1 台が広告をやめたときは「いま」）。
/// </param>
/// <param name="Connected">
/// いま繋がっている 1 台か。<b>アドレスで見る</b> — 名前の前方一致で見ると、「Polar」に繋がっているときに「Polar H10 …」にも印が付く。
/// </param>
internal sealed record HrsPanelMonitor(string Name, string? Address, int? Signal, string? LastSeen, bool Connected)
{
    /// <summary>
    /// 繋がっている 1 台の最後の広告がこれより古ければ、広告をやめたとみなす。GATT で繋ぐと広告を止める機種があり、
    /// 残っているのは繋いだときの広告なので、そのまま出すと「接続中・2 時間前」になる。
    /// </summary>
    internal static readonly TimeSpan FreshAdvertisement = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 1 行を作る。繋がっている 1 台が広告をやめていれば、最後に見た時刻は「いま」（繋がっている間は居るのが分かっている）、
    /// 電波の強さは出さない（繋がりからは読めない。古い数字を出すと、離れても強いままに見える）。広告を続けている機種はそのまま出す。
    /// </summary>
    public static HrsPanelMonitor From(MonitorSighting seen, MonitorIdentity? connected, DateTimeOffset now)
    {
        var here = connected is { } link && link.IsSame(seen.Monitor);
        var quiet = here && (seen.LastSeen is not { } at || now - at > FreshAdvertisement);

        return new(
            seen.Monitor.Name,
            seen.Monitor.Address != 0 ? MonitorIdentity.FormatAddress(seen.Monitor.Address) : null,
            quiet ? null : seen.SignalDbm,
            (quiet ? now : seen.LastSeen)?.ToString("O"),
            here);
    }
}
