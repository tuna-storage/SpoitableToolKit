namespace SpoitableToolKit.Abstractions.Localization;

/// <summary>
/// 画面に出す文言を引く。<b>ログや例外は対象外</b> — あれは開発者が読むもので、
/// 検索しやすさの方が大事なので日本語のまま置く。
/// <code>
/// host.Text["hrs.panel.state.receiving"]
/// host.Text.Format("hrs.panel.firewall.missing", port)
/// </code>
/// <para>
/// キーは <c>&lt;持ち主&gt;.&lt;場所&gt;.&lt;名前&gt;</c>。持ち主はホスト（<c>host</c>）か
/// モジュールの Id なので、<b>モジュールが増えても衝突しない</b>。
/// </para>
/// <para>
/// 辞書はモジュールが<b>自分で同梱する</b>（<c>modules/&lt;id&gt;/locales/&lt;言語&gt;.json</c>、平らな key-value）。
/// ホストは読み込んで 1 つに合成するので、第三者のモジュールも同じやり方で足せる。
/// <b>入るのは、自分の Id で始まるキーだけ</b>（<c>hrs</c> なら <c>hrs.…</c>）。ほかのキーは入れずに、ログへ警告を出す。
/// Id に <c>.</c> を入れると（<c>a.b</c>）、<c>a</c> のモジュールとキーが重なりうる（先に読まれた方が残る）ので、入れない方がよい。
/// </para>
/// <para>
/// 「閉じる」「コピー」「戻る」のような、どのモジュールでも使う短い文言は本体が持っている（<see cref="CommonPhrases"/>）:
/// <c>host.Text[CommonPhrases.Ui.Close]</c>。
/// </para>
/// </summary>
public interface ILocalizer
{
    /// <summary>
    /// いま出している言語（<c>ja</c> / <c>en</c>）。画面側へ渡すときに使う。
    /// </summary>
    string Language { get; }

    /// <summary>
    /// 引く。<b>無ければキーをそのまま返す</b> —
    /// 既定の言語へ落とすと、画面は読めるが翻訳の抜けに誰も気付かない。
    /// </summary>
    string this[string key] { get; }

    /// <summary>
    /// 引いて <c>{0}</c> に差し込む。差し込みに失敗しても<b>落とさず</b>元の文を返す
    /// （翻訳側の書き間違いで画面が消えるのは割に合わない）。
    /// </summary>
    string Format(string key, params object?[] arguments);
}
