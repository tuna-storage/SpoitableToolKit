using SpoitableToolKit.Abstractions.Osc;
using SpoitableToolKit.Modules.Hrs.Config;

namespace SpoitableToolKit.Modules.Hrs.Avatar.Presets;

/// <summary>
/// VRCOSC のプリセット。VRCOSC の心拍モジュールを既定の設定のまま使うアバターに合わせた名前と値を送る。
/// 名前は設定で変えられる（<see cref="VrcoscParameterSettings"/>）。
/// <list type="bullet">
///   <item><c>Connected</c>・<c>Enabled</c>: 心拍が届いているか（<c>Enabled</c> は古い形の名前で、同じ値）</item>
///   <item><c>Value</c>: 心拍（int）。<c>Normalised</c>: 心拍を 0〜<see cref="NormalisedTop"/> から 0〜1 に直した float（外へ出た分は端に収める）</item>
///   <item><c>Average</c>: 直近 10 秒の平均（int）</item>
///   <item><c>Beat</c>: 拍ごとに true と false を入れ替える（<see cref="HeartRateInfo.Beats"/> が奇数なら true）</item>
///   <item><c>Units</c>・<c>Tens</c>・<c>Hundreds</c>: 各桁を 10 で割った float（72 なら 0.2・0.7・0.0）</item>
/// </list>
/// 止まったら全部 0 と false（<c>Beat</c> も false にそろえる）。
/// </summary>
internal sealed record VrcoscSender(
    string Connected,
    string Value,
    string Normalised,
    string Average,
    string Beat,
    string Enabled,
    string Units,
    string Tens,
    string Hundreds) : IHrsOscSender
{
    /// <summary><c>Normalised</c> で 1 になる心拍（VRCOSC の名前と値の決まり）。</summary>
    public const float NormalisedTop = 240f;

    public static VrcoscSender From(VrcoscParameterSettings names) => new(
        ParameterNames.Prefix + names.Connected,
        ParameterNames.Prefix + names.Value,
        ParameterNames.Prefix + names.Normalised,
        ParameterNames.Prefix + names.Average,
        ParameterNames.Prefix + names.Beat,
        ParameterNames.Prefix + names.Enabled,
        ParameterNames.Prefix + names.Units,
        ParameterNames.Prefix + names.Tens,
        ParameterNames.Prefix + names.Hundreds);

    /// <summary>計算するだけのプロパティなので、record の等しさの比べには入らない。</summary>
    public IReadOnlyCollection<string> Addresses => [Connected, Value, Normalised, Average, Beat, Enabled, Units, Tens, Hundreds];

    public void Send(IOscBus osc, HeartRateInfo info)
    {
        // 止まったときは HeartRateInfo.Stopped（数は 0・拍の数も 0）が来るので、ここで分けなくても全部 0 と false になる。
        // Normalised は寄せている途中の小数のまま割る（メーターがなめらかに動く）。整数で送るものは 1 つに丸めた値から取る。
        var live = info.Live;
        var heartRate = info.WholeBpm;

        osc.Send(Connected, live);
        osc.Send(Value, heartRate);
        osc.Send(Normalised, Math.Clamp(info.Bpm / NormalisedTop, 0f, 1f));
        osc.Send(Average, info.WholeAverage);
        osc.Send(Beat, info.Beats % 2 == 1);
        osc.Send(Enabled, live);
        osc.Send(Units, heartRate % 10 / 10f);
        osc.Send(Tens, heartRate / 10 % 10 / 10f);
        osc.Send(Hundreds, heartRate / 100 % 10 / 10f);
    }
}
