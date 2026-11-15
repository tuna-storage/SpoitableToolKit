namespace SpoitableToolKit.Host.Updates.Models;

/// <summary>画面は字として入れる（HTML としては読まない）。</summary>
/// <param name="Kind"><c>heading</c>（見出し。<paramref name="Level"/> が 1〜6）・<c>item</c>（箇条書きの 1 行）・<c>text</c>（地の文）。</param>
internal sealed record ReleaseNoteBlock(string Kind, string Text, int Level);
