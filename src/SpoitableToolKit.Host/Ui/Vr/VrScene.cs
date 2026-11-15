using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Ui.Vr.Overlays;
using SpoitableToolKit.Host.Ui.Vr.Pointing;

namespace SpoitableToolKit.Host.Ui.Vr;

/// <summary>
/// ダッシュボードの外の板を 1 周ぶん回す: 手を読む → 板を置く → 両手で指す → 掴み始める。
/// 手は周に 1 回だけ読む（<see cref="IVrOverlays.ReadHands"/> は中で <c>UpdateActionState</c> を呼ぶ）。
/// 隠れている間も読む — 押し始めは前の周との違いで決めるので、読まない周があると、見ていない間に押したトリガーを出た瞬間に押したと取り違える。
/// ダッシュボードが開いている間は、どの板も出さない・指さない（板はダッシュボードに重なり、SteamVR のレーザーと 2 本になる）。
/// 開いているかは周に 1 回だけ読んで持ち主に渡す。
/// </summary>
/// <param name="captureAll">入力を全部こちらで預かるか（Layout Mode の間。<see cref="IVrOverlays.ReadHands"/>）。手を読む周の頭で聞くので、持ち主が周の中で出入りしたら効くのは次の周から。null なら預からない。</param>
internal sealed class VrScene(
    IVrOverlays overlays, ILogger logger, IReadOnlyList<IVrBoardOwner> owners, Func<long>? now = null, Func<bool>? captureAll = null)
{
    private readonly Func<bool> _captureAll = captureAll ?? (() => false);

    private readonly List<VrLaserTarget> _targets = [];

    public VrPointing Pointing { get; } = new(overlays, logger, now);

    public void Start()
    {
        Pointing.Start();

        foreach (var owner in owners) owner.Start();
    }

    /// <summary>
    /// 畳む。レーザーは押したままのボタンを離し、<c>:hover</c> を外す報せを送ってから点を消す
    /// （browser を捨てるのは <see cref="VrLoop"/> で、これより後）。
    /// </summary>
    public void Stop()
    {
        Pointing.Stop();

        for (var i = owners.Count - 1; i >= 0; i--) owners[i].Stop();
    }

    public void Tick()
    {
        var hands = overlays.ReadHands(_captureAll());
        var dashboardOpen = overlays.DashboardOpen();

        _targets.Clear();

        foreach (var owner in owners) owner.Place(hands, dashboardOpen, _targets);

        var pointed = Pointing.Tick(hands, _targets);

        foreach (var owner in owners) owner.Take(hands, pointed);
    }
}
