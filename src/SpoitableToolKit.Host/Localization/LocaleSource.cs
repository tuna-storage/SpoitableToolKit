using SpoitableToolKit.Abstractions.Localization;

namespace SpoitableToolKit.Host.Localization;

/// <summary>
/// 辞書の置き場所（<c>&lt;Directory&gt;/locales/&lt;言語&gt;.json</c>）と、その持ち主。
/// <para>
/// <b>モジュールの辞書に書けるのは、自分の Id で始まるキーだけ</b>（<c>hrs</c> なら <c>hrs.…</c>）。
/// 本体（<see cref="Owner"/> が null）の辞書には <c>host.…</c> と、どのモジュールも使う <c>common.…</c>
/// （<see cref="CommonPhrases"/>）が入っている。
/// </para>
/// <para>
/// ⚠ Id に <c>.</c> が入るモジュール（<c>a.b</c>）のキーは、<c>a</c> のモジュールのキーと重なりうる（どちらも <c>a.b.x</c> を持てる）。
/// 重なったら先に読んだ方が残り、警告が出る。
/// </para>
/// </summary>
internal readonly record struct LocaleSource(string Directory, string? Owner)
{
    /// <summary>本体の辞書。</summary>
    public static LocaleSource Host { get; } = new(AppContext.BaseDirectory, null);

    /// <summary>
    /// このキーを、この持ち主の辞書から入れてよいか。<b>Id の大文字小文字は問わない</b> — 読み込みは、フォルダ名と大文字小文字だけ違う Id
    /// （<c>hrs</c> のフォルダで <c>Hrs</c> と名乗る）を警告だけで通すので、ここで区別すると、その辞書が 1 件も入らなくなる。
    /// </summary>
    public bool Owns(string key) =>
        Owner is null || (key.Length > Owner.Length + 1 && key[Owner.Length] == '.' && key.StartsWith(Owner, StringComparison.OrdinalIgnoreCase));
}
