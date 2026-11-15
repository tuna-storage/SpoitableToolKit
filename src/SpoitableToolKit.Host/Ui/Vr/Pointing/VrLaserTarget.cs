using SpoitableToolKit.Host.Ui.Vr.Overlays;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;

namespace SpoitableToolKit.Host.Ui.Vr.Pointing;

/// <param name="Handle">板の handle。SteamVR の当たりと突き合わせるのに使い（<see cref="IVrOverlays.Intersect"/>）、どの板を指したかもこれで返す。</param>
/// <param name="Device">板が付いている機器。点はこれに付ける — 板と一緒に動くので、板の上で震えない。null なら板は空間に置いてある（空間の窓）。そのときは点も空間に置く（板も点も同じ流儀なので、やはり震えない）。</param>
/// <param name="DevicePose">その機器の今の姿勢。空間に置いた板では <see cref="VrMatrix.Identity"/>。</param>
/// <param name="Offset">機器から見た板の置き場。空間に置いた板では、立った姿勢の原点から見た置き場そのもの。</param>
/// <param name="Clickable">いま押せる物の上か（CEF のカーソルが手の形か）。乗ったときの振動に使う。</param>
/// <param name="Scrolls">トリガーを引いたまま動かすと巻くか。手首は巻かない（巻く中身が無い）。スティックでは巻かない — VRChat の移動・回転にも素通しで効くため。</param>
/// <param name="OnlyFor">この手でしか指さない（手首は付けた手の反対だけ）。null なら両手。</param>
/// <param name="Grabbable">いま掴める物の上か（CEF のカーソルが CSS の <c>cursor: grab</c> / <c>grabbing</c> か）。巻く板でも、ここで引いたら巻かずに押す・動かす・離すをそのまま送る（設定のモーダルのつまみ <c>⠿</c> など、ページが自分で掴んで動かすもの）。null なら掴める物は無い。</param>
internal sealed record VrLaserTarget(
    ulong Handle, uint? Device, VrMatrix DevicePose, VrMatrix Offset,
    float Width, float Height, int PixelWidth, int PixelHeight,
    VrHost.PointerSink Sink, Func<bool> Clickable, bool Scrolls, string Name, VrHand? OnlyFor = null, Func<bool>? Grabbable = null)
{
    public VrBoard Board => new(DevicePose.Then(Offset), Width, Height, PixelWidth, PixelHeight);
}
