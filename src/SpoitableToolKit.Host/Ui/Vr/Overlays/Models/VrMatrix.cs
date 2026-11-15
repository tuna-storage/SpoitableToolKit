using SpoitableToolKit.Host.Ui.Vr.Wrist;

namespace SpoitableToolKit.Host.Ui.Vr.Overlays.Models;

/// <summary>OpenVR の 3×4 行列（<c>HmdMatrix34_t</c>）と同じ並び（行優先、4 列目が位置）。剛体（回転 + 移動）しか扱わないので、逆行列は転置で済ませている。</summary>
internal readonly record struct VrMatrix(
    float M0, float M1, float M2, float M3,
    float M4, float M5, float M6, float M7,
    float M8, float M9, float M10, float M11)
{
    public static VrMatrix Identity => new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0);

    public static VrMatrix Translation(float x, float y, float z) => new(1, 0, 0, x, 0, 1, 0, y, 0, 0, 1, z);

    public static VrMatrix RotationX(float degrees)
    {
        var (s, c) = SinCos(degrees);
        return new(1, 0, 0, 0, 0, c, -s, 0, 0, s, c, 0);
    }

    public static VrMatrix RotationY(float degrees)
    {
        var (s, c) = SinCos(degrees);
        return new(c, 0, s, 0, 0, 1, 0, 0, -s, 0, c, 0);
    }

    public (float X, float Y, float Z) Position => (M3, M7, M11);

    /// <summary>点を写す（回してから動かす）。この行列の座標系の点を、外の座標系へ。</summary>
    public (float X, float Y, float Z) Apply((float X, float Y, float Z) point)
    {
        var (x, y, z) = point;
        return (M0 * x + M1 * y + M2 * z + M3, M4 * x + M5 * y + M6 * z + M7, M8 * x + M9 * y + M10 * z + M11);
    }

    public (float X, float Y, float Z) Rotate((float X, float Y, float Z) direction)
    {
        var (x, y, z) = direction;
        return (M0 * x + M1 * y + M2 * z, M4 * x + M5 * y + M6 * z, M8 * x + M9 * y + M10 * z);
    }

    public (float X, float Y, float Z) AxisZ => (M2, M6, M10);

    /// <summary>この行列が向けている X 軸（1 列目）。板なら字の右の向き。</summary>
    public (float X, float Y, float Z) AxisX => (M0, M4, M8);

    /// <summary>この行列が向けている Y 軸（2 列目）。板なら字の上の向き。</summary>
    public (float X, float Y, float Z) AxisY => (M1, M5, M9);

    /// <summary><c>this * other</c>。右から掛けたものが先に効く（other の座標系を this の座標系へ）。</summary>
    public VrMatrix Then(VrMatrix other) => new(
        M0 * other.M0 + M1 * other.M4 + M2 * other.M8,
        M0 * other.M1 + M1 * other.M5 + M2 * other.M9,
        M0 * other.M2 + M1 * other.M6 + M2 * other.M10,
        M0 * other.M3 + M1 * other.M7 + M2 * other.M11 + M3,
        M4 * other.M0 + M5 * other.M4 + M6 * other.M8,
        M4 * other.M1 + M5 * other.M5 + M6 * other.M9,
        M4 * other.M2 + M5 * other.M6 + M6 * other.M10,
        M4 * other.M3 + M5 * other.M7 + M6 * other.M11 + M7,
        M8 * other.M0 + M9 * other.M4 + M10 * other.M8,
        M8 * other.M1 + M9 * other.M5 + M10 * other.M9,
        M8 * other.M2 + M9 * other.M6 + M10 * other.M10,
        M8 * other.M3 + M9 * other.M7 + M10 * other.M11 + M11);

    /// <summary>逆。剛体のときだけ正しい（回転を転置し、位置を逆向きに回す）。</summary>
    public VrMatrix Inverse() => new(
        M0, M4, M8, -(M0 * M3 + M4 * M7 + M8 * M11),
        M1, M5, M9, -(M1 * M3 + M5 * M7 + M9 * M11),
        M2, M6, M10, -(M2 * M3 + M6 * M7 + M10 * M11));

    /// <summary>
    /// 回転の部分を正規直交に戻す（位置はそのまま）。SteamVR の姿勢は浮動小数で少し剛体から外れていて、掴んだ置き場に乗ったその外れが掴み直すたびに積み重なる（6 回ほどで軸の長さが 1.00015 になった）。
    /// <see cref="Inverse"/> は剛体を前提に転置で済ませているので、放っておくと逆も合わなくなる。
    /// X 軸を先に揃え、Y 軸から X の成分を除き、Z は X × Y で作る（グラム・シュミット）。潰れた行列（軸の長さが 0 に近い）は直せないので、そのまま返す。
    /// </summary>
    public VrMatrix Orthonormalized()
    {
        var (xx, xy, xz) = Normalize(M0, M4, M8);

        var dot = M1 * xx + M5 * xy + M9 * xz;
        var (yx, yy, yz) = Normalize(M1 - dot * xx, M5 - dot * xy, M9 - dot * xz);

        if (float.IsNaN(xx) || float.IsNaN(yx)) return this;

        var (zx, zy, zz) = (xy * yz - xz * yy, xz * yx - xx * yz, xx * yy - xy * yx);

        return new(xx, yx, zx, M3, xy, yy, zy, M7, xz, yz, zz, M11);

        static (float, float, float) Normalize(float x, float y, float z)
        {
            var length = MathF.Sqrt(x * x + y * y + z * z);
            return length < 1e-6f ? (float.NaN, float.NaN, float.NaN) : (x / length, y / length, z / length);
        }
    }

    /// <summary>左右の鏡写し（X を反す）。回転は右手系のまま（S·M·S、S = diag(-1, 1, 1)）。左手で決めた置き場を右手へ持っていくときに使う。</summary>
    public VrMatrix MirroredX() => new(
        M0, -M1, -M2, -M3,
        -M4, M5, M6, M7,
        -M8, M9, M10, M11);

    public float[] ToArray() => [M0, M1, M2, M3, M4, M5, M6, M7, M8, M9, M10, M11];

    /// <summary>保存したものから戻す。数が合わない・数でないものが混ざっていれば無かったことにする。</summary>
    public static VrMatrix? FromArray(float[]? values)
    {
        if (values is not { Length: 12 }) return null;
        if (values.Any(v => !float.IsFinite(v))) return null;

        return new(
            values[0], values[1], values[2], values[3],
            values[4], values[5], values[6], values[7],
            values[8], values[9], values[10], values[11]);
    }

    private static (float Sin, float Cos) SinCos(float degrees)
    {
        var radians = degrees * MathF.PI / 180f;
        return (MathF.Sin(radians), MathF.Cos(radians));
    }
}
