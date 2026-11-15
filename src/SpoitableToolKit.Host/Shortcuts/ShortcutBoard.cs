using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Abstractions.Shortcuts;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Host.Notifications;
using SpoitableToolKit.Host.Modules;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Shortcuts.Gestures;
using SpoitableToolKit.Host.Status;
using SpoitableToolKit.Host.Ui.Browser;
using SpoitableToolKit.Host.Ui.Push;
using SpoitableToolKit.Host.Ui.Shell;

namespace SpoitableToolKit.Host.Shortcuts;

/// <summary>
/// 本体と各モジュールが登録したものを、決まった順で 1 列に持つ。利用者が並べる（設定 <see cref="HostConfig.WristShortcuts"/>）のは、ここにある名前（<see cref="ShortcutBoardEntry.Key"/>）。
/// 名前の付け方: 本体は <c>log</c> / <c>settings</c> / <c>dashboard</c>、パネルを持つモジュールはモジュールの Id、
/// モジュールが登録した分（<c>host.Shortcuts.Register</c>）は <c>&lt;モジュール&gt;/&lt;Id&gt;</c>。
/// タイルの中身（<see cref="ModuleStatus"/>）は <see cref="StatusBoard"/> と同じで、
/// 書かれたスレッドでは印を付けるだけにして、<see cref="UiPump"/> の周期でまとめて配る（心拍は BLE のループから書かれる）。
/// </summary>
internal sealed class ShortcutBoard(UiBroadcast broadcast, NotificationManager notify, ILocalizer text, ILogger logger)
{
    public const string HostSource = "host";

    private readonly Lock _gate = new();

    private readonly List<ShortcutBoardEntry> _entries = [];

    /// <summary>走っている最中の処理（名前）。<b>走っている間に押されても 2 本目は走らせない</b>。</summary>
    private readonly HashSet<string> _running = [];

    private readonly CancellationTokenSource _shutdown = new();

    private ShortcutBoardContent[] _last = [];

    /// <summary>本体の分を入れる。最初に呼ぶ（設定のモーダルで本体が先頭に来る）。</summary>
    public void AddHost()
    {
        lock (_gate)
        {
            foreach (var (id, icon) in HostViews)
                _entries.Add(new ShortcutBoardEntry(id, HostSource, "host.nav." + id, null, icon, null, null, id, null, null));
        }
    }

    private static readonly (string Id, string Icon)[] HostViews = [("log", "i-log"), ("settings", "i-gear"), ("dashboard", "i-dash")];

    /// <summary>
    /// モジュールの分を入れる。読み込み順に呼ぶ。
    /// <paramref name="hasPanel"/> なら、登録しなくてもパネルを開くもの（名前 = モジュールの Id、中身 = <paramref name="status"/>）が先頭に入る。
    /// そのあとに、モジュールが <c>host.Shortcuts.Register</c> で登録した順（名前と字は登録のときに検めてある）。
    /// タイルのページ（<see cref="IWristShortcut.Page"/>）は、字が外れている・<paramref name="pageExists"/> が無いと言ったら、
    /// 警告を出してページ無しのタイルにする（ショートカットそのものは並べる）。
    /// </summary>
    /// <param name="pageExists">モジュールの <c>web/</c> にそのファイルがあるか。<c>null</c> なら、ページは全部受けない。</param>
    /// <returns>ページを 1 つでも受けたか（受けたなら、パネルが無くても <c>spoitable://&lt;Id&gt;/</c> を配る）。</returns>
    public bool AddModule(
        string moduleId, string displayName, string? icon, string? iconPath, bool hasPanel, ModuleStatus? status,
        IReadOnlyList<RegisteredShortcut> registered, Func<string, bool>? pageExists = null)
    {
        var safeIconPath = NotifyKind.IsSafePath(iconPath) ? iconPath : null;
        var added = new List<ShortcutBoardEntry>();

        if (hasPanel)
            added.Add(new ShortcutBoardEntry(moduleId, moduleId, moduleId + ".name", displayName, null, icon, safeIconPath, moduleId, null, status));

        foreach (var shortcut in registered)
        {
            // 線のアイコンは、自分のが通らなければモジュールのもの（線が無ければ字のアイコン）へ落とす。
            var ownPath = NotifyKind.IsSafePath(shortcut.IconPath) ? shortcut.IconPath : null;

            added.Add(new ShortcutBoardEntry(
                $"{moduleId}/{shortcut.Id}", moduleId, shortcut.Name, null, null,
                ownPath is null ? icon : null, ownPath ?? safeIconPath,
                null, shortcut.Handler, shortcut.Status, CheckPage(moduleId, shortcut, pageExists), shortcut.Marked));
        }

        lock (_gate) _entries.AddRange(added);

        return added.Any(e => e.Page is not null);
    }

    /// <summary>
    /// タイルのページを検める。通らなければ null（ページ無しのタイルになる）。
    /// 字を絞るのは、画面がこの字で URL を組むから（<c>spoitable://&lt;Id&gt;/&lt;Page&gt;</c>。<c>?</c> や <c>#</c>、<c>..</c> を入れさせない）。
    /// 登録のときにも検めているが、URL を組む前にもう一度見る。
    /// </summary>
    private string? CheckPage(string moduleId, RegisteredShortcut shortcut, Func<string, bool>? pageExists)
    {
        if (shortcut.Page is not { } page) return null;

        if (!ModulePanelEntries.IsPagePath(page))
        {
            logger.LogWarning("{Module} gave the shortcut {Id} the page {Page}; use a path under web/ ending in .html, with letters, digits, '-', '_' and '/', so the tile is drawn without it", moduleId, shortcut.Id, page);
            return null;
        }

        if (pageExists?.Invoke(page) != true)
        {
            logger.LogWarning("{Module} gave the shortcut {Id} the page {Page}, but there is no such file in its web/; the tile is drawn without it", moduleId, shortcut.Id, page);
            return null;
        }

        return page;
    }

    public IReadOnlyList<string> Keys()
    {
        lock (_gate) return [.. _entries.Select(e => e.Key)];
    }

    public IReadOnlyList<ShortcutBoardEntry> Entries()
    {
        lock (_gate) return [.. _entries];
    }

    public ShortcutBoardEntry? Find(string? key)
    {
        if (key is null) return null;

        lock (_gate) return _entries.FirstOrDefault(e => e.Key == key);
    }

    /// <summary>
    /// 押された。開くものは <paramref name="openWindow"/> へ画面を渡し（その画面なら閉じる、の判断は窓がする）、
    /// モジュールが登録した押すだけのものは裏で <see cref="IClickableWristShortcut.OnTriggerAsync"/> を呼ぶ。
    /// 台帳に無い名前・ドラッグだけのものは何もしない（画面から来た名前をそのまま信じない）。
    /// </summary>
    public Task PressAsync(string key, Action<string> openWindow)
    {
        if (Find(key) is not { } entry)
        {
            logger.LogWarning("A shortcut that is not there was pressed: {Key}", key);
            return Task.CompletedTask;
        }

        if (entry.View is { } view)
        {
            openWindow(view);
            return Task.CompletedTask;
        }

        if (entry.Handler is not IClickableWristShortcut clickable)
        {
            logger.LogWarning("The shortcut {Key} is dragged, not pressed; ignoring the press", key);
            return Task.CompletedTask;
        }

        return TriggerAsync(entry, clickable);
    }

    /// <summary>モジュールの <see cref="IClickableWristShortcut.OnTriggerAsync"/> を呼ぶ。返すまでの間に押し直されても 2 回目は呼ばない。例外はログとエラーのトースト、畳むときは token を切る（切れて止まったものは黙る）。</summary>
    private async Task TriggerAsync(ShortcutBoardEntry entry, IClickableWristShortcut clickable)
    {
        lock (_gate)
        {
            if (!_running.Add(entry.Key))
            {
                logger.LogInformation("The shortcut {Key} is still running; not starting it again", entry.Key);
                return;
            }
        }

        try
        {
            logger.LogInformation("Running the shortcut {Key}", entry.Key);

            // CEF のスレッドで待たない。モジュールの処理が同期で重くても、押した画面は止めない。
            await Task.Run(() => clickable.OnTriggerAsync(_shutdown.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Failed(entry, ex);
        }
        finally
        {
            lock (_gate) _running.Remove(entry.Key);
        }
    }

    /// <summary>モジュールが登録した、ドラッグだけのショートカット。台帳に無い・本体やパネルの分・押すだけのものなら null。</summary>
    public IDraggableWristShortcut? FindDraggable(string key) => Find(key)?.Handler as IDraggableWristShortcut;

    /// <summary>ドラッグの呼び出しが返り切っていないか。その間の押しは捨てる（<see cref="ShortcutGestures"/>）。</summary>
    public bool IsRunning(string key)
    {
        lock (_gate) return _running.Contains(key);
    }

    /// <summary>
    /// ドラッグを始める（<see cref="IDraggableWristShortcut.OnStartDragAsync"/> をすぐ呼ぶ）。ドラッグだけのものでなければ・走っている最中なら、始めずに null
    /// （2 本目のドラッグは走らせない）。終えるまで「走っている」に入れておく。例外は押したときと同じくログとエラーのトースト（1 回のドラッグで 1 回）。
    /// </summary>
    public ShortcutDragRun? BeginDrag(string key, ShortcutDragEventParams start)
    {
        if (Find(key) is not { Handler: IDraggableWristShortcut draggable } entry) return null;

        lock (_gate)
        {
            if (!_running.Add(entry.Key))
            {
                logger.LogInformation("The shortcut {Key} is still running; not starting a drag", entry.Key);
                return null;
            }
        }

        logger.LogInformation("Dragging the shortcut {Key}", entry.Key);

        return new ShortcutDragRun(draggable, start, _shutdown.Token, ex => Failed(entry, ex), () =>
        {
            lock (_gate) _running.Remove(entry.Key);
        });
    }

    /// <summary>モジュールが投げた。押した人は手首にいてログは見えないので、エラーのトーストにする。</summary>
    private void Failed(ShortcutBoardEntry entry, Exception ex)
    {
        logger.LogWarning(ex, "The shortcut {Key} failed", entry.Key);
        notify.PushToast(NotifyKind.Error, text["host.shortcut.failed"], ex.Message);
    }

    public void Cancel() => _shutdown.Cancel();

    /// <summary><see cref="UiPump"/> の周期から呼ばれる。毎回組み立てて、前に配ったものと違うときだけ配る（<see cref="StatusBoard.Flush"/> と同じ形）。</summary>
    public void Flush()
    {
        var current = Current();
        if (current.SequenceEqual(_last)) return;

        _last = current;
        broadcast.Push("shortcutStatus", current);
    }

    public ShortcutBoardContent[] Current()
    {
        lock (_gate)
            return
            [
                .. _entries
                    .Where(e => e.Status is not null)
                    .Select(e => new ShortcutBoardContent(e.Key, e.Status!.Text, StatusBoard.Wire(e.Status.State))),
            ];
    }

    /// <summary>CefSharp の bind 越しに返す形（record のままでは渡せない。<see cref="StatusBoard.ForBridge"/> と同じ理由）。</summary>
    public object[] ForBridge() =>
    [
        .. Entries().Select(e => new
        {
            key = e.Key,
            source = e.Source,
            name = e.Name,
            fallback = e.Fallback,
            hostIcon = e.HostIcon,
            icon = e.Icon,
            iconPath = e.IconPath,
            view = e.View,
            runs = e.Handler is not null,

            // ドラッグだけのもの。手首は押した・動いた・離したをそのまま C# へ送る（ShortcutGestures）。ほかは click で押す。
            draggable = e.Handler is IDraggableWristShortcut,

            // 開かないが、窓がこの画面を出している間は枠を付ける（モジュールが名乗った。開くのはモジュールの処理）。
            marked = e.Marked,

            // タイルに描くページ。**出どころの origin で開く**（手首はこの origin で持ち主を決める）。
            page = e.Page is { } page ? $"{SpoitableSchemeHandlerFactory.SchemeName}://{e.Source}/{page}" : null,
            text = e.Status?.Text ?? "",
            state = e.Status is { } status ? StatusBoard.Wire(status.State) : "off",
        }),
    ];
}
