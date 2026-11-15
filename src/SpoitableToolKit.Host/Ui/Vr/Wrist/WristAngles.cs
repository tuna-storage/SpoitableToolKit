namespace SpoitableToolKit.Host.Ui.Vr.Wrist;

/// <summary>手首の板を出す・隠す閾値（設定の角度）。<see cref="WristPlacement.Gaze"/> と比べるので cos で持つ（角度が小さいほど大きい）。</summary>
/// <param name="ShowWhenCosAbove">隠れている板を、これ以上で出す。</param>
/// <param name="HideWhenCosAbove">出ている板を、これ以上なら出したままにする（下回ったら隠す）。</param>
internal readonly record struct WristAngles(float ShowWhenCosAbove, float HideWhenCosAbove)
{
    public static WristAngles Default { get; } = FromDegrees(WristPlacement.DefaultShowWithinDegrees, WristPlacement.DefaultHideBeyondDegrees);

    /// <summary>範囲の外は丸める。隠す角度は出す角度より小さくしない（小さいと、出した次の周で隠れる）。</summary>
    public static WristAngles FromDegrees(int showWithin, int hideBeyond)
    {
        var show = Math.Clamp(showWithin, WristPlacement.MinDegrees, WristPlacement.MaxDegrees);
        var hide = Math.Clamp(Math.Max(hideBeyond, show), WristPlacement.MinDegrees, WristPlacement.MaxDegrees);
        return new WristAngles(Cos(show), Cos(hide));
    }

    private static float Cos(int degrees) => MathF.Cos(degrees * MathF.PI / 180f);
}
