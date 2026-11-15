namespace SpoitableToolKit.Abstractions.Config;

/// <summary>
/// 入れ子の設定を、設定画面でどう置くか。<b>既定は <see cref="AlwaysShown"/></b> なので、
/// 何も書かなければ開いたまま並ぶ。
/// </summary>
public enum SpoitableCategory
{
    /// <summary>いつも開いている。畳む手段を出さない。</summary>
    AlwaysShown,

    /// <summary>
    /// 見出しを押すと畳める。<b>畳んでも値は何も変わらない</b> — 見えなくなるだけで、
    /// モジュールからはそのまま読める。
    /// <para>
    /// 数は多いが普段は触らない項目（パラメータ名の一覧など）を、一段落とすためのもの。
    /// <b>使うか使わないかで出し分けたいなら <see cref="ITogglableSettings"/></b>
    /// — あちらは値なので、切にすると機能そのものが止まる。
    /// </para>
    /// </summary>
    Foldable,
}
