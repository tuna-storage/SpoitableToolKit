using SpoitableToolKit.Abstractions.Events.VRChat;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Abstractions.VrcLog;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Status;
using SpoitableToolKit.Host.VRChat;
using SpoitableToolKit.Tests.Fakes;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.VRChat;

public sealed class VRChatPresenceTests
{
    /// <summary>試験では見張りの時計を回さない（<see cref="VRChatPresence.Poll"/> を手で呼ぶ）。</summary>
    private static readonly TimeSpan Never = TimeSpan.FromHours(1);

    private sealed class Rig : IDisposable
    {
        private readonly FakeModule _module = new("p");

        public bool Running { get; set; }

        public Func<bool>? Probe { get; set; }

        public List<string> Events { get; } = [];

        public EventHub Hub { get; } = VRChatHub();

        public VRChatPresence Presence { get; }

        public Rig(bool running = false, Logs? logs = null)
        {
            Running = running;
            Presence = new VRChatPresence(() => Probe?.Invoke() ?? Running, Hub,
                logs is null ? NullLogger<VRChatPresence>.Instance : logs.CreateLogger<VRChatPresence>());
            Listen(new On(started: () => Events.Add("started"), shutdown: () => Events.Add("shutdown")));
        }

        public void Listen(IVRChatEventListener listener) => Hub.Source.AddListener<IVRChatEventListener>(_module, listener);

        public void Dispose() => Presence.Dispose();
    }

    private sealed class On(Action? started = null, Action? shutdown = null) : IVRChatEventListener
    {
        public void OnVRChatStarted(VRChatStartedEventParams e) => started?.Invoke();

        public void OnVRChatShutdown(VRChatShutdownEventParams e) => shutdown?.Invoke();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheStartupCheckSetsTheStateWithoutAnEvent(bool running)
    {
        using var rig = new Rig(running);

        rig.Presence.CheckOnce();

        Assert.Equal(running, rig.Presence.IsRunning);
        Assert.Empty(rig.Events);
    }

    // 読み込みの間に登録したモジュールも受け取らない。
    [Fact]
    public void RunningAtStartupGivesNoStartedAtTheFirstPoll()
    {
        using var rig = new Rig(running: true);

        rig.Presence.Begin(() => { }, Never);
        rig.Presence.Poll();

        Assert.True(rig.Presence.IsRunning);
        Assert.Empty(rig.Events);
    }

    // 読み込みの間に読む状態は、起動時に確かめた値のまま（途中で見張ると、先に読まれたモジュールだけが出来事を受ける）。
    // 見張りの時計は本当に回す（10 ms おき）: 読み込みに 300 ms かかる間、30 回分の見張りが来ていない。
    [Fact]
    public void AStartDuringLoadingIsRaisedOnceAfterLoading()
    {
        using var rig = new Rig(running: false);
        using var started = new ManualResetEventSlim();
        rig.Listen(new On(started: started.Set));
        bool? seenWhileLoading = null;
        var raisedWhileLoading = -1;

        rig.Presence.Begin(() =>
        {
            rig.Running = true;
            Thread.Sleep(300);
            seenWhileLoading = rig.Presence.IsRunning;
            raisedWhileLoading = rig.Events.Count;
        }, TimeSpan.FromMilliseconds(10));

        Assert.False(seenWhileLoading);
        Assert.Equal(0, raisedWhileLoading);

        Assert.True(started.Wait(TimeSpan.FromSeconds(10)), "the watch did not notice VRChat starting after loading");
        Thread.Sleep(100);

        Assert.Equal(["started"], rig.Events);
        Assert.True(rig.Presence.IsRunning);
    }

    [Fact]
    public void BeginDoesNotCheckAgainAfterTheAppHasChecked()
    {
        using var rig = new Rig(running: true);
        rig.Presence.CheckOnce();

        rig.Running = false;
        rig.Presence.Begin(() => Assert.True(rig.Presence.IsRunning), Never);

        Assert.True(rig.Presence.IsRunning);
        rig.Presence.Poll();
        Assert.Equal(["shutdown"], rig.Events);
    }

    [Fact]
    public void APollRaisesAnEventOnlyWhenTheStateChanges()
    {
        using var rig = new Rig(running: true);
        rig.Presence.CheckOnce();

        rig.Presence.Poll();
        rig.Running = false;
        rig.Presence.Poll();
        rig.Presence.Poll();
        rig.Running = true;
        rig.Presence.Poll();

        Assert.Equal(["shutdown", "started"], rig.Events);
    }

    [Fact]
    public void TheStateIsAlreadyNewInsideTheEvent()
    {
        using var rig = new Rig(running: true);
        rig.Presence.CheckOnce();
        List<bool> seen = [];
        rig.Listen(new On(started: () => seen.Add(rig.Presence.IsRunning), shutdown: () => seen.Add(rig.Presence.IsRunning)));

        rig.Running = false;
        rig.Presence.Poll();
        rig.Running = true;
        rig.Presence.Poll();

        Assert.Equal([false, true], seen);
    }

    [Fact]
    public void AProbeThatThrowsKeepsTheLastAnswer()
    {
        var logs = new Logs();
        using var rig = new Rig(running: true, logs);
        rig.Presence.CheckOnce();

        rig.Probe = () => throw new InvalidOperationException("access denied");
        rig.Presence.Poll();

        Assert.True(rig.Presence.IsRunning);
        Assert.Empty(rig.Events);
        Assert.Contains(logs.Warnings, line => line.Contains("Could not check whether VRChat is running"));
    }

    // 受け取る側が「起動した」の中で待っている間の見張りは飛ばす。重なると「終わった」が先に流れ、順番が入れ替わる。待ちが解けた次の見張りで「終わった」を流す。
    [Fact]
    public void PollsDoNotOverlapWhileAHandlerIsStillRunning()
    {
        using var rig = new Rig(running: false);
        rig.Presence.CheckOnce();
        using var inside = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        rig.Listen(new On(started: () =>
        {
            inside.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        }));

        rig.Running = true;
        var first = Task.Run(rig.Presence.Poll);
        Assert.True(inside.Wait(TimeSpan.FromSeconds(10)));

        rig.Running = false;
        rig.Presence.Poll();
        Assert.Equal(["started"], rig.Events);

        release.Set();
        Assert.True(first.Wait(TimeSpan.FromSeconds(10)));
        rig.Presence.Poll();

        Assert.Equal(["started", "shutdown"], rig.Events);
    }

    [Fact]
    public void AThrowingHandlerDoesNotStopTheOthersOrTheNextEvent()
    {
        using var rig = new Rig(running: true);
        rig.Presence.CheckOnce();

        var hub = VRChatHub();
        var presence = new VRChatPresence(() => rig.Running, hub, NullLogger<VRChatPresence>.Instance);
        presence.CheckOnce();
        List<string> heard = [];
        var module = new FakeModule("p");
        hub.Source.AddListener<IVRChatEventListener>(module, new On(shutdown: () => throw new InvalidOperationException("boom")));
        hub.Source.AddListener<IVRChatEventListener>(module, new On(started: () => heard.Add("started"), shutdown: () => heard.Add("shutdown")));

        rig.Running = false;
        presence.Poll();
        rig.Running = true;
        presence.Poll();
        presence.Dispose();

        Assert.Equal(["shutdown", "started"], heard);
    }

    // 流している最中の出来事が済むまで待つ（その後でモジュールを畳むので、畳んだ後に届かない）。
    [Fact]
    public void DisposingWaitsForAnEventThatIsStillBeingRaised()
    {
        var rig = new Rig(running: false);
        rig.Presence.CheckOnce();
        using var inside = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        rig.Listen(new On(started: () =>
        {
            inside.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        }));

        rig.Running = true;
        var poll = Task.Run(rig.Presence.Poll);
        Assert.True(inside.Wait(TimeSpan.FromSeconds(10)));

        var disposing = Task.Run(rig.Presence.Dispose);
        Assert.False(disposing.Wait(TimeSpan.FromMilliseconds(300)), "Dispose returned while the event was still being raised");

        release.Set();
        Assert.True(disposing.Wait(TimeSpan.FromSeconds(10)));
        Assert.True(poll.Wait(TimeSpan.FromSeconds(10)));
    }

    // モジュールを畳む前に止める（畳んだモジュールへ届かない）。
    [Fact]
    public void NothingIsRaisedAfterDisposing()
    {
        var rig = new Rig(running: true);
        rig.Presence.CheckOnce();

        rig.Presence.Dispose();
        rig.Running = false;
        rig.Presence.Poll();

        Assert.Empty(rig.Events);
    }

    [Fact]
    public void TheWatchRunsOnATimerAfterBegin()
    {
        using var rig = new Rig(running: false);
        using var started = new ManualResetEventSlim();
        rig.Listen(new On(started: started.Set));

        rig.Presence.Begin(() => { }, TimeSpan.FromMilliseconds(10));
        rig.Running = true;

        Assert.True(started.Wait(TimeSpan.FromSeconds(10)), "the watch did not notice VRChat starting");
    }

    private sealed class Listener(InstanceTracker instance, int priority = 0) : IVRChatEventListener
    {
        public int ListenerPriority => priority;

        public List<string> Heard { get; } = [];

        public void OnVRChatStarted(VRChatStartedEventParams e) => Heard.Add("started");

        public void OnVRChatShutdown(VRChatShutdownEventParams e) => Heard.Add(instance.World.State == StatusState.Off ? "shutdown after the chips" : "shutdown before the chips");
    }

    /// <summary>owner はどれでも通す（モジュールを読み込まずに作るので）。</summary>
    private static EventHub VRChatHub() =>
        new EventHubs(_ => true, NullLoggerFactory.Instance).CreateHub(HostOwner.Instance, [typeof(IVRChatEventListener)]);

    // モジュールの受け手を本体より先に足す（本番とは逆の順）。それでも本体が先に聞くことを、どの試験も通りがけに見る（本体の受け手は配る順がいちばん前）。
    private static (Rig Rig, InstanceTracker Instance, FakeVrcLogBus Bus, LogReadingStatus Reading, FakeNotifications Notify, Listener Listener) Connected()
    {
        var rig = new Rig(running: true);
        rig.Presence.CheckOnce();

        var bus = new FakeVrcLogBus();
        var instance = new InstanceTracker(bus, new FakeVrcLogStore(), new FakeLocalizer(), NullLogger<InstanceTracker>.Instance);
        var reading = new LogReadingStatus(() => @"C:\logs\output_log_2026-11-12_12-00-00.txt", new FakeLocalizer(), () => rig.Presence.IsRunning);
        var notify = new FakeNotifications();
        var listener = new Listener(instance);
        rig.Listen(listener);

        VRChatPresence.Connect(rig.Hub.Source, instance, reading, notify, new FakeLocalizer());
        return (rig, instance, bus, reading, notify, listener);
    }

    private static WorldJoined World() => new()
    {
        Line = new VrcLogLine { Timestamp = DateTimeOffset.UnixEpoch, Level = VrcLogLevel.Log, Message = "x", SourceFile = "output_log.txt" },
        WorldId = "wrld_a",
        InstanceName = "1",
        WorldName = "Cozy",
        AccessType = "public",
        RawInstance = "1",
    };

    [Fact]
    public void WhenVRChatClosesTheChipsAndTitleChangeThenItIsAnnouncedAndModulesHear()
    {
        var (rig, instance, bus, reading, notify, listener) = Connected();
        using var _ = rig;
        bus.Emit(World());
        Assert.Equal("Cozy", instance.World.Text);
        Assert.Equal("host.status.log.reading", reading.Status.Text);

        rig.Running = false;
        rig.Presence.Poll();

        Assert.Equal(StatusState.Off, instance.World.State);
        Assert.Equal("", instance.World.Text);
        Assert.Equal("host.status.vrchat.off", reading.Status.Text);
        Assert.Equal(StatusState.Off, reading.Status.State);
        Assert.Equal([("host.vrchat.stopped", (string?)null)], notify.Toasts);
        Assert.Equal([VRChatPresence.NoteKind], notify.Kinds);
        Assert.Equal(["shutdown after the chips"], listener.Heard);
    }

    [Fact]
    public void WhenVRChatStartsTheTitleComesBackThenItIsAnnouncedAndModulesHear()
    {
        var (rig, _, _, reading, notify, listener) = Connected();
        using var _2 = rig;

        rig.Running = false;
        rig.Presence.Poll();
        rig.Running = true;
        rig.Presence.Poll();

        Assert.Equal("host.status.log.reading", reading.Status.Text);
        Assert.Equal(StatusState.Ok, reading.Status.State);
        Assert.Equal("host.vrchat.started", notify.Toasts[^1].Title);
        Assert.Equal(VRChatPresence.NoteKind, notify.Kinds[^1]);
        Assert.Equal("started", listener.Heard[^1]);
    }

    // 本番の順は、本体を足してから、モジュールを読み込む。
    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void AModuleThatAsksToGoFirstStillHearsAfterTheHost(int priority)
    {
        using var rig = new Rig(running: true);
        rig.Presence.CheckOnce();
        var bus = new FakeVrcLogBus();
        var instance = new InstanceTracker(bus, new FakeVrcLogStore(), new FakeLocalizer(), NullLogger<InstanceTracker>.Instance);
        var reading = new LogReadingStatus(() => "output_log.txt", new FakeLocalizer(), () => rig.Presence.IsRunning);
        VRChatPresence.Connect(rig.Hub.Source, instance, reading, new FakeNotifications(), new FakeLocalizer());
        var eager = new Listener(instance, priority);
        rig.Listen(eager);
        bus.Emit(World());

        rig.Running = false;
        rig.Presence.Poll();

        Assert.Equal(["shutdown after the chips"], eager.Heard);
    }

    // 登録したとき・読み込んだときには「起動した」を流さない。見張りが同じ答えを返し続ける間も届かない。
    /// </summary>
    [Fact]
    public void AListenerRegisteredWhileVRChatRunsHearsNothingUntilItCloses()
    {
        var (rig, _, _, _, _, listener) = Connected();
        using var _2 = rig;

        rig.Presence.Begin(() => { }, Never);
        rig.Presence.Poll();
        rig.Presence.Poll();
        Assert.Empty(listener.Heard);

        rig.Running = false;
        rig.Presence.Poll();
        Assert.Equal(["shutdown after the chips"], listener.Heard);
    }

    private sealed class ThrowingNotifications : INotificationManager
    {
        public Task<bool> PushModalAsync(NotifyRequest request, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public IToast PushToast(NotifyKind kind, string title, string? body = null, params NotifyAction[] actions) =>
            throw new InvalidOperationException("the frame is no longer valid");

        public void PushBanner(NotifyKind kind, string title, string? body = null, params NotifyAction[] actions) { }
    }

    // 状態はもう変わっていて、次の見張りでは流し直さないので、本体の側が投げてもモジュールへ流す（ここで落とすと二度と届かない）。
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ModulesHearEvenWhenTheHostSideThrows(bool closing)
    {
        using var rig = new Rig(running: closing);
        rig.Presence.CheckOnce();
        var instance = new InstanceTracker(new FakeVrcLogBus(), new FakeVrcLogStore(), new FakeLocalizer(), NullLogger<InstanceTracker>.Instance);
        var reading = new LogReadingStatus(() => null, new FakeLocalizer(), () => rig.Presence.IsRunning);
        VRChatPresence.Connect(rig.Hub.Source, instance, reading, new ThrowingNotifications(), new FakeLocalizer());
        var listener = new Listener(instance);
        rig.Listen(listener);

        rig.Running = !closing;
        rig.Presence.Poll();

        Assert.Equal([closing ? "shutdown after the chips" : "started"], listener.Heard);
    }

    // 見張りは最大 2 秒遅れる。ログの中身が正。
    [Fact]
    public void ALineAfterTheShutdownIsStillCounted()
    {
        var (rig, instance, bus, _, _, _) = Connected();
        using var _2 = rig;

        rig.Running = false;
        rig.Presence.Poll();
        bus.Emit(World());

        Assert.Equal("Cozy", instance.World.Text);
    }

    [Fact]
    public void TheAnnouncementCanBeSwitchedOffOnTheWrist()
    {
        var candidates = new HostConfig().SuggestValuesFor(nameof(HostConfig.HiddenWristNotes));

        Assert.NotNull(candidates);
        Assert.Contains(VRChatPresence.NoteKind.Id, candidates);
    }
}
