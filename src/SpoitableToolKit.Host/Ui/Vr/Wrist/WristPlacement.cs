using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Pointing;

namespace SpoitableToolKit.Host.Ui.Vr.Wrist;

internal static class WristPlacement
{
    /// <summary>
    /// 板の見える面が向いている Z の向き。openvr.h には「-Z points out of the overlay」としか無く、どちらが見える面かは読み切れなかったので実測で決めた:
    /// 開発用の置き方（<see cref="InFrontOfHead"/>）で頭の前に回転なしで置くと、字が左右反転せずに読めた。頭は -Z を見ているので、板の +Z が頭の方を向く。
    /// </summary>
    public const float FrontZ = 1f;

    /// <summary>
    /// 既定で、板の真ん中が視野の真ん中（頭の向き）からこれより近くなったら出す（度。<see cref="Gaze"/>）。出す側と隠す側で値を分けて、境目で止めたときに出たり消えたりを繰り返さない。
    /// 面の向きだけで決めると、手首をこちらへ向けていれば前を見ていても出てしまうので、決め手は視線にして、面の向きは裏・真横を弾くだけにした（<see cref="FacingShowWithinDegrees"/>）。
    /// 設定で変えられる（<c>HostConfig.WristShowWithinDegrees</c>）。
    /// </summary>
    public const int DefaultShowWithinDegrees = 30;

    /// <summary>既定で、出ている板の真ん中が視野の真ん中からこれより離れたら隠す（度）。設定は <c>HostConfig.WristHideBeyondDegrees</c>。</summary>
    public const int DefaultHideBeyondDegrees = 45;

    /// <summary>板の面が目の方をこれより大きく外れていたら出さない（度。<see cref="Facing"/>）。決め手は視線で、こちらは裏・真横を弾くだけなので緩く、設定には出さない。</summary>
    public const int FacingShowWithinDegrees = 60;

    /// <summary>出ている板の面が目の方をこれより大きく外れたら隠す（度）。</summary>
    public const int FacingHideBeyondDegrees = 75;

    public static readonly float FacingShowWhenCosAbove = MathF.Cos(FacingShowWithinDegrees * MathF.PI / 180f);

    public static readonly float FacingHideWhenCosAbove = MathF.Cos(FacingHideBeyondDegrees * MathF.PI / 180f);

    /// <summary>角度の設定で受ける範囲（度）。設定の入力の範囲と同じ。手で config.json を書き換えて外れた値も、ここへ丸める。</summary>
    public const int MinDegrees = 5;

    /// <inheritdoc cref="MinDegrees"/>
    public const int MaxDegrees = 90;

    /// <summary>
    /// 既定の置き場。コントローラーから見た板の位置と向き。XSOverlay の手首の板と被りにくい所に置く（XSOverlay を使う人は多く、腕時計の位置だと手首で被り、近くに置くと両方がレーザーに反応する）。
    /// 値は左手の置き場（幅 22 cm で合わせたもの）を 4 桁に丸め、歪みを取ったもの。見える面はおおむね手の甲の側（-X）で、手の甲の側へ 12 cm・下へ 11 cm・手首の方へ 20 cm。右手は鏡写し。
    /// </summary>
    public static VrMatrix DefaultOffset(VrHand hand) => hand == VrHand.Left ? LeftDefault : LeftDefault.MirroredX();

    private static readonly VrMatrix LeftDefault = new VrMatrix(
        0.0964f, -0.5599f, -0.8231f, -0.1162f,
        -0.6036f, -0.6905f, 0.3990f, -0.1119f,
        -0.7916f, 0.4583f, -0.4044f, 0.1950f).Orthonormalized();

    /// <summary>開発用（<c>SPOITABLE_VR_WRIST=hmd</c>）。頭の前に固定する。仮想ヘッドセット（null ドライバ）にはコントローラーが無いので、こうしないと絵を確かめられない。</summary>
    public static VrMatrix InFrontOfHead => VrMatrix.Translation(0f, -0.05f, -0.45f);

    /// <summary>板の見える面が目の方をどれだけ向いているか（cos）。1 で真正面、0 で真横、負なら裏。</summary>
    public static float Facing(VrMatrix head, VrMatrix board)
    {
        var (hx, hy, hz) = head.Position;
        var (bx, by, bz) = board.Position;

        var (tx, ty, tz) = (hx - bx, hy - by, hz - bz);
        var length = MathF.Sqrt(tx * tx + ty * ty + tz * tz);

        // 目と板が同じ場所にある。向きは決められないので「向いていない」とする。
        if (length < 1e-4f) return -1f;

        var (fx, fy, fz) = board.AxisZ;
        return (fx * tx + fy * ty + fz * tz) * FrontZ / length;
    }

    /// <summary>
    /// 板の真ん中が頭の向きからどれだけ外れているか（cos）。1 で視野の真ん中、0 で真横、負なら後ろ。
    /// ⚠ 頭の向き（HMD の -Z）であって、目の向きではない（OpenVR では視線を追えない）。手首は目だけ下げて見ることも多いので、出す角度は「真ん中に当たっているか」より広く取る（<see cref="DefaultShowWithinDegrees"/>）。
    /// </summary>
    public static float Gaze(VrMatrix head, VrMatrix board)
    {
        var (hx, hy, hz) = head.Position;
        var (bx, by, bz) = board.Position;

        var (tx, ty, tz) = (bx - hx, by - hy, bz - hz);
        var length = MathF.Sqrt(tx * tx + ty * ty + tz * tz);

        // 目と板が同じ場所にある。向きは決められないので「見ていない」とする。
        if (length < 1e-4f) return -1f;

        var (fx, fy, fz) = head.AxisZ;
        return -(fx * tx + fy * ty + fz * tz) / length;
    }

    /// <summary>出すか隠すか。板の真ん中が視野の真ん中の近くにあり（<paramref name="gaze"/>）、面がこちらを向いている（<paramref name="facing"/>）とき出す。今の状態で閾値を変える（出ているなら隠す側、隠れているなら出す側）。</summary>
    public static bool ShouldShow(bool shown, float facing, float gaze, WristAngles angles) =>
        shown
            ? facing >= FacingHideWhenCosAbove && gaze >= angles.HideWhenCosAbove
            : facing >= FacingShowWhenCosAbove && gaze >= angles.ShowWhenCosAbove;
}
