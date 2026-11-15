namespace SpoitableToolKit.Host.Ui.ContextMenu;

/// <summary>Act が入っていれば、そのぶんだけページ側の仕事が残っている。</summary>
public sealed record ContextMenuResult
{
    private const string ReplaceSelection = "replaceSelection";

    public bool Ok { get; init; }

    public string? Act { get; init; }

    public string Text { get; init; } = string.Empty;

    public static ContextMenuResult Done => new() { Ok = true };

    public static ContextMenuResult Failed => new() { Ok = false };

    public static ContextMenuResult Replace(string text) => new() { Ok = true, Act = ReplaceSelection, Text = text };
}
