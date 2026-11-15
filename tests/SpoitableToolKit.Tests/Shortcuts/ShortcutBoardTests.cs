using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Shortcuts;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Host.Notifications;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Shortcuts;
using SpoitableToolKit.Tests.Fakes;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Shortcuts;

public sealed class ShortcutBoardTests
{
    // 壊れたときに試験が固まらないよう、終わるはずの待ちには上限を付ける。
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static (ShortcutBoard Board, Recording Sent, Logs Logs) New()
    {
        var sent = new Recording();
        var logs = new Logs();
        var notify = new NotificationManager(sent, new HostConfig(), logs);

        return (new ShortcutBoard(sent, notify, new FakeLocalizer(), logs), sent, logs);
    }

    private static RegisteredShortcut Registered(
        string id, Func<CancellationToken, Task>? trigger = null, ModuleStatus? status = null, string? page = null, string? iconPath = null, string? name = null,
        string? marked = null) =>
        new(id, name ?? id, iconPath, page, status, new Shortcut(trigger ?? (_ => Task.CompletedTask)), marked);

    private sealed class Shortcut(Func<CancellationToken, Task> trigger) : IClickableWristShortcut
    {
        public string Id => "unused";

        public string Name => "unused";

        public Task OnTriggerAsync(CancellationToken cancellationToken) => trigger(cancellationToken);
    }

    // パネルの分の名前はモジュールの Id のまま（前から設定に入っている hrs / videourl がそのまま通る）。
    [Fact]
    public void TheHostComesFirstAndAPanelKeepsTheModuleIdSoSavedSettingsStillWork()
    {
        var (board, _, _) = New();

        board.AddHost();
        board.AddModule("hrs", "Heart Rate", "♥", null, hasPanel: true, status: null, []);
        board.AddModule("videourl", "Video URL", null, null, hasPanel: true, status: null, [Registered("resolve-latest")]);

        Assert.Equal(["log", "settings", "dashboard", "hrs", "videourl", "videourl/resolve-latest"], board.Keys());
        Assert.Equal("hrs", board.Find("hrs")!.View);
        Assert.Equal("hrs", board.Find("hrs")!.Source);
        Assert.Equal(ShortcutBoard.HostSource, board.Find("settings")!.Source);
    }

    [Fact]
    public void AModuleWithoutAPanelOffersOnlyWhatItRegistered()
    {
        var (board, _, _) = New();

        board.AddModule("osc", "OSC", null, null, hasPanel: false, status: null, [Registered("height"), Registered("reset")]);

        Assert.Equal(["osc/height", "osc/reset"], board.Keys());
    }

    [Fact]
    public async Task PressingAPanelShortcutHandsItsScreenToTheWindow()
    {
        var (board, _, _) = New();
        board.AddHost();
        board.AddModule("hrs", "Heart Rate", null, null, hasPanel: true, status: null, []);

        var opened = new List<string>();
        await board.PressAsync("hrs", opened.Add);
        await board.PressAsync("settings", opened.Add);

        Assert.Equal(["hrs", "settings"], opened);
    }

    [Fact]
    public async Task PressingARegisteredShortcutCallsItsTriggerWithoutOpeningTheWindow()
    {
        var runs = 0;
        var (board, _, _) = New();
        board.AddModule("videourl", "Video URL", null, null, hasPanel: true, status: null,
            [Registered("resolve-latest", _ => { runs++; return Task.CompletedTask; })]);

        var opened = new List<string>();
        await board.PressAsync("videourl/resolve-latest", opened.Add);

        Assert.Equal(1, runs);
        Assert.Empty(opened);
    }

    // 画面から来た名前を信じない。台帳に無いものは何もしない（残すのは警告だけ）。
    [Fact]
    public async Task PressingANameThatIsNotThereDoesNothing()
    {
        var (board, sent, logs) = New();
        board.AddHost();

        var opened = new List<string>();
        await board.PressAsync("nothing/here", opened.Add);

        Assert.Empty(opened);
        Assert.Empty(sent.On("toast"));
        Assert.Contains(logs.Warnings, w => w.Contains("nothing/here"));
    }

    // 押した人は手首にいて、ログは見えないので、エラーのトーストにする。
    [Fact]
    public async Task ATriggerThatThrowsShowsAnErrorToast()
    {
        var (board, sent, logs) = New();
        board.AddModule("x", "X", null, null, hasPanel: false, status: null,
            [Registered("boom", _ => throw new InvalidOperationException("壊れた"))]);

        await board.PressAsync("x/boom", _ => { });

        var toast = Assert.Single(sent.On("toast"));
        Assert.Equal("host.shortcut.failed", Recording.Field(toast.Payload, "title"));
        Assert.Equal("壊れた", Recording.Field(toast.Payload, "body"));
        Assert.Contains(logs.Warnings, w => w.Contains("x/boom"));
    }

    // OnTriggerAsync が返すまでの押し直しは呼ばない（解くのに数秒かかる間に、手応えが無くてもう一度押す）。返したあとはまた呼ぶ。
    [Fact]
    public async Task PressingAgainWhileItRunsDoesNotStartASecondRun()
    {
        var runs = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource();
        var (board, _, _) = New();
        board.AddModule("x", "X", null, null, hasPanel: false, status: null,
            [Registered("slow", async _ => { Interlocked.Increment(ref runs); started.TrySetResult(); await release.Task; })]);

        var first = board.PressAsync("x/slow", _ => { });

        // 呼ばれなければ started は立たない。待ち切らずに落とす（試験が固まらないように）。
        await started.Task.WaitAsync(Patience);

        // 2 本目が走ってしまうと、この押す処理は release まで戻らない。待ち切らずに落とす。
        await board.PressAsync("x/slow", _ => { }).WaitAsync(Patience);
        Assert.Equal(1, runs);

        release.SetResult();
        await first;
        await board.PressAsync("x/slow", _ => { });

        Assert.Equal(2, runs);
    }

    // 押した側のスレッドで待たない（CEF のスレッドから来る）。
    [Fact]
    public async Task ASlowSynchronousTriggerDoesNotHoldThePress()
    {
        using var gate = new ManualResetEventSlim();
        var (board, _, _) = New();
        board.AddModule("x", "X", null, null, hasPanel: false, status: null,
            [Registered("blocking", _ => { gate.Wait(Patience); return Task.CompletedTask; })]);

        var press = board.PressAsync("x/blocking", _ => { });

        Assert.False(press.IsCompleted);
        gate.Set();
        await press;
    }

    [Fact]
    public async Task ShuttingDownCancelsARunningShortcutQuietly()
    {
        var started = new TaskCompletionSource();
        var (board, sent, _) = New();
        board.AddModule("x", "X", null, null, hasPanel: false, status: null,
            [Registered("wait", async token => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); })]);

        var press = board.PressAsync("x/wait", _ => { });
        await started.Task.WaitAsync(Patience);
        board.Cancel();
        await press.WaitAsync(Patience);

        Assert.Empty(sent.On("toast"));
    }

    // 書かれたスレッドでは配らず、周期（<see cref="ShortcutBoard.Flush"/>）でまとめて 1 回、変わったときだけ配る。
    [Fact]
    public void ContentIsSentOnTheSweepOnlyWhenItChanged()
    {
        var bpm = new ModuleStatus { Text = "72 bpm", State = StatusState.Ok };
        var resolving = new ModuleStatus();
        var (board, sent, _) = New();
        board.AddHost();
        board.AddModule("hrs", "Heart Rate", null, null, hasPanel: true, status: bpm, []);
        board.AddModule("videourl", "Video URL", null, null, hasPanel: true, status: null, [Registered("resolve-latest", status: resolving)]);

        board.Flush();
        var first = Assert.Single(sent.On("shortcutStatus"));
        Assert.Equal(
            [new ShortcutBoardContent("hrs", "72 bpm", "ok"), new ShortcutBoardContent("videourl/resolve-latest", "", "off")],
            (ShortcutBoardContent[])first.Payload);

        bpm.Text = "73 bpm";
        Assert.Single(sent.On("shortcutStatus"));
        board.Flush();
        board.Flush();

        var second = sent.On("shortcutStatus");
        Assert.Equal(2, second.Length);
        Assert.Equal("73 bpm", ((ShortcutBoardContent[])second[1].Payload)[0].Text);

        bpm.Text = "73 bpm";
        board.Flush();
        Assert.Equal(2, sent.On("shortcutStatus").Length);

        // 掃く前に変わって元へ戻った。配ったものと同じなので配らない。
        bpm.Text = "74 bpm";
        bpm.Text = "73 bpm";
        board.Flush();
        Assert.Equal(2, sent.On("shortcutStatus").Length);
    }

    // パスの字以外が混ざった線のアイコンは使わない（シェルの origin の属性に、モジュールの書いた字が入るため）。
    [Fact]
    public void AShortcutWithoutAUsableIconOfItsOwnTakesTheModules()
    {
        const string modulePath = "M1 1L2 2";
        var (board, _, _) = New();
        board.AddModule("x", "X", "★", modulePath, hasPanel: false, status: null,
            [Registered("own-path", iconPath: "M3 3L4 4"), Registered("none"), Registered("unsafe", iconPath: "<script>")]);
        board.AddModule("y", "Y", "♪", null, hasPanel: false, status: null, [Registered("glyph-only")]);

        Assert.Equal(((string?)null, (string?)"M3 3L4 4"), Icons("x/own-path"));
        Assert.Equal(((string?)"★", (string?)modulePath), Icons("x/none"));
        Assert.Equal(((string?)"★", (string?)modulePath), Icons("x/unsafe"));
        Assert.Equal(((string?)"♪", (string?)null), Icons("y/glyph-only"));

        (string?, string?) Icons(string key) => (board.Find(key)!.Icon, board.Find(key)!.IconPath);
    }

    // marked は開く画面ではないので view には入れない（入れると手首が自分で開いてしまう）。
    [Fact]
    public void TheScreenGetsWhatEachShortcutDoes()
    {
        var (board, _, _) = New();
        board.AddHost();
        board.AddModule("videourl", "Video URL", null, null, hasPanel: true,
            status: new ModuleStatus { Text = "失敗 2", State = StatusState.Bad },
            [Registered("resolve-latest", name: "videourl.shortcut.resolveLatest"), Registered("failures", marked: "videourl#failures")]);

        var items = board.ForBridge();

        Assert.Equal(6, items.Length);
        Assert.Null(Recording.Field(items[4], "marked"));
        Assert.Equal("videourl#failures", Recording.Field(items[5], "marked"));
        Assert.Null(Recording.Field(items[5], "view"));
        Assert.True((bool)Recording.Field(items[5], "runs")!);
        Assert.Equal("host.nav.log", Recording.Field(items[0], "name"));
        Assert.Equal("i-log", Recording.Field(items[0], "hostIcon"));

        Assert.Equal("videourl", Recording.Field(items[3], "view"));
        Assert.Equal("videourl.name", Recording.Field(items[3], "name"));
        Assert.Equal("Video URL", Recording.Field(items[3], "fallback"));
        Assert.Equal("失敗 2", Recording.Field(items[3], "text"));
        Assert.Equal("bad", Recording.Field(items[3], "state"));
        Assert.False((bool)Recording.Field(items[3], "runs")!);

        Assert.Equal("videourl/resolve-latest", Recording.Field(items[4], "key"));
        Assert.Equal("videourl", Recording.Field(items[4], "source"));
        Assert.Null(Recording.Field(items[4], "view"));
        Assert.True((bool)Recording.Field(items[4], "runs")!);
        Assert.Equal("videourl.shortcut.resolveLatest", Recording.Field(items[4], "name"));
        Assert.Null(Recording.Field(items[4], "page"));
        Assert.False((bool)Recording.Field(items[4], "draggable")!);
        Assert.False((bool)Recording.Field(items[3], "draggable")!);
    }

    // 手首は押した・動いた・離したを C# の受け口へ送るので、押しの口へ来ても呼ばない。画面へは draggable で渡す（手首はこれで道を分ける）。
    [Fact]
    public async Task ADraggableShortcutIsNotPressedAndIsMarkedForTheWrist()
    {
        var (board, sent, logs) = New();
        var slider = new Slider();
        board.AddModule("osc", "OSC", null, null, hasPanel: false, status: null,
            [new RegisteredShortcut("height", "height", null, null, null, slider), Registered("reset")]);

        var opened = new List<string>();
        await board.PressAsync("osc/height", opened.Add);

        Assert.Equal(0, slider.Calls);
        Assert.Empty(opened);
        Assert.Empty(sent.On("toast"));
        Assert.Contains(logs.Warnings, w => w.Contains("osc/height"));
        Assert.Same(slider, board.FindDraggable("osc/height"));
        Assert.Null(board.FindDraggable("osc/reset"));

        var items = board.ForBridge();
        Assert.True((bool)Recording.Field(items[0], "draggable")!);
        Assert.True((bool)Recording.Field(items[0], "runs")!);
        Assert.False((bool)Recording.Field(items[1], "draggable")!);
    }

    private sealed class Slider : IDraggableWristShortcut
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public string Id => "unused";

        public string Name => "unused";

        public Task OnStartDragAsync(ShortcutDragEventParams e, CancellationToken cancellationToken) => Count();

        public Task OnDraggingAsync(ShortcutDragEventParams e, CancellationToken cancellationToken) => Count();

        public Task OnEndDragAsync(ShortcutDragEventParams e, CancellationToken cancellationToken) => Count();

        private Task Count()
        {
            Interlocked.Increment(ref _calls);
            return Task.CompletedTask;
        }
    }

    // 手首はこの origin で持ち主を決める。ファイルがあるかはモジュールの web/ に聞き、聞いた字のまま URL に組む。
    [Fact]
    public void ATilePageOpensFromTheModulesOwnOrigin()
    {
        var (board, _, logs) = New();
        var asked = new List<string>();

        var serves = board.AddModule("osc", "OSC", null, null, hasPanel: false, status: null,
            [Registered("height", page: "tiles/height_slider.html"), Registered("plain")], page => { asked.Add(page); return true; });

        Assert.True(serves);
        Assert.Equal(["tiles/height_slider.html"], asked);
        Assert.Equal("tiles/height_slider.html", board.Find("osc/height")!.Page);
        Assert.Null(board.Find("osc/plain")!.Page);

        var items = board.ForBridge();
        Assert.Equal("spoitable://osc/tiles/height_slider.html", Recording.Field(items[0], "page"));
        Assert.Null(Recording.Field(items[1], "page"));
        Assert.Empty(logs.Warnings);
    }

    // 画面がこの字で URL を組むので、?・#・.. を入れさせない。登録のときにも断るが（ModuleShortcutsTests）、URL を組む台帳でももう一度見る。ショートカットは並べる（ページ無しのタイルになるだけ）。
    [Theory]
    [InlineData("../other/tile.html")]
    [InlineData("/tile.html")]
    [InlineData("tile.html?x=1")]
    [InlineData("tile.html#x")]
    [InlineData("tile.htm")]
    [InlineData("tiles\\tile.html")]
    [InlineData("my tile.html")]
    [InlineData("tile.html\n")]
    [InlineData("")]
    public void APageWithStrayCharactersIsDrawnWithoutIt(string page)
    {
        var (board, _, logs) = New();
        var asked = new List<string>();

        var serves = board.AddModule("osc", "OSC", null, null, hasPanel: false, status: null,
            [Registered("height", page: page)], p => { asked.Add(p); return true; });

        Assert.False(serves);
        Assert.Empty(asked);
        Assert.Null(board.Find("osc/height")!.Page);
        Assert.Contains(logs.Warnings, w => w.Contains("osc") && w.Contains("height") && w.Contains("without it"));
    }

    // 中が 404 のタイルにしない。
    [Fact]
    public void APageThatIsNotThereIsDrawnWithoutIt()
    {
        var (board, _, logs) = New();

        var missing = board.AddModule("osc", "OSC", null, null, hasPanel: false, status: null, [Registered("height", page: "tile.html")], _ => false);
        var unasked = board.AddModule("hrs", "HRS", null, null, hasPanel: true, status: null, [Registered("live", page: "tile.html")]);

        Assert.False(missing);
        Assert.False(unasked);
        Assert.Null(board.Find("osc/height")!.Page);
        Assert.Null(board.Find("hrs/live")!.Page);
        Assert.Equal(2, logs.Warnings.Count(w => w.Contains("no such file")));
    }
}
