using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.Events.Config;
using SpoitableToolKit.Abstractions.Events.VRChat;
using SpoitableToolKit.Abstractions.Events.Video;
using SpoitableToolKit.Abstractions.Events.VrcLog;
using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.Config;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.ExternalModules;
using SpoitableToolKit.Host.Modules;
using SpoitableToolKit.Host.VrcLog;
using SpoitableToolKit.Tests.Fakes;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Events;

/// <summary>モジュールに渡す出来事の口と、owner の名簿（入れた・畳んだ）。owner はそのモジュール自身だけ（参照で照合する。ほかのモジュールを名乗れない）。</summary>
public sealed class HostEventsTests
{
    private readonly Logs _logs = new();

    private HostEvents Events() => new(_logs);

    private static readonly VideoPlaybackInfo Video = new() { Url = "https://example.invalid/v", AttemptCount = 1, Timestamp = DateTimeOffset.UnixEpoch };

    private sealed class Ear(string name, List<string> heard) : IVRChatEventListener, IVideoEventListener, IVrcLogEventListener
    {
        public void OnVRChatStarted(VRChatStartedEventParams e) => heard.Add($"{name}:started");

        public void OnVRChatShutdown(VRChatShutdownEventParams e) => heard.Add($"{name}:shutdown");

        public void OnVideoPlaybackFailed(VideoPlaybackFailedEventParams e) => heard.Add($"{name}:failed {e.Video.Url} {e.IsRepeatedFail}");

        public void OnLogLine(VrcLogLine line) => heard.Add($"{name}:line {line.Message}");

        public void OnLogEvent(VrcLogEventParams e) => heard.Add($"{name}:event {e.GetType().Name}");
    }

    private sealed class ThrowsOnLines : IVrcLogEventListener
    {
        public void OnLogLine(VrcLogLine line) => throw new InvalidOperationException("boom");
    }

    private static readonly VrcLogLine Joined = new()
    {
        Timestamp = DateTimeOffset.UnixEpoch, Level = VrcLogLevel.Log, Message = "[Behaviour] Joining wrld_x:1", SourceFile = "output_log.txt",
    };

    private static readonly VrcLogLine Plain = Joined with { Message = "[VRCTrackingManager] nothing to see" };

    private static ParsedEntry JoinedEntry() =>
        new(Joined, new WorldJoined { Line = Joined, WorldId = "wrld_x", InstanceName = "1", RawInstance = "wrld_x:1" });

    private sealed class Exploding : IVideoEventListener
    {
        public void OnVideoPlaybackFailed(VideoPlaybackFailedEventParams e) => throw new InvalidOperationException("boom");
    }

    private interface IHostPingListener : IEventListener
    {
        void OnPing() { }
    }

    private static void Fail(HostEvents events) =>
        events.Videos.Fire<IVideoEventListener>(l => l.OnVideoPlaybackFailed(new VideoPlaybackFailedEventParams { Video = Video, IsRepeatedFail = false }));

    /// <summary>VRChat の起動・終了は、足した全員に届く。足すのは、そのモジュールの <c>host.VRChat</c> から。</summary>
    [Fact]
    public void EveryModuleThatAddedAVRChatListenerHearsStartsAndShutdowns()
    {
        var events = Events();
        var a = new FakeModule("a");
        var b = new FakeModule("b");
        events.Admit(a);
        events.Admit(b);
        List<string> heard = [];

        events.For(a).VRChat.AddListener<IVRChatEventListener>(a, new Ear("a", heard));
        events.For(b).VRChat.AddListener<IVRChatEventListener>(b, new Ear("b", heard));
        events.VRChat.Fire<IVRChatEventListener>(l => l.OnVRChatStarted(new VRChatStartedEventParams { NoticedAt = DateTimeOffset.UnixEpoch }));
        events.VRChat.Fire<IVRChatEventListener>(l => l.OnVRChatShutdown(new VRChatShutdownEventParams { NoticedAt = DateTimeOffset.UnixEpoch }));

        Assert.Equal(["a:started", "b:started", "a:shutdown", "b:shutdown"], heard);
    }

    [Fact]
    public void AVideoListenerHearsTheFailureWithItsDetails()
    {
        var events = Events();
        var a = new FakeModule("a");
        events.Admit(a);
        List<string> heard = [];

        events.For(a).Videos.AddListener<IVideoEventListener>(a, new Ear("a", heard));
        Fail(events);

        Assert.Equal(["a:failed https://example.invalid/v False"], heard);
    }

    /// <summary>ほかのモジュールを owner として渡せない（参照で照合する。Id が同じだけの別物も通さない）。どちらの Id も文に出す（どの口から誰を名乗ったか分かるように）。</summary>
    [Fact]
    public void AModuleCannotClaimAnothersIdentityThroughItsEntryPoints()
    {
        var events = Events();
        var mine = new FakeModule("mine");
        var other = new FakeModule("other");
        var sameId = new FakeModule("mine");
        events.Admit(mine);
        events.Admit(other);
        var host = events.For(mine);

        var vrchat = Assert.Throws<InvalidOperationException>(() => host.VRChat.AddListener<IVRChatEventListener>(other, new Ear("x", [])));
        var videos = Assert.Throws<InvalidOperationException>(() => host.Videos.RemoveListener<IVideoEventListener>(other, new Ear("x", [])));
        var hubs = Assert.Throws<InvalidOperationException>(() => host.Hubs.Create<IHostPingListener>(sameId));
        var log = Assert.Throws<InvalidOperationException>(
            () => host.LogOf(new FakeVrcLogBus(events.VrcLog)).AddListener<IVrcLogEventListener>(other, new Ear("x", [])));

        Assert.Equal("Tried to use 'other' as the owner through host.VRChat of 'mine'. Pass the module itself (this) as the owner.", vrchat.Message);
        Assert.Contains("through host.Videos of 'mine'", videos.Message);
        Assert.Contains("'mine' as the owner through host.Events of 'mine'", hubs.Message);
        Assert.Contains("'other' as the owner through host.VrcLog of 'mine'", log.Message);
        Assert.Equal(3, events.Hubs.HubCount);
    }

    [Fact]
    public void AModuleThatWasNotAdmittedCannotAdd()
    {
        var events = Events();
        var stranger = new FakeModule("stranger");

        var refused = Assert.Throws<InvalidOperationException>(
            () => events.For(stranger).Videos.AddListener<IVideoEventListener>(stranger, new Ear("x", [])));

        Assert.Contains($"'stranger' ({typeof(FakeModule).FullName}) is not a loaded module", refused.Message);
    }

    /// <summary>畳んだら（<see cref="HostEvents.Retire"/>）、足した listener は全部の口から外れ、作った口は閉じ、もう足せない。入れていないものを畳んでもよい（読み込みの途中で落ちたとき）。</summary>
    [Fact]
    public void RetiringAModuleTakesOffItsListenersAndClosesItsHubs()
    {
        var events = Events();
        var a = new FakeModule("a");
        var b = new FakeModule("b");
        events.Admit(a);
        events.Admit(b);
        List<string> heard = [];
        events.For(a).Videos.AddListener<IVideoEventListener>(a, new Ear("a", heard));
        events.For(b).Videos.AddListener<IVideoEventListener>(b, new Ear("b", heard));
        events.For(a).Hubs.Create<IHostPingListener>(a);

        Assert.Equal(2, events.AdmittedCount);
        events.Retire(a);
        events.Retire(new FakeModule("never admitted"));
        Fail(events);

        Assert.Equal(["b:failed https://example.invalid/v False"], heard);
        Assert.Equal(3, events.Hubs.HubCount);
        Assert.Equal(1, events.AdmittedCount);
        Assert.Throws<InvalidOperationException>(() => events.For(a).Videos.AddListener<IVideoEventListener>(a, new Ear("late", heard)));
    }

    /// <summary>落ちた listener は、持ち主の Id・listener の型・出来事の口の型を付けて記録し、残りへ配る。どのメソッドで落ちたかは例外の stack に出る（文には入れない。配る口は 1 つの出来事に 1 つのメソッドとは限らないので）。</summary>
    [Fact]
    public void AFailingListenerIsLoggedWithItsOwnerAndTheOthersStillHear()
    {
        var events = Events();
        var broken = new FakeModule("hrs");
        var fine = new FakeModule("fine");
        events.Admit(broken);
        events.Admit(fine);
        List<string> heard = [];
        events.For(broken).Videos.AddListener<IVideoEventListener>(broken, new Exploding());
        events.For(fine).Videos.AddListener<IVideoEventListener>(fine, new Ear("fine", heard));

        Fail(events);

        Assert.Equal(["fine:failed https://example.invalid/v False"], heard);
        var warning = Assert.Single(_logs.Warnings);
        Assert.Contains($"{typeof(Exploding).FullName} of hrs threw while host was giving out {nameof(IVideoEventListener)}", warning);
    }

    /// <summary>取り違えると、足しても届かない。どの口も owner を照合する包みを通っている（包む前の口を渡すと、ほかのモジュールを名乗れて、作った口もアプリの名簿に載らない）。</summary>
    [Fact]
    public void TheModulesHostHandsOutTheRightEntryPoints()
    {
        var events = Events();
        var module = new FakeModule("p");
        var other = new FakeModule("q");
        events.Admit(module);
        events.Admit(other);
        List<string> heard = [];
        var config = ConfigChannel.Create(events.Hubs, typeof(PingConfig));
        IModuleHost host = new ModuleHost(new EmptyServices(), Path.GetTempPath(), new FakeVrcLogBus(events.VrcLog), new FakeVrcLogStore(),
            new FakeExternalToolProvider(), new FakeNotifications(), new FakeClipboard(), new FakeOscBus(),
            config.Source, events.For(module), new FakeModuleHost().Ui, new FakeModuleHost().Shortcuts,
            new ExternalModuleRegistry(_logs), new FakeLocalizer(), NullLogger.Instance, () => false);
        var ear = new Ear("p", heard);

        host.VRChat.AddListener<IVRChatEventListener>(module, ear);
        host.Videos.AddListener<IVideoEventListener>(module, ear);
        host.VrcLog.AddListener<IVrcLogEventListener>(module, ear);
        host.Config.AddListener<IConfigEventListener<PingConfig>>(module, new PingConfigEar());
        var own = host.Events.Create<IHostPingListener>(module);
        events.VRChat.Fire<IVRChatEventListener>(l => l.OnVRChatStarted(new VRChatStartedEventParams { NoticedAt = DateTimeOffset.UnixEpoch }));
        Fail(events);
        events.VrcLog.Fire<IVrcLogEventListener>(l => l.OnLogLine(Plain));

        Assert.Equal(["p:started", "p:failed https://example.invalid/v False", $"p:line {Plain.Message}"], heard);
        Assert.Same(module, ((EventHub)own).Owner);
        Assert.Equal(5, events.Hubs.HubCount);
        Assert.Equal(1, config.ListenerCount);

        // 断るのは包み（owner の照合）。足していない新しい listener で確かめる — 足し済みの ear だと、
        // 「同じものをほかの owner が足した」で口の側も断るので、包みが無くても通ってしまう。
        Assert.Contains("'q' as the owner through host.VRChat of 'p'",
            Assert.Throws<InvalidOperationException>(() => host.VRChat.AddListener<IVRChatEventListener>(other, new Ear("q", []))).Message);
        Assert.Contains("'q' as the owner through host.Videos of 'p'",
            Assert.Throws<InvalidOperationException>(() => host.Videos.AddListener<IVideoEventListener>(other, new Ear("q", []))).Message);
        Assert.Contains("'q' as the owner through host.VrcLog of 'p'",
            Assert.Throws<InvalidOperationException>(() => host.VrcLog.AddListener<IVrcLogEventListener>(other, new Ear("q", []))).Message);
        Assert.Contains("'q' as the owner through host.Events of 'p'",
            Assert.Throws<InvalidOperationException>(() => host.Events.Create<IHostPingListener>(other)).Message);
        Assert.Contains("'q' as the owner through host.Config of 'p'",
            Assert.Throws<InvalidOperationException>(() => host.Config.AddListener<IConfigEventListener<PingConfig>>(other, new PingConfigEar())).Message);
    }

    private sealed class PingConfig : IModuleConfig
    {
        public int Seconds = 1;
    }

    private sealed class PingConfigEar : IConfigEventListener<PingConfig>;

    /// <summary>設定を持たないモジュールの <c>host.Config</c> は、足そうとしたら設定が無いと言って断る（null にしない）。</summary>
    [Fact]
    public void AModuleWithoutSettingsIsToldItHasNone()
    {
        var refused = Assert.Throws<InvalidOperationException>(
            () => ConfigChannel.None("plain").AddListener<IConfigEventListener<PingConfig>>(new FakeModule("plain"), new PingConfigEar()));

        Assert.Contains("'plain' has no settings (ISpoitableModule.Config is null)", refused.Message);
        Assert.Contains("IConfigEventListener<PingConfig>", refused.Message);
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>Host が配る口は、アプリ自身（<see cref="HostOwner"/>）の口。モジュールからは配れない（受ける側には Source だけを見せる）。</summary>
    [Fact]
    public void TheHostsHubsBelongToTheAppAndModulesOnlySeeTheirSources()
    {
        var events = Events();
        var a = new FakeModule("a");
        events.Admit(a);

        Assert.Same(HostOwner.Instance, events.VRChat.Owner);
        Assert.Same(HostOwner.Instance, events.Videos.Owner);
        Assert.Same(HostOwner.Instance, events.VrcLog.Owner);
        Assert.False(events.For(a).VRChat is IEventHub);
        Assert.False(events.For(a).Videos is IEventHub);
        Assert.False(events.For(a).LogOf(new FakeVrcLogBus(events.VrcLog)) is IEventHub);
    }

    /// <summary>ログの 1 行は、全部の受け手へ行 → 全部の受け手へ出来事、の順に届く（受け手ごとに行と出来事を続けて呼ぶのではない）。意味の分からない行は行だけ。</summary>
    [Fact]
    public void ALogLineReachesEveryoneBeforeItsEventDoes()
    {
        var events = Events();
        var module = new FakeModule("p");
        events.Admit(module);
        List<string> heard = [];
        var bus = new FakeVrcLogBus(events.VrcLog);
        bus.AddListener<IVrcLogEventListener>(HostOwner.Instance, new Ear("host", heard));
        events.For(module).LogOf(bus).AddListener<IVrcLogEventListener>(module, new Ear("p", heard));

        VrcLogBus.Publish(events.VrcLog, JoinedEntry());
        VrcLogBus.Publish(events.VrcLog, new ParsedEntry(Plain, null));
        VrcLogBus.Publish(events.VrcLog, null);

        Assert.Equal(
            [$"host:line {Joined.Message}", $"p:line {Joined.Message}", "host:event WorldJoined", "p:event WorldJoined",
             $"host:line {Plain.Message}", $"p:line {Plain.Message}"],
            heard);
    }

    /// <summary>ログの受け手が投げても、ほかの受け手には同じ行も出来事も届く。落ちたのは持ち主の Id を付けて記録する。</summary>
    [Fact]
    public void AListenerThatThrowsOnEveryLineDoesNotStopTheOthers()
    {
        var events = Events();
        var broken = new FakeModule("broken");
        var fine = new FakeModule("fine");
        events.Admit(broken);
        events.Admit(fine);
        List<string> heard = [];
        var bus = new FakeVrcLogBus(events.VrcLog);
        events.For(broken).LogOf(bus).AddListener<IVrcLogEventListener>(broken, new ThrowsOnLines());
        events.For(fine).LogOf(bus).AddListener<IVrcLogEventListener>(fine, new Ear("fine", heard));

        VrcLogBus.Publish(events.VrcLog, JoinedEntry());

        Assert.Equal([$"fine:line {Joined.Message}", "fine:event WorldJoined"], heard);
        Assert.Contains($"{typeof(ThrowsOnLines).FullName} of broken threw while host was giving out {nameof(IVrcLogEventListener)}", Assert.Single(_logs.Warnings));
    }

    [Fact]
    public void TheModulesLogSourceShowsTheFileBeingRead()
    {
        var events = Events();
        var module = new FakeModule("p");
        events.Admit(module);

        var log = events.For(module).LogOf(new ReadingBus("C:/logs/output_log_1.txt"));

        Assert.Equal("C:/logs/output_log_1.txt", log.CurrentLogFile);
    }

    private sealed class ReadingBus(string file) : IVrcLogBus
    {
        public string? CurrentLogFile => file;

        public void AddListener<TListener>(ISpoitableModule owner, IEventListener listener) where TListener : class, IEventListener { }

        public void RemoveListener<TListener>(ISpoitableModule owner, IEventListener listener) where TListener : class, IEventListener { }
    }
}
