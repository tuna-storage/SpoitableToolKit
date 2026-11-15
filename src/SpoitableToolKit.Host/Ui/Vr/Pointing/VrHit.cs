namespace SpoitableToolKit.Host.Ui.Vr.Pointing;

/// <param name="X">ブラウザの画素。<b>左上が原点</b>（OpenVR のマウスの座標とは縦が逆 — <see cref="VrPointerMap"/> は通さない）。</param>
/// <param name="LocalX">板の中心から見た位置（メートル、右が正）。点を置くのに使う。</param>
/// <param name="LocalY">同上（上が正）。</param>
internal readonly record struct VrHit(int X, int Y, float LocalX, float LocalY, float Distance);
