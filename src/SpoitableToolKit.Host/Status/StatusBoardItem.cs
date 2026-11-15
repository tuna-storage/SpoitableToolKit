namespace SpoitableToolKit.Host.Status;

/// <summary>UI へ渡す 1 件。<c>State</c> は <c>off</c> / <c>ok</c> / <c>warn</c> / <c>bad</c>。</summary>
internal sealed record StatusBoardItem(string Id, string Text, string State, string? Note);
