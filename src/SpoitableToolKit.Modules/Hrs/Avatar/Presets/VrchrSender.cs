using SpoitableToolKit.Abstractions.Osc;
using SpoitableToolKit.Modules.Hrs.Config;

namespace SpoitableToolKit.Modules.Hrs.Avatar.Presets;

/// <summary>
/// VRCHR のプリセット。9 個のアドレスは設定に書かれた名前へ <see cref="ParameterNames.Prefix"/> を足しただけ。
/// session が送るたびに設定から作り直すので、名前を変えるとその場で効く。
/// </summary>
internal sealed record VrchrSender(
    string Ones,
    string Tens,
    string Hundreds,
    string IsConnected,
    string IsActive,
    string IsBeat,
    string Percent,
    string FullPercent,
    string HeartRate) : IHrsOscSender
{
    public static VrchrSender From(ParameterSettings names) => new(
        ParameterNames.Prefix + names.Ones,
        ParameterNames.Prefix + names.Tens,
        ParameterNames.Prefix + names.Hundreds,
        ParameterNames.Prefix + names.IsConnected,
        ParameterNames.Prefix + names.IsActive,
        ParameterNames.Prefix + names.IsBeat,
        ParameterNames.Prefix + names.Percent,
        ParameterNames.Prefix + names.FullPercent,
        ParameterNames.Prefix + names.HeartRate);

    /// <summary>計算するだけのプロパティなので、record の等しさの比べには入らない。</summary>
    public IReadOnlyCollection<string> Addresses => [Ones, Tens, Hundreds, IsConnected, IsActive, IsBeat, Percent, FullPercent, HeartRate];

    /// <summary>
    /// 9 個をまとめて送る。<c>HRPercent</c> は 255 で割った値（アバター側が 0〜1 の float を期待するため）。
    /// 0 なら bool は全部 false（繋がっている・動いている・拍）。0 は計れていない・切れた・黙った・止めた、のどれでも同じで、
    /// 心拍計は繋がっていても心拍が来ていなければ、アバターには「繋がっていない」と見せる。
    /// </summary>
    public void Send(IOscBus osc, HeartRateInfo info)
    {
        // 割合は寄せている途中の小数のまま割る（メーターがなめらかに動く）。整数で送るものは 1 つに丸めた値から取る。
        var heartRate = info.WholeBpm;
        var percent = Math.Clamp(info.Bpm / 255f, 0f, 1f);
        var live = info.Live;

        osc.Send(Ones, heartRate % 10);
        osc.Send(Tens, heartRate / 10 % 10);
        osc.Send(Hundreds, heartRate / 100 % 10);
        osc.Send(IsConnected, live);
        osc.Send(IsActive, live);
        osc.Send(IsBeat, info.Beat);
        osc.Send(Percent, percent);
        osc.Send(FullPercent, percent);
        osc.Send(HeartRate, heartRate);
    }
}
