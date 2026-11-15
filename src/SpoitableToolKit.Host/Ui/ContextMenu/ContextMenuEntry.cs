namespace SpoitableToolKit.Host.Ui.ContextMenu;

public sealed record ContextMenuEntry
{
    public string Id { get; init; } = string.Empty;

    public string LabelKey { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public bool Separator { get; init; }
}
