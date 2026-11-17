namespace SpoitableToolKit.Host.Ui.Tray;

/// <summary>トレイのメニューの 1 行。Windows のメニューを知らない形で組み立てて（<see cref="TrayMenu"/>）、開くたびに <see cref="TrayPopupMenu"/> が Windows 標準のメニューへ写す。</summary>
internal sealed record TrayMenuItem
{
    /// <summary>押されたときに返す名前（<see cref="TrayMenu"/> の定数）。区切りとサブメニューの親は押しても何もしない。</summary>
    public required string Id { get; init; }

    public required string Label { get; init; }

    /// <summary>左クリックと同じ項目（太字で出す）。</summary>
    public bool Default { get; init; }

    public bool Checked { get; init; }

    public bool Enabled { get; init; } = true;

    public bool Separator { get; init; }

    public IReadOnlyList<TrayMenuItem> Children { get; init; } = [];

    public static TrayMenuItem Line { get; } = new() { Id = "", Label = "", Separator = true };
}
