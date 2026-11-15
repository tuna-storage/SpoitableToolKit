using System.Text.RegularExpressions;
using SpoitableToolKit.Abstractions.Shortcuts;
using SpoitableToolKit.Host.Modules;

namespace SpoitableToolKit.Host.Shortcuts;

/// <summary>
/// 登録を覚えるだけで、並べるのは読み込みの後の <c>Program</c>（<see cref="ShortcutBoard.AddModule"/> へ <see cref="Registered"/> を渡す）。
/// 登録できるのは OnInit の間（<see cref="RegistrationWindow"/>。<c>host.Ui</c> と同じ窓）だけ。
/// 値は登録したときに 1 回だけ読む（あとで変わっても並びは変えない）。
/// </summary>
internal sealed partial class ModuleShortcuts(string moduleId, RegistrationWindow window) : IModuleShortcuts
{
    private readonly List<RegisteredShortcut> _registered = [];

    public IReadOnlyList<RegisteredShortcut> Registered
    {
        get
        {
            lock (window.Gate) return [.. _registered];
        }
    }

    public void Register(IClickableWristShortcut shortcut) => Add(shortcut);

    public void Register(IDraggableWristShortcut shortcut) => Add(shortcut);

    private void Add(IWristShortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);

        // 両方の種類を実装したクラスは、型を絞れば片方の口に渡せてしまう。押しとドラッグのどちらで受けるかが決まらないので断る。
        if (shortcut is IClickableWristShortcut && shortcut is IDraggableWristShortcut)
            throw new ArgumentException(
                $"{moduleId} called host.Shortcuts.Register with {shortcut.GetType().Name}, which implements both IClickableWristShortcut and IDraggableWristShortcut. " +
                "A wrist tile is either pressed or dragged: keep IClickableWristShortcut (OnTriggerAsync) for a tile that does something when pressed, " +
                "or IDraggableWristShortcut (OnStartDragAsync, OnDraggingAsync, OnEndDragAsync) for a slider or a knob, and split the other into its own shortcut.",
                nameof(shortcut));

        // 値は 1 回だけ読む（モジュールのプロパティが毎回違うものを返しても、並びと押した先は変わらない）。
        var id = shortcut.Id;
        var name = shortcut.Name;
        var iconPath = shortcut.IconPath;
        var page = shortcut.Page;
        var status = shortcut.Status;
        var marked = shortcut.MarkedPanel;

        if (id is null || !ShortcutId().IsMatch(id))
            throw new ArgumentException(
                $"{moduleId} called host.Shortcuts.Register with the Id \"{id}\" ({shortcut.GetType().Name}). " +
                "Use 1-64 lower-case letters, digits and '-', starting with a letter or digit (for example \"resolve-latest\"): " +
                $"it is saved in the settings as {moduleId}/<Id>, so do not change it once people have placed the tile.", nameof(shortcut));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                $"{moduleId} called host.Shortcuts.Register for \"{id}\" with an empty Name. Give the name people see on the tile, " +
                $"best a key in the module's locales (for example \"{moduleId}.shortcut.{id}\").", nameof(shortcut));

        if (page is not null && !ModulePanelEntries.IsPagePath(page))
            throw new ArgumentException(
                $"{moduleId} called host.Shortcuts.Register for \"{id}\" with the page \"{page}\". Give a page under the module's web/ folder, " +
                "ending in .html, with letters, digits, '-', '_' and '/' only (for example \"tile.html\"), or null to draw the tile with its icon and name.",
                nameof(shortcut));

        if (marked?.Place is { } place && !ModuleUi.IsPlace(place))
            throw new ArgumentException(
                $"{moduleId} called host.Shortcuts.Register for \"{id}\" with MarkedPanel = new PanelView {{ Place = \"{place}\" }}. " +
                "Give the same place the shortcut passes to host.Ui.ToggleVrPanel: 1-64 letters, digits, '-' and '_' only (for example \"stats_7d\"), " +
                "or new PanelView() for the top of the panel.", nameof(shortcut));

        lock (window.Gate)
        {
            window.Admit("host.Shortcuts.Register", "the wrist shortcuts");

            if (_registered.Any(r => r.Id == id))
                throw new InvalidOperationException(
                    $"{moduleId} called host.Shortcuts.Register twice with the Id \"{id}\". Each shortcut of a module needs its own Id.");

            _registered.Add(new RegisteredShortcut(
                id, name, iconPath, page, status, shortcut, marked is null ? null : ModuleUi.ViewOf(moduleId, marked.Place)));
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}\\z")]
    private static partial Regex ShortcutId();
}
