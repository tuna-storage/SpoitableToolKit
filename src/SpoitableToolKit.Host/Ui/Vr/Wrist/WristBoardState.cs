using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Window;

namespace SpoitableToolKit.Host.Ui.Vr.Wrist;

/// <summary>見えている間だけ（消えていく途中も含む）。空間の窓を手首の板の上に付けるのに使う（<see cref="VrWindowHost"/>）。</summary>
/// <param name="Device">板を付けている機器（コントローラー。開発用の頭の前では 0 = 頭）。</param>
/// <param name="Alpha">今の透明度（0〜1）。出入りでフェードしている間は 1 より小さい。窓も同じ値で揃える。</param>
/// <param name="Live">出ている（指してよい）か。消えていく途中は false — 薄くなっていく板にレーザーを当てさせない。</param>
internal sealed record WristBoardState(
    VrHand Hand, uint Device, VrMatrix DevicePose, VrMatrix Offset, float Height, float Alpha = 1f, bool Live = true)
{
    public VrMatrix Pose => DevicePose.Then(Offset);
}
