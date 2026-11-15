namespace SpoitableToolKit.Abstractions.Clipboard;

/// <summary>
/// クリップボード。
/// <para>
/// ホスト側に置くのは、書き込みに STA スレッドが要るのと、
/// 通知から直接コピーする経路（UI を開いていない状態）でも同じ口が必要なため。
/// </para>
/// </summary>
public interface IClipboard
{
    /// <summary>書けたら <c>true</c>。他のプロセスが掴んでいると失敗しうる。</summary>
    bool SetText(string text);

    /// <summary>
    /// 中身。文字が入っていなければ（画像だけ・空・他のプロセスが掴んでいる）<c>null</c>。
    /// <para>
    /// <b>読むのも STA スレッドが要る</b>のは書くときと同じ。
    /// </para>
    /// </summary>
    string? GetText();
}
