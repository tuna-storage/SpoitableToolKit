namespace SpoitableToolKit.Modules.Hrs.Avatar;

/// <summary>
/// 送る値を、新しく届いた値へ<b>なめらかに寄せる</b>（設定の「値をなめらかに動かす」）。
/// <para>
/// 決まり:
/// <list type="bullet">
///   <item><b>行き過ぎない</b>（寄せる先を越えない。止まる先の手前で減っていく形）</item>
///   <item><b>1 秒でほぼ寄り切る</b>（140 bpm の差までなら、1 秒後には 1 bpm より近い）</item>
///   <item><b>止まったあとの 1 つ目は寄せない</b>（前の値が無ければ、届いた値をそのまま使う。0 から寄せると、戻るたびに数字が 0 から駆け上がる）</item>
///   <item>寄せる量は<b>手と手の間の時間</b>で決める（送る手は拍の瞬間にも起きるので、間は一定ではない。間が 0 なら動かない）</item>
/// </list>
/// </para>
/// </summary>
internal static class HeartRateSmoothing
{
    /// <summary>寄せる速さ（時定数）。この時間で差が約 37% になり、1 秒（5 倍）で約 0.7% になる。</summary>
    private static readonly TimeSpan TimeConstant = TimeSpan.FromMilliseconds(200);

    public static float Approach(float? current, float target, TimeSpan elapsed, bool smooth)
    {
        if (!smooth || current is not { } from) return target;

        return from + (target - from) * (float)(1 - Math.Exp(-elapsed / TimeConstant));
    }
}
