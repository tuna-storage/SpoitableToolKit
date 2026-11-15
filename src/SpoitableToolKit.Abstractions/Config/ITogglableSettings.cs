using SpoitableToolKit.Abstractions.Config.Attributes;

namespace SpoitableToolKit.Abstractions.Config;

/// <summary>
/// <b>入/切を持つ設定。</b> 中身は<b>ふつうに並べるだけ</b>でよく、
/// その中にまた入/切の設定を置ける。
/// <code>
/// sealed class ServerSettings : ITogglableSettings
/// {
///     public bool Enabled { get; set; }
///
///     public string Url = "";
///     public int RetryCount = 3;
///
///     // 入れ子の入れ子。ここも入/切を持つ
///     public YtDlpSettings YtDlp { get; } = new();
/// }
/// </code>
/// <para>
/// <b><see cref="Enabled"/> は切でも画面に出る。</b> ホストがそう扱うので
/// <see cref="SpoitableSettingsToggleAttribute"/> を書かなくてよい —
/// 書き忘れると<b>切にした瞬間にスイッチごと消えて二度と戻せなくなる</b>ので、
/// 忘れられる形にしていない。
/// </para>
/// <para>
/// 入/切ではない条件（モードで出し分ける、条件が 2 つ以上ある）なら
/// <see cref="IConditionalSettings"/> を実装して <see cref="IConditionalSettings.IsEnabled"/> を自分で書く。
/// </para>
/// </summary>
public interface ITogglableSettings : ISpoitableSettings
{
    /// <summary>
    /// 使うかどうか。<b>切でも画面に出る</b>（出ないと入に戻す手が無くなる）。
    /// これがそのまま「中身を出すか」になる。
    /// </summary>
    bool Enabled { get; set; }
}
