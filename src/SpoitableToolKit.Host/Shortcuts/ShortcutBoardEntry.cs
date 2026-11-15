using SpoitableToolKit.Abstractions.Shortcuts;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Host.Shortcuts.Gestures;

namespace SpoitableToolKit.Host.Shortcuts;

internal sealed record ShortcutBoardEntry(
    string Key, string Source, string Name, string? Fallback, string? HostIcon, string? Icon, string? IconPath,
    string? View, IWristShortcut? Handler, ModuleStatus? Status, string? Page = null, string? Marked = null);
