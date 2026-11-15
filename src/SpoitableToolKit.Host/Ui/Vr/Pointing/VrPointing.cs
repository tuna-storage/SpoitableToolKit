using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Ui.Vr.Overlays;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;

namespace SpoitableToolKit.Host.Ui.Vr.Pointing;

/// <summary>
/// 自前のポインタ 2 本（両手）。点は手ごとに 1 つ — 手首と窓とで別々にレーザーを持つと、同じ手に点が 2 つ出る。
/// 1 枚の板を指せるのは 1 本の手だけ（先に指した手が持つ）。CEF のマウスは 1 つなので、両手で同じ窓を指すとカーソルが周ごとに両手の間を跳ぶ。
/// </summary>
internal sealed class VrPointing(IVrOverlays overlays, ILogger logger, Func<long>? now = null)
{
    public const string Key = "dev.f2a.spoitable.pointer";

    private readonly VrLaser _left = new(overlays, logger, Key + ".left", now);

    private readonly VrLaser _right = new(overlays, logger, Key + ".right", now);

    public VrLaser For(VrHand hand) => hand == VrHand.Left ? _left : _right;

    public void Start()
    {
        _left.Start();
        _right.Start();
    }

    public void Stop()
    {
        _left.Stop();
        _right.Stop();
    }

    /// <summary>
    /// 1 周。<paramref name="targets"/> はいま指してよい板の全部。
    /// 手ごとに、その手で指してよい板（<see cref="VrLaserTarget.OnlyFor"/>）から、もう一方の手が持っていない板を渡す。
    /// </summary>
    public VrPointed Tick(VrHands hands, IReadOnlyList<VrLaserTarget> targets)
    {
        var left = _left.Tick(VrHand.Left, hands.Left, For(VrHand.Left, targets, _right.Target));
        var right = _right.Tick(VrHand.Right, hands.Right, For(VrHand.Right, targets, _left.Target));

        // どちらの手でも、板を押したら流れを止める（片方の手で窓を流したまま、もう片方の手で手首のショートカットを押して面を替える、など）。
        // 流れている板を反対の手で指したら、板はその手のものになり、流していた手からは指せなくなって流れが止まる（VrLaser.Glide）。
        // なので反対の手の 1 押しはふつうのクリックになる（止めるだけにするのは、流していた手で押したときだけ）。
        if (left.Pressed) _right.StopGliding();
        if (right.Pressed) _left.StopGliding();

        return new VrPointed(left, right);
    }

    private static IReadOnlyList<VrLaserTarget> For(VrHand hand, IReadOnlyList<VrLaserTarget> targets, ulong takenByTheOtherHand)
    {
        if (targets.Count == 0) return targets;

        List<VrLaserTarget> mine = new(targets.Count);

        foreach (var target in targets)
        {
            if (target.OnlyFor is { } only && only != hand) continue;
            if (target.Handle == takenByTheOtherHand) continue;

            mine.Add(target);
        }

        return mine;
    }
}
