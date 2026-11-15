using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Wrist;

namespace SpoitableToolKit.Host.Ui.Vr.Pointing;

internal static class VrBoardHit
{
    /// <summary>
    /// 狙い（<paramref name="aim"/> の -Z）が板に当たるか。
    /// 表からだけ当たる。見える面は +Z（<see cref="WristPlacement.FrontZ"/>）なので、板の +Z の側から -Z の向きへ進む光線だけを取る。
    /// 板の外 <paramref name="margin"/> までは当たったことにする（画素は端に挟む）。縁を指しているときに出たり消えたりしないよう、呼ぶ側は入るときと出るときで余白を変える。
    /// </summary>
    public static VrHit? Cast(VrMatrix aim, VrBoard board, float margin, float reach)
    {
        var inverse = board.Pose.Inverse();

        var (ox, oy, oz) = inverse.Apply(aim.Position);
        var (ax, ay, az) = aim.AxisZ;
        var (dx, dy, dz) = inverse.Rotate((-ax, -ay, -az));

        if (oz <= 0f || dz >= -1e-6f) return null;

        var distance = -oz / dz;

        if (distance > reach) return null;

        var x = ox + distance * dx;
        var y = oy + distance * dy;

        if (MathF.Abs(x) > board.Width / 2f + margin || MathF.Abs(y) > board.Height / 2f + margin) return null;

        var px = Pixel((x / board.Width + 0.5f) * board.PixelWidth, board.PixelWidth);
        var py = Pixel((0.5f - y / board.Height) * board.PixelHeight, board.PixelHeight);

        return new VrHit(px, py, x, y, distance);
    }

    private static int Pixel(float value, int size) => (int)Math.Clamp(value, 0f, size - 1);
}
