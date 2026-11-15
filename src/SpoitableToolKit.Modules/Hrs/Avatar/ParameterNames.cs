using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Modules.Hrs.Config;

namespace SpoitableToolKit.Modules.Hrs.Avatar;

/// <summary>
/// 設定に書かれたパラメータ名の<b>検証と修復</b>。送る側（<see cref="Presets.IHrsOscSender"/>）は、ここを通った名前に
/// <see cref="Prefix"/> を足して送る。
/// </summary>
internal static class ParameterNames
{
    /// <summary>
    /// VRChat のアバターパラメータはこの下にしか無いので、<b>変えられるのは後ろだけ</b>。
    /// 設定には名前だけを保存して、送るときに足す。
    /// </summary>
    public const string Prefix = "/avatar/parameters/";

    /// <summary>
    /// 名前の項目の全部。検証も修復も同じ表を回す。ラベルは設定画面のものと揃える（却下の理由がどの欄の話か分かるように）。
    /// どの設定かは式で持つ（設定の変更を値ごとに断るとき、その式をそのまま渡す）。
    /// </summary>
    private static readonly (string LabelKey, Expression<Func<HrsConfig, string>> Setting, Action<HrsConfig, string> Set)[] Names =
    [
        ("hrs.settings.parameters.ones.label", c => c.Parameters.Ones, (c, v) => c.Parameters.Ones = v),
        ("hrs.settings.parameters.tens.label", c => c.Parameters.Tens, (c, v) => c.Parameters.Tens = v),
        ("hrs.settings.parameters.hundreds.label", c => c.Parameters.Hundreds, (c, v) => c.Parameters.Hundreds = v),
        ("hrs.settings.parameters.isConnected.label", c => c.Parameters.IsConnected, (c, v) => c.Parameters.IsConnected = v),
        ("hrs.settings.parameters.isActive.label", c => c.Parameters.IsActive, (c, v) => c.Parameters.IsActive = v),
        ("hrs.settings.parameters.isBeat.label", c => c.Parameters.IsBeat, (c, v) => c.Parameters.IsBeat = v),
        ("hrs.settings.parameters.percent.label", c => c.Parameters.Percent, (c, v) => c.Parameters.Percent = v),
        ("hrs.settings.parameters.fullPercent.label", c => c.Parameters.FullPercent, (c, v) => c.Parameters.FullPercent = v),
        ("hrs.settings.parameters.heartRate.label", c => c.Parameters.HeartRate, (c, v) => c.Parameters.HeartRate = v),
        ("hrs.settings.vrcosc.connected.label", c => c.VrcoscParameters.Connected, (c, v) => c.VrcoscParameters.Connected = v),
        ("hrs.settings.vrcosc.value.label", c => c.VrcoscParameters.Value, (c, v) => c.VrcoscParameters.Value = v),
        ("hrs.settings.vrcosc.normalised.label", c => c.VrcoscParameters.Normalised, (c, v) => c.VrcoscParameters.Normalised = v),
        ("hrs.settings.vrcosc.average.label", c => c.VrcoscParameters.Average, (c, v) => c.VrcoscParameters.Average = v),
        ("hrs.settings.vrcosc.beat.label", c => c.VrcoscParameters.Beat, (c, v) => c.VrcoscParameters.Beat = v),
        ("hrs.settings.vrcosc.enabled.label", c => c.VrcoscParameters.Enabled, (c, v) => c.VrcoscParameters.Enabled = v),
        ("hrs.settings.vrcosc.units.label", c => c.VrcoscParameters.Units, (c, v) => c.VrcoscParameters.Units = v),
        ("hrs.settings.vrcosc.tens.label", c => c.VrcoscParameters.Tens, (c, v) => c.VrcoscParameters.Tens = v),
        ("hrs.settings.vrcosc.hundreds.label", c => c.VrcoscParameters.Hundreds, (c, v) => c.VrcoscParameters.Hundreds = v),
    ];

    private static readonly Func<HrsConfig, string>[] Getters = [.. Names.Select(n => n.Setting.Compile())];

    /// <summary>
    /// 名前として読めない理由の<b>翻訳キー</b>。読めれば <c>null</c>。
    /// <b>文にはしない</b> — 画面に出す文は、文言を持っている側が組む。
    /// <para>
    /// 見るのは<b>自分の送信が壊れる条件だけ</b>。空だと宛先が <c>/avatar/parameters/</c> で終わってしまう。
    /// <b>名前の途中の <c>/</c> は通す</b> — VRChat では <c>VRCOSC/Heartrate/Value</c> で 1 つのパラメータ名で、
    /// 宛先は <c>/avatar/parameters/VRCOSC/Heartrate/Value</c> になる（VRCOSC のプリセットの既定がこの形）。
    /// 頭か終わりの <c>/</c> と <c>//</c> は空の段ができて別のアドレスになるので拒む。
    /// OSC がパターンマッチ用に予約している <c>#*,?[]{}</c> は見ない —
    /// VRChat 側はパターンマッチをしないので弾く実害を示せない。
    /// </para>
    /// </summary>
    public static string? ErrorOf(string name) =>
        string.IsNullOrWhiteSpace(name) ? "hrs.error.paramName.empty"
        : name.StartsWith('/') || name.EndsWith('/') || name.Contains("//", StringComparison.Ordinal) ? "hrs.error.paramName.slash"
        : null;

    /// <summary>
    /// 設定の中の「読めない名前」の全部（どの設定か・項目名の翻訳キー・理由の翻訳キー）。全部読めれば空。
    /// 設定画面から来た変更を値ごとに断るのに使う。
    /// </summary>
    public static IEnumerable<(Expression<Func<HrsConfig, string>> Setting, string LabelKey, string ReasonKey)> ErrorsIn(HrsConfig config)
    {
        for (var i = 0; i < Names.Length; i++)
            if (ErrorOf(Getters[i](config)) is { } reasonKey)
                yield return (Names[i].Setting, Names[i].LabelKey, reasonKey);
    }

    /// <summary>
    /// 読めない名前を既定へ戻す。設定ファイルを直接書き換えられた場合のためにある
    /// （設定画面から入る値は <see cref="ErrorsIn"/> で弾かれる）。
    /// </summary>
    public static void Repair(HrsConfig config, ILogger logger)
    {
        var defaults = new HrsConfig();

        for (var i = 0; i < Names.Length; i++)
        {
            var (labelKey, _, set) = Names[i];
            var get = Getters[i];
            if (ErrorOf(get(config)) is not { } reasonKey) continue;

            var fallback = get(defaults);

            // ログは開発者のものなので翻訳しない。キーのまま出す方が、どの項目の話かを探しやすい。
            logger.LogWarning("Parameter name {Label} is not usable ({Reason}); falling back to the default {Default}",
                labelKey, reasonKey, fallback);
            set(config, fallback);
        }
    }
}
