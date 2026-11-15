namespace SpoitableToolKit.Host.Shortcuts;

/// <summary>画面へ配るタイルの中身。<c>State</c> は <c>off</c> / <c>ok</c> / <c>warn</c> / <c>bad</c>。</summary>
internal sealed record ShortcutBoardContent(string Key, string Text, string State);
