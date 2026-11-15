using System.Globalization;
using SpoitableToolKit.Abstractions.Shortcuts;
using SpoitableToolKit.Host.Notifications;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Shortcuts;
using SpoitableToolKit.Host.Shortcuts.Gestures;
using SpoitableToolKit.Tests.Fakes;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Shortcuts.Gestures;

/// <summary>
/// 手首のドラッグだけのタイル（<see cref="IDraggableWristShortcut"/>）への、押した・動いた・離した・途切れたの受け口。
/// 押した瞬間に <c>OnStartDragAsync</c>、動けば <c>OnDraggingAsync</c>、離したら <c>OnEndDragAsync</c>。押すだけのタイルは click で台帳へ行く（ここを通らない）。
/// タイルは 100 × 100 画素（<see cref="Rig"/>）。位置はタイルの中の割合で届くので、画素の 1/100 になる。片付くのは <see cref="ShortcutGestures.Idle"/> で待つ（sleep しない）。
/// </summary>
public sealed class ShortcutGesturesTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private const string Key = "x/tile";

    /// <summary>手首のページの読み込みの印（試験ではふつう 1 つ）。</summary>
    private const string Page = "page-1";

    /// <summary><b>押した瞬間にドラッグが始まる</b>（動かすのを待たない）。動いた分は Dragging、離したら最後の位置で End。</summary>
    [Fact]
    public async Task APressStartsTheDragAtOnceAndTheReleaseEndsIt()
    {
        var rig = new Rig();

        rig.Down(1, 20, 20);
        await WaitForAsync(() => rig.Tile.Calls.Count == 1);
        Assert.Equal(["start 0.2,0.2 from 0.2,0.2"], rig.Tile.Calls);

        rig.Move(1, 40, 30);
        rig.Up(1, 45, 35);
        await rig.SettledAsync();

        Assert.Equal(["start 0.2,0.2 from 0.2,0.2", "drag 0.4,0.3", "end 0.45,0.35"], rig.Tile.Calls);
    }

    /// <summary>触って離すだけでも Start と End が来る（押しではない。押すだけのタイルは別の種類）。</summary>
    [Fact]
    public async Task TouchingAndLettingGoIsAShortDrag()
    {
        var rig = new Rig();

        rig.Down(1, 50, 50);
        rig.Up(1, 50, 50);
        await rig.SettledAsync();

        Assert.Equal(["start 0.5,0.5 from 0.5,0.5", "end 0.5,0.5"], rig.Tile.Calls);
    }

    /// <summary>
    /// <b>押しが途切れたら（離したのが届かなかった）、終わりを 1 回</b>（最後の位置で）。途切れるのは: 手首のページが知らせたとき
    /// （<see cref="ShortcutPointerKind.Cancel"/>）と、次の押しが来たとき。途切れたあとに届いた離しは捨てる。
    /// </summary>
    [Fact]
    public async Task AnInterruptedPressEndsTheDragOnce()
    {
        var rig = new Rig();

        rig.Down(1, 10, 10);
        rig.Move(1, 30, 10);
        rig.Cancel(1);
        rig.Cancel(1);
        rig.Up(1, 50, 10);
        await rig.SettledAsync();
        Assert.Equal(["start 0.1,0.1 from 0.1,0.1", "drag 0.3,0.1", "end 0.3,0.1"], rig.Tile.Calls);

        // 別のタイルを押したら、引いていた方は終わる（離したのが届かなかった）。
        rig.Tile.Clear();
        rig.Down(2, 10, 10);
        rig.Move(2, 30, 10);
        rig.Down(3, 10, 10, "x/other");
        rig.Up(3, 10, 10, "x/other");
        await rig.SettledAsync();
        Assert.Equal(["start 0.1,0.1 from 0.1,0.1", "drag 0.3,0.1", "end 0.3,0.1"], rig.Tile.Calls);
        Assert.Equal(["start 0.1,0.1 from 0.1,0.1", "end 0.1,0.1"], rig.Other.Calls);
    }

    /// <summary>
    /// <b>知らせの番号が今の押しと違えば捨てる。前に見た番号以下の押し始めも捨てる</b> — 順が狂っても、押しを失うだけで、終わらないドラッグは残らない。
    /// </summary>
    [Fact]
    public async Task StaleOrOutOfOrderPointersAreDropped()
    {
        var rig = new Rig();

        rig.Down(5, 50, 50);
        rig.Down(3, 10, 10);
        rig.Move(4, 90, 90);
        rig.Up(4, 90, 90);
        rig.Up(5, 60, 50);
        await rig.SettledAsync();
        Assert.Equal(["start 0.5,0.5 from 0.5,0.5", "end 0.6,0.5"], rig.Tile.Calls);

        // 済んだ番号の押し始めがあとから来ても、新しい押しにしない。
        rig.Down(5, 50, 50);
        rig.Up(5, 50, 50);
        await rig.SettledAsync();
        Assert.Equal(["start 0.5,0.5 from 0.5,0.5", "end 0.6,0.5"], rig.Tile.Calls);
    }

    /// <summary>
    /// <b>手首の板が隠れた・SteamVR が閉じた（C# が知っている途切れ）は、ページの離しを待たずに終える</b>（<c>VrLoop</c> が <see cref="ShortcutGestures.CutOff"/> を呼ぶ）。
    /// 眠らせた・閉じた browser に離しが届くとは限らず、届かないとドラッグが始まったまま終わらない。
    /// 途切れたあと<b>同じタイル</b>をもう一度押すと効く（終わりが返り切ってから）。あとから届いた離しは捨てる。
    /// </summary>
    [Fact]
    public async Task CuttingOffAPressEndsItWithoutWaitingForTheRelease()
    {
        var rig = new Rig();

        rig.Down(1, 50, 50);
        rig.Move(1, 60, 50);
        rig.Gestures.CutOff();
        rig.Up(1, 70, 50);
        await rig.SettledAsync();
        Assert.Equal(["start 0.5,0.5 from 0.5,0.5", "drag 0.6,0.5", "end 0.6,0.5"], rig.Tile.Calls);

        rig.Gestures.CutOff();
        rig.Down(2, 50, 50);
        rig.Up(2, 50, 50);
        await rig.SettledAsync();
        Assert.Equal(["start 0.5,0.5 from 0.5,0.5", "drag 0.6,0.5", "end 0.6,0.5", "start 0.5,0.5 from 0.5,0.5", "end 0.5,0.5"], rig.Tile.Calls);
    }

    /// <summary>手首のページが作り直されたら（SteamVR に入り直した）、番号は 1 から数え直す。印（page）が変わった押し始めは、前のページの番号より小さくても受ける。前のページの遅れた知らせは捨てる（今の押しを途切れさせない）。</summary>
    [Fact]
    public async Task AReloadedWristPageCountsItsPressesAfresh()
    {
        var rig = new Rig();

        rig.Down(40, 50, 50, page: "before");
        rig.Up(40, 50, 50, page: "before");
        await rig.SettledAsync();
        rig.Tile.Clear();

        rig.Down(1, 10, 10, page: "after");
        rig.Cancel(1, page: "before");
        rig.Up(1, 10, 10, page: "before");
        rig.Move(1, 30, 10, page: "after");
        rig.Up(1, 30, 10, page: "after");
        await rig.SettledAsync();
        Assert.Equal(["start 0.1,0.1 from 0.1,0.1", "drag 0.3,0.1", "end 0.3,0.1"], rig.Tile.Calls);

        rig.Down(2, 50, 50, page: "after");
        rig.Up(2, 50, 50, page: "after");
        await rig.SettledAsync();
        Assert.Equal(["start 0.1,0.1 from 0.1,0.1", "drag 0.3,0.1", "end 0.3,0.1", "start 0.5,0.5 from 0.5,0.5", "end 0.5,0.5"], rig.Tile.Calls);
    }

    /// <summary>
    /// <b>ドラッグの最中の動きは、前の呼び出しが返るまでに来た分を最後の 1 つにまとめる</b>（溜めない）。
    /// 終わりはまとめず、<b>最後の Dragging が返ってから、離した位置で 1 回</b>。
    /// </summary>
    [Fact]
    public async Task MovesWhileTheCallRunsAreFoldedIntoTheLatestAndTheEndComesLast()
    {
        var rig = new Rig();
        var release = new TaskCompletionSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Tile.OnDragging = async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        };

        rig.Down(1, 0, 0);
        rig.Move(1, 30, 0);
        await entered.Task.WaitAsync(Patience);

        rig.Move(1, 40, 0);
        rig.Move(1, 50, 0);
        rig.Move(1, 60, 0);
        rig.Up(1, 70, 0);
        release.SetResult();
        await rig.SettledAsync();

        Assert.Equal(["start 0,0 from 0,0", "drag 0.3,0", "drag 0.6,0", "end 0.7,0"], rig.Tile.Calls);
    }

    /// <summary>
    /// モジュールが投げても<b>終わりは必ず呼ぶ</b>。トーストは<b>1 回のドラッグで 1 回</b>（押した人は手首にいて、ログは見えない）。
    /// </summary>
    [Fact]
    public async Task AThrowingDragStillEndsAndShowsOneToast()
    {
        var rig = new Rig();
        rig.Tile.OnStart = _ => throw new InvalidOperationException("始めで壊れた");
        rig.Tile.OnDragging = _ => throw new InvalidOperationException("途中で壊れた");

        rig.Down(1, 0, 0);
        rig.Move(1, 20, 0);
        rig.Move(1, 30, 0);
        rig.Up(1, 30, 0);
        await rig.SettledAsync();

        Assert.Equal("start 0,0 from 0,0", rig.Tile.Calls[0]);
        Assert.Equal("end 0.3,0", rig.Tile.Calls[^1]);
        var toast = Assert.Single(rig.Sent.On("toast"));
        Assert.Equal("始めで壊れた", Recording.Field(toast.Payload, "body"));
        Assert.False(rig.Board.IsRunning(Key));
    }

    /// <summary>
    /// <b>前のドラッグの呼び出しが返り切るまでに押し始めた押しは捨てる</b>（2 本目のドラッグは走らせない）— <b>離す前に前の呼び出しが返っても</b>、その押しは捨てたまま。
    /// 返ったあとに押し始めたものは、また受ける。
    /// </summary>
    [Fact]
    public async Task APressStartedWhileTheLastDragStillRunsIsDropped()
    {
        var rig = new Rig();
        var release = new TaskCompletionSource();
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Tile.OnEnd = async () =>
        {
            ended.TrySetResult();
            await release.Task;
        };

        rig.Down(1, 50, 50);
        rig.Up(1, 50, 50);
        await ended.Task.WaitAsync(Patience);
        var first = rig.Gestures.Idle;

        // 押すのは、まだ終わりが返っていない間。離すのは、返り切ったあと。
        rig.Down(2, 20, 20);
        rig.Move(2, 30, 20);
        release.SetResult();
        await first.WaitAsync(Patience);
        Assert.False(rig.Board.IsRunning(Key));
        rig.Up(2, 30, 20);
        Assert.Equal(["start 0.5,0.5 from 0.5,0.5", "end 0.5,0.5"], rig.Tile.Calls);

        // 返り切ったあとに押し始めたものは受ける。
        rig.Tile.OnEnd = null;
        rig.Down(3, 10, 10);
        rig.Up(3, 10, 10);
        await rig.SettledAsync();
        Assert.Equal(["start 0.5,0.5 from 0.5,0.5", "end 0.5,0.5", "start 0.1,0.1 from 0.1,0.1", "end 0.1,0.1"], rig.Tile.Calls);
    }

    /// <summary>Layout Mode の間は押しを受けない（タイルは並べ替えるもの）。</summary>
    [Fact]
    public async Task NothingRunsInLayoutMode()
    {
        var rig = new Rig { Layout = true };

        rig.Down(1, 50, 50);
        rig.Move(1, 90, 50);
        rig.Up(1, 90, 50);
        await rig.SettledAsync();

        Assert.Empty(rig.Tile.Calls);
    }

    /// <summary>
    /// <b>画面から来た値はそのまま信じない</b>: 数でない・大きさが 0・台帳に無い名前・パネルを開くタイル・<b>押すだけのタイル</b>は捨てる
    /// （押すだけのタイルは click で台帳へ行く。ここへ来ても押しにもドラッグにもしない）。捨てたあとも、正しい押しは通る。
    /// </summary>
    [Fact]
    public async Task PointersThatAreNotOnADraggableShortcutAreIgnored()
    {
        var rig = new Rig();

        rig.Gestures.Pointer(Page, Key, 1, ShortcutPointerKind.Down, float.NaN, 0, 100, 100);
        rig.Gestures.Pointer(Page, Key, 2, ShortcutPointerKind.Down, 0, 0, 0, 100);
        rig.Gestures.Pointer(Page, Key, 3, ShortcutPointerKind.Down, 0, float.PositiveInfinity, 100, 100);
        rig.Down(4, 50, 50, "x");
        rig.Up(4, 50, 50, "x");
        rig.Down(5, 50, 50, "nobody/here");
        rig.Up(5, 50, 50, "nobody/here");
        rig.Down(6, 50, 50, "x/press");
        rig.Move(6, 90, 50, "x/press");
        rig.Up(6, 90, 50, "x/press");
        await rig.SettledAsync();
        Assert.Empty(rig.Tile.Calls);
        Assert.Equal(0, rig.Pressed.Presses);

        // 捨てたことは警告に残す（押すだけのタイルへ来たのは、手首のページの道の分け違い）。
        Assert.Contains(rig.Logs.Warnings, w => w.Contains("x/press"));
        Assert.Contains(rig.Logs.Warnings, w => w.Contains("nobody/here"));

        // 押している最中の数でない動きも捨てる。
        rig.Down(7, 50, 50);
        rig.Gestures.Pointer(Page, Key, 7, ShortcutPointerKind.Move, float.NaN, 50, 100, 100);
        rig.Gestures.Pointer(Page, Key, 7, ShortcutPointerKind.Move, 50, 50, 100, -1);
        rig.Up(7, 50, 50);
        await rig.SettledAsync();
        Assert.Equal(["start 0.5,0.5 from 0.5,0.5", "end 0.5,0.5"], rig.Tile.Calls);
    }

    /// <summary>位置はタイルの中の割合で、<b>タイルの外へ引いたら 0〜1 の外になる</b>（切り詰めない）。</summary>
    [Fact]
    public async Task APositionOutsideTheTileIsNotClamped()
    {
        var rig = new Rig();

        rig.Down(1, 90, 50);
        rig.Move(1, 150, -20);
        rig.Up(1, 150, -20);
        await rig.SettledAsync();

        Assert.Equal(["start 0.9,0.5 from 0.9,0.5", "drag 1.5,-0.2", "end 1.5,-0.2"], rig.Tile.Calls);
    }

    /// <summary>
    /// 畳むときは、<b>呼び出しが止まっている間（押したまま動かしていない）でも</b>、終わりを呼ばずに終える。終えないと台帳の「走っている」が残る。
    /// </summary>
    [Fact]
    public async Task ShuttingDownWhileADragRestsFinishesItWithoutTheEnd()
    {
        var rig = new Rig();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Tile.OnStart = _ =>
        {
            started.TrySetResult();
            return Task.CompletedTask;
        };

        rig.Down(1, 0, 0);
        await started.Task.WaitAsync(Patience);
        var run = rig.Gestures.Idle;

        rig.Gestures.Stop();
        rig.Board.Cancel();
        await run.WaitAsync(Patience);

        Assert.Equal(["start 0,0 from 0,0"], rig.Tile.Calls);
        Assert.False(rig.Board.IsRunning(Key));
    }

    /// <summary>
    /// 畳むときはドラッグの途中でも<b>終わりを呼ばずに</b>終える（token が切れる）。台帳の「走っている」は外れる。畳んだあとの知らせは受けない。
    /// </summary>
    [Fact]
    public async Task ShuttingDownStopsADragWithoutTheEnd()
    {
        var rig = new Rig();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Tile.OnStart = async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };

        rig.Down(1, 0, 0);
        await started.Task.WaitAsync(Patience);
        var run = rig.Gestures.Idle;

        rig.Gestures.Stop();
        rig.Down(2, 50, 50, "x/other");
        rig.Board.Cancel();
        await run.WaitAsync(Patience);
        rig.Up(1, 20, 0);

        Assert.Equal(["start 0,0 from 0,0"], rig.Tile.Calls);
        Assert.Empty(rig.Other.Calls);
        Assert.False(rig.Board.IsRunning(Key));
        Assert.Empty(rig.Sent.On("toast"));
    }

    private static async Task WaitForAsync(Func<bool> done)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!done())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("待っていたことが起きなかった");

            await Task.Delay(10);
        }
    }

    /// <summary>
    /// 台帳 1 つに、パネルを開くタイル（<c>x</c>）・ドラッグだけのショートカット 2 つ（<c>x/tile</c>・<c>x/other</c>）・押すだけのショートカット（<c>x/press</c>）。
    /// </summary>
    private sealed class Rig
    {
        public Rig()
        {
            Sent = new Recording();
            var logs = Logs;
            Board = new ShortcutBoard(Sent, new NotificationManager(Sent, new HostConfig(), logs), new FakeLocalizer(), logs);
            Board.AddModule("x", "X", null, null, hasPanel: true, status: null,
            [
                new RegisteredShortcut("tile", "tile", null, null, null, Tile),
                new RegisteredShortcut("other", "other", null, null, null, Other),
                new RegisteredShortcut("press", "press", null, null, null, Pressed),
            ]);

            Gestures = new ShortcutGestures(Board, () => Layout, logs);
        }

        public Recording Sent { get; }

        public Logs Logs { get; } = new();

        public ShortcutBoard Board { get; }

        public ShortcutGestures Gestures { get; }

        public Recorder Tile { get; } = new();

        public Recorder Other { get; } = new();

        public PressCounter Pressed { get; } = new();

        public bool Layout { get; set; }

        public void Down(long seq, float x, float y, string key = Key, string page = Page) => Gestures.Pointer(page, key, seq, ShortcutPointerKind.Down, x, y, 100, 100);

        public void Move(long seq, float x, float y, string key = Key, string page = Page) => Gestures.Pointer(page, key, seq, ShortcutPointerKind.Move, x, y, 100, 100);

        public void Up(long seq, float x, float y, string key = Key, string page = Page) => Gestures.Pointer(page, key, seq, ShortcutPointerKind.Up, x, y, 100, 100);

        public void Cancel(long seq, string key = Key, string page = Page) => Gestures.Pointer(page, key, seq, ShortcutPointerKind.Cancel, 0, 0, 100, 100);

        /// <summary>最後に始めたドラッグが片付くまで待つ。</summary>
        public Task SettledAsync() => Gestures.Idle.WaitAsync(Patience);
    }

    /// <summary>呼ばれた順を控える、ドラッグだけのショートカット。位置は小数 2 桁まで。</summary>
    private sealed class Recorder : IDraggableWristShortcut
    {
        private readonly Lock _gate = new();

        private readonly List<string> _calls = [];

        public List<string> Calls
        {
            get
            {
                lock (_gate) return [.. _calls];
            }
        }

        public Func<CancellationToken, Task>? OnStart { get; set; }

        public Func<ShortcutDragEventParams, Task>? OnDragging { get; set; }

        public Func<Task>? OnEnd { get; set; }

        public string Id => "unused";

        public string Name => "unused";

        public Task OnStartDragAsync(ShortcutDragEventParams e, CancellationToken cancellationToken)
        {
            Add($"start {Format(e.Position)} from {Format(e.StartPosition)}");
            return OnStart?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }

        public Task OnDraggingAsync(ShortcutDragEventParams e, CancellationToken cancellationToken)
        {
            Add($"drag {Format(e.Position)}");
            return OnDragging?.Invoke(e) ?? Task.CompletedTask;
        }

        public Task OnEndDragAsync(ShortcutDragEventParams e, CancellationToken cancellationToken)
        {
            Add($"end {Format(e.Position)}");
            return OnEnd?.Invoke() ?? Task.CompletedTask;
        }

        public void Clear()
        {
            lock (_gate) _calls.Clear();
        }

        private void Add(string call)
        {
            lock (_gate) _calls.Add(call);
        }

        private static string Format(System.Numerics.Vector2 v) =>
            $"{Math.Round(v.X, 2).ToString(CultureInfo.InvariantCulture)},{Math.Round(v.Y, 2).ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>押すだけのショートカット（押された回数だけ数える）。ドラッグの受け口に来ても呼ばれないことを見る。</summary>
    private sealed class PressCounter : IClickableWristShortcut
    {
        private int _presses;

        public int Presses => Volatile.Read(ref _presses);

        public string Id => "unused";

        public string Name => "unused";

        public Task OnTriggerAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _presses);
            return Task.CompletedTask;
        }
    }
}
