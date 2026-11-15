namespace SpoitableToolKit.Abstractions.Ui;

/// <summary>
/// メニューに出す 1 項目。
/// <para>
/// <b>文言はキーで渡す。</b> 訳した文字ではなくキーを返せば、
/// <b>言語を切り替えたときに次のメニューから正しく出る</b>（モジュールが言語の変化を
/// 追いかける必要が無い）。キーはモジュールの <c>locales/*.json</c> に置くもので、
/// <b>持ち主で始まる</b>のは設定の文言と同じ約束。
/// </para>
/// </summary>
public sealed record ContextMenuItem
{
    /// <summary>
    /// 押されたときに返ってくる印。モジュールの中で一意ならよい
    /// （<b>ホストが持ち主を頭に付けて配る</b>ので、他のモジュールやホストの項目とはぶつからない）。
    /// </summary>
    public required string Id { get; init; }

    /// <summary>文言のキー。</summary>
    public required string LabelKey { get; init; }

    /// <summary>
    /// 押せるか。<b>出さないのではなく灰色で出す</b>のは、「ここにその操作がある」ことは
    /// 見せたいが今は出来ない、というときだけにする。
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>区切り線。これが <c>true</c> のものは <see cref="Id"/> も文言も見ない。</summary>
    public bool Separator { get; init; }

    public static ContextMenuItem Divider => new() { Id = string.Empty, LabelKey = string.Empty, Separator = true };
}
