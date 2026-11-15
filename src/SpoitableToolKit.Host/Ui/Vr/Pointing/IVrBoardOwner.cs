using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;

namespace SpoitableToolKit.Host.Ui.Vr.Pointing;

/// <summary>
/// ダッシュボードの外の板の持ち主（手首・空間の窓）。周の中で 2 回呼ばれる（<see cref="VrScene.Tick"/>）。
/// 手ごとにいちばん近い板を指すため、全部の板が置かれてから決める。
/// </summary>
internal interface IVrBoardOwner
{
    void Start();

    void Stop();

    /// <summary>
    /// 周の前半: 板を置き、いま指してよい板を <paramref name="targets"/> に足す（隠れている・掴んでいる間は足さない）。
    /// 転んでも投げない（自分だけ畳む。ほかの板とダッシュボードは動き続ける）。
    /// </summary>
    /// <param name="dashboardOpen">SteamVR のダッシュボードが開いている。開いている間は板を隠す（ダッシュボードに重なるため）。閉じてはいない — 閉じたら同じ所・同じ画面で戻る。</param>
    void Place(VrHands hands, bool dashboardOpen, List<VrLaserTarget> targets);

    /// <summary>周の後半: レーザーの答えを受ける。転んでも投げない。</summary>
    void Take(VrHands hands, VrPointed pointed);
}
