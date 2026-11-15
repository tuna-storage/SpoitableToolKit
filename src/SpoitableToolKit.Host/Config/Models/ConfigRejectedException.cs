namespace SpoitableToolKit.Host.Config.Models;

/// <summary>
/// 理由はそのまま設定画面に出るので、ホスト側で言い換えない。
/// どの項目のことかを持って運ぶ（画面は項目を赤枠にし、その下に理由を出す）。
/// </summary>
internal sealed class ConfigRejectedException(IReadOnlyList<ConfigNote> notes, IReadOnlyList<string> applied)
    : Exception(string.Join(" / ", notes.Select(n => n.Message)))
{
    public IReadOnlyList<ConfigNote> Notes { get; } = notes;

    /// <summary>断られなかったので入った（保存した）欄の key。値ごとに断られたときだけ並ぶ（全部を断った・読めなかったときは空）。</summary>
    public IReadOnlyList<string> Applied { get; } = applied;
}
