using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Wrist;

namespace SpoitableToolKit.Host.Ui.Vr.Window;

internal static class VrWindowPlacement
{
    public const float InFrontDistance = 0.7f;

    /// <summary>「目の前」: 目の高さからこれだけ下（真正面だと VRChat の視界の真ん中を塞ぐ）。</summary>
    public const float InFrontDrop = 0.1f;

    public const float AboveWristGap = 0.03f;

    /// <summary>掴んだままトリガーを引いて、手をこれだけ前へ押すと半分、これだけ手前へ引くと倍になる。</summary>
    public const float HalvingDistance = 0.3f;

    /// <summary>頭の向きの水平な成分の先に置き、顔の方へ向ける。真上・真下を見ていて水平な向きが決まらなければ、頭の上（字の上）の向きを裏返したものを使う。</summary>
    public static VrMatrix InFront(VrMatrix head)
    {
        var (hx, hy, hz) = head.Position;
        var (ax, _, az) = head.AxisZ;

        var (fx, fz) = Horizontal(-ax, -az) ?? Horizontal(head.AxisY.X, head.AxisY.Z) ?? (0f, -1f);

        var position = (hx + fx * InFrontDistance, hy - InFrontDrop, hz + fz * InFrontDistance);

        return FacingHead(position, head.Position);
    }

    /// <summary>手首に付けた窓の既定の置き場（手首の板から見た位置と向き）。手首の板の真上に同じ面で置き、窓の下端が板の上端の <see cref="AboveWristGap"/> 上に来る。</summary>
    public static VrMatrix OnWrist(float wristHeight, float windowHeight) =>
        VrMatrix.Translation(0f, wristHeight / 2f + AboveWristGap + windowHeight / 2f, 0f);

    /// <summary><paramref name="position"/> に置いて、見える面（+Z）を頭へ向ける。字の上はなるべく空間の上に合わせる（傾けない）。頭の真上・真下に置いたときは上の向きが決まらないので、字の右を空間の +X に取る。</summary>
    public static VrMatrix FacingHead((float X, float Y, float Z) position, (float X, float Y, float Z) head)
    {
        var z = Normalize((head.X - position.X, head.Y - position.Y, head.Z - position.Z)) ?? (0f, 0f, 1f);

        var x = Normalize(Cross((0f, 1f, 0f), z)) ?? (1f, 0f, 0f);
        var y = Cross(z, x);

        return new VrMatrix(
            x.X, y.X, z.X, position.X,
            x.Y, y.Y, z.Y, position.Y,
            x.Z, y.Z, z.Z, position.Z);
    }

    /// <summary>掴んだままトリガーを引いている間の横幅。<paramref name="pushed"/> は引き始めてから前へ押した長さ（メートル、手前へ引くと負）。掛け算で効く（押すたびに同じ割合で縮む）ので、小さい窓でも大きい窓でも同じ手応えになる。</summary>
    public static float Resized(float startWidth, float pushed) => startWidth * MathF.Pow(2f, -pushed / HalvingDistance);

    /// <summary>「前」の向き。窓の見える面の裏向き（窓は顔の方を向いているので、窓へ向かって押す向き）。</summary>
    public static (float X, float Y, float Z) Forward(VrMatrix window)
    {
        var (x, y, z) = window.AxisZ;
        return (-x, -y, -z);
    }

    private static (float X, float Z)? Horizontal(float x, float z)
    {
        var length = MathF.Sqrt(x * x + z * z);
        return length < 1e-4f ? null : (x / length, z / length);
    }

    private static (float X, float Y, float Z)? Normalize((float X, float Y, float Z) v)
    {
        var length = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return length < 1e-4f ? null : (v.X / length, v.Y / length, v.Z / length);
    }

    private static (float X, float Y, float Z) Cross((float X, float Y, float Z) a, (float X, float Y, float Z) b) =>
        (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}
