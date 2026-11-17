using SpoitableToolKit.Abstractions.Localization;

namespace SpoitableToolKit.Host.Ui.Tray;

/// <summary>
/// トレイの右クリックのメニューの中身。<b>開くたびに組み立て直す</b>ので、言語を変えればその場で次に開いたメニューから変わる。
/// 押されたときの動きは持たない（名前を返すだけ。動かすのは <see cref="Desktop.DesktopApp"/>）。
/// </summary>
internal static class TrayMenu
{
    public const string Open = "open";

    public const string Exit = "exit";

    public static IReadOnlyList<TrayMenuItem> Build(ILocalizer text) =>
    [
        new() { Id = Open, Label = text["host.tray.open"], Default = true },
        TrayMenuItem.Line,
        new() { Id = Exit, Label = text["host.tray.exit"] },
    ];
}
