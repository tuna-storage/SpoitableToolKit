using SpoitableToolKit.Abstractions.Shortcuts;
using SpoitableToolKit.Abstractions.Ui;

namespace SpoitableToolKit.Host.Shortcuts;

internal sealed record RegisteredShortcut(
    string Id, string Name, string? IconPath, string? Page, ModuleStatus? Status, IWristShortcut Handler, string? Marked = null);
