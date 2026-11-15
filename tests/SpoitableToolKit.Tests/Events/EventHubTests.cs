using System.Reflection;
using System.Reflection.Emit;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Tests.Fakes;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Events;

public sealed class EventHubTests
{
    private readonly Logs _logs = new();
    private readonly HashSet<ISpoitableModule> _loaded = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> _heard = [];

    private EventHubs Hubs() => new(p =>
    {
        lock (_loaded) return _loaded.Contains(p);
    }, _logs);

    private FakeModule Module(string id)
    {
        var module = new FakeModule(id);
        lock (_loaded) _loaded.Add(module);
        return module;
    }

    private void Unload(ISpoitableModule module)
    {
        lock (_loaded) _loaded.Remove(module);
    }

    private sealed class Ear(List<string> heard, string name, int priority = 0)
        : IHubPingListener, IHubPongListener, IHubAskListener, IHubCountListener
    {
        public int ListenerPriority { get; set; } = priority;

        public Func<EventResult?>? Answer { get; set; }

        public Func<string?>? Counted { get; set; }

        public Action? WhenPinged { get; set; }

        public void OnPing(HubPing e)
        {
            lock (heard) heard.Add($"{name}:{e.N}");
            WhenPinged?.Invoke();
        }

        public void OnPong(HubPing e)
        {
            lock (heard) heard.Add($"{name}:pong");
        }

        public EventResult OnAsk(HubPing e)
        {
            lock (heard) heard.Add($"{name}:ask");
            return Answer is null ? EventResult.Continue : Answer()!;
        }

        public string? Count(HubPing e) => Counted?.Invoke();
    }

    /// <summary>ping しか実装していない listener。</summary>
    private sealed class PingOnly : IHubPingListener;

    private sealed class Thing<T>(List<string> heard) : IHubThingListener<T>
    {
        public void OnThing(T value)
        {
            lock (heard) heard.Add($"thing:{value}");
        }
    }

    [Fact]
    public void ListenersAreCalledFromTheHighestPriorityThenInTheOrderTheyWereAdded()
    {
        var owner = Module("a");
        var hub = Hubs().Create<IHubPingListener>(owner);

        hub.Source.AddListener<IHubPingListener>(owner, new Ear(_heard, "first"));
        hub.Source.AddListener<IHubPingListener>(owner, new Ear(_heard, "high", priority: 10));
        hub.Source.AddListener<IHubPingListener>(owner, new Ear(_heard, "second"));
        hub.Source.AddListener<IHubPingListener>(owner, new Ear(_heard, "low", priority: -5));

        hub.Fire<IHubPingListener>(l => l.OnPing(new HubPing(1)));

        Assert.Equal(["high:1", "first:1", "second:1", "low:1"], _heard);
    }

    /// <summary>優先度を読むのは足したときの 1 回だけ。配るたびに読むと、途中で変えた listener が並びを崩す。</summary>
    [Fact]
    public void APriorityIsReadOnceWhenTheListenerIsAdded()
    {
        var owner = Module("a");
        var hub = Hubs().Create<IHubPingListener>(owner);
        var climber = new Ear(_heard, "climber");
        hub.Source.AddListener<IHubPingListener>(owner, new Ear(_heard, "top", priority: 5));
        hub.Source.AddListener<IHubPingListener>(owner, climber);

        climber.ListenerPriority = 100;
        hub.Fire<IHubPingListener>(l => l.OnPing(new HubPing(1)));

        Assert.Equal(["top:1", "climber:1"], _heard);
    }

    private readonly struct StructEar : IHubPingListener;

    /// <summary>struct の listener は、渡すたびに別の箱になる（2 回足すと 2 回届き、外せない）ので、足す・外すの時点で断る。</summary>
    [Fact]
    public void AStructListenerIsRefused()
    {
        var owner = Module("a");
        var hub = Hubs().Create<IHubPingListener>(owner);

        var adding = Assert.Throws<ArgumentException>(() => hub.Source.AddListener<IHubPingListener>(owner, new StructEar()));
        var removing = Assert.Throws<ArgumentException>(() => hub.Source.RemoveListener<IHubPingListener>(owner, new StructEar()));

        Assert.Contains($"{typeof(StructEar).FullName} is a struct. Pass an instance of a class", adding.Message);
        Assert.Contains("is a struct", removing.Message);
        Assert.Equal(0, ((EventHub)hub).ListenerCount);
    }

    /// <summary>
    /// 畳んだ owner は、まだ「読み込まれている」と答えている間でも（Host が畳む印を付けてから一覧を直すまでの間）、足すことも作ることもできない。
    /// </summary>
    [Fact]
    public void AForgottenOwnerCannotAddOrCreateEvenBeforeItIsUnloaded()
    {
        var hubs = Hubs();
        var host = hubs.Create<IHubPingListener>(HostOwner.Instance);
        var module = Module("p");

        hubs.Forget(module);
        var adding = Assert.Throws<InvalidOperationException>(() => host.Source.AddListener<IHubPingListener>(module, new Ear(_heard, "late")));
        var creating = Assert.Throws<InvalidOperationException>(() => hubs.Create<IHubPingListener>(module));

        Assert.Contains($"'p' ({typeof(FakeModule).FullName}) is not a loaded module", adding.Message);
        Assert.Contains("is not a loaded module", creating.Message);
        Assert.Equal(0, hubs.ListenerCount);
        Assert.Equal(1, hubs.HubCount);
    }

    private sealed class SlowPriority(ManualResetEventSlim reading, ManualResetEventSlim release) : IHubPingListener
    {
        public int ListenerPriority
        {
            get
            {
                reading.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                return 0;
            }
        }
    }

    /// <summary>
    /// 足している途中（owner を確かめたあと、入れる前。優先度を読む間はモジュールのコードが走る）に、別の thread がその owner を畳んだら、入れない。
    /// 入れると、外す相手のいないまま、畳んだモジュールの listener が呼ばれ続ける。
    /// </summary>
    [Fact]
    public async Task AnAddThatOverlapsTheOwnerBeingForgottenLeavesNothingBehind()
    {
        var hubs = Hubs();
        var host = hubs.Create<IHubPingListener>(HostOwner.Instance);
        var module = Module("p");
        using var reading = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var adding = Task.Run(() => host.Source.AddListener<IHubPingListener>(module, new SlowPriority(reading, release)));
        Assert.True(reading.Wait(TimeSpan.FromSeconds(10)), "優先度を読みに来なかった");
        hubs.Forget(module);
        release.Set();

        await Assert.ThrowsAsync<InvalidOperationException>(() => adding);
        Assert.Equal(0, hubs.ListenerCount);
    }

    [Fact]
    public void CancellableAndCollectingRoundsAlsoSkipWhatWasRemovedWhileFiring()
    {
        var owner = Module("a");
        var asking = Hubs().Create<IHubAskListener, IHubCountListener>(owner);
        var laterAsk = new Ear(_heard, "later");
        var laterCount = new Ear(_heard, "later") { Counted = () => "later" };
        asking.Source.AddListener<IHubAskListener>(owner, new Ear(_heard, "remover")
        {
            Answer = () =>
            {
                asking.Source.RemoveListener<IHubAskListener>(owner, laterAsk);
                return EventResult.Continue;
            },
        });
        asking.Source.AddListener<IHubAskListener>(owner, laterAsk);
        asking.Source.AddListener<IHubCountListener>(owner, new Ear(_heard, "remover")
        {
            Counted = () =>
            {
                asking.Source.RemoveListener<IHubCountListener>(owner, laterCount);
                return "remover";
            },
        });
        asking.Source.AddListener<IHubCountListener>(owner, laterCount);

        asking.FireCancellable<IHubAskListener>(l => l.OnAsk(new HubPing(1)));
        var counted = asking.FireCollect<IHubCountListener, string?>(l => l.Count(new HubPing(1)));

        Assert.Equal(["remover:ask"], _heard);
        Assert.Equal(["remover"], counted);
    }

    private sealed class BadPriority(List<string> heard) : IHubPingListener
    {
        public int ListenerPriority => throw new InvalidOperationException("boom");

        public void OnPing(HubPing e)
        {
            lock (heard) heard.Add("bad");
        }
    }

    /// <summary>優先度を読むと投げる listener も、0 として足す（足すのを断ると、壊れた property 1 つで出来事が届かなくなる）。</summary>
    [Fact]
    public void APriorityThatThrowsIsTakenAsZero()
    {
        var owner = Module("a");
        var hub = Hubs().Create<IHubPingListener>(owner);
        hub.Source.AddListener<IHubPingListener>(owner, new Ear(_heard, "below", priority: -1));
        hub.Source.AddListener<IHubPingListener>(owner, new BadPriority(_heard));
        hub.Source.AddListener<IHubPingListener>(owner, new Ear(_heard, "above", priority: 1));

        hub.Fire<IHubPingListener>(l => l.OnPing(new HubPing(1)));

        Assert.Equal(["above:1", "bad", "below:1"], _heard);
        Assert.Contains(_logs.Warnings, line => line.Contains($"ListenerPriority of {typeof(BadPriority).FullName} (a) threw; taken as 0"));
    }

    [Fact]
    public void TheAppCannotCreateAHubForATypeThatIsNotAListener()
    {
        var hubs = Hubs();

        var refused = Assert.Throws<ArgumentException>(() => hubs.CreateHub(HostOwner.Instance, [typeof(IDisposable)]));

        Assert.Contains("System.IDisposable is not a listener interface", refused.Message);
        Assert.Equal(0, hubs.HubCount);
    }

    /// <summary>1 つの instance が 2 つの listener の interface を実装していても、届くのは足した型の分だけ（必要のないものを listen させない）。</summary>
    [Fact]
    public void OnlyTheTypeAListenerWasAddedAsReachesIt()
    {
        var owner = Module("a");
        var hub = Hubs().Create<IHubPingListener, IHubPongListener>(owner);
        hub.Source.AddListener<IHubPingListener>(owner, new Ear(_heard, "ear"));

        hub.Fire<IHubPongListener>(l => l.OnPong(new HubPing(1)));
        hub.Fire<IHubPingListener>(l => l.OnPing(new HubPing(2)));

        Assert.Equal(["ear:2"], _heard);
    }

    [Fact]
    public void ATypeTheHubDoesNotGiveOutIsRefusedWhenAddingAndWhenFiring()
    {
        var owner = Module("a");
        var hub = Hubs().Create<IHubPingListener>(owner);

        var adding = Assert.Throws<ArgumentException>(() => hub.Source.AddListener<IHubPongListener>(owner, new Ear(_heard, "ear")));
        var firing = Assert.Throws<ArgumentException>(() => hub.Fire<IHubPongListener>(l => l.OnPong(new HubPing(1))));

        Assert.Contains("the events of 'a' give out IHubPingListener, not IHubPongListener", adding.Message);
        Assert.Contains("the events of 'a' give out IHubPingListener, not IHubPongListener", firing.Message);
    }

    [Fact]
    public void AListenerThatDoesNotImplementTheTypeIsRefused()
    {
        var owner = Module("a");
        var hub = Hubs().Create<IHubPingListener, IHubPongListener>(owner);

        var refused = Assert.Throws<ArgumentException>(() => hub.Source.AddListener<IHubPongListener>(owner, new PingOnly()));

        Assert.Contains($"{typeof(PingOnly).FullName} does not implement {typeof(IHubPongListener).FullName}", refused.Message);
        Assert.Equal(0, ((EventHub)hub).ListenerCount);
    }

    /// <summary><c>where TListener : class, IEventListener</c> だけでは、クラスと土台そのものが通ってしまう。</summary>
    [Fact]
    public void TheTypeArgumentMustBeTheInterfaceOfTheEvents()
    {
        var owner = Module("a");
        var hubs = Hubs();
        var hub = hubs.Create<IHubPingListener>(owner);
        var ear = new Ear(_heard, "ear");

        var baseCreate = Assert.Throws<ArgumentException>(() => hubs.Create<IEventListener>(owner));
        var classCreate = Assert.Throws<ArgumentException>(() => hubs.Create<Ear>(owner));
        var baseAdd = Assert.Throws<ArgumentException>(() => hub.Source.AddListener<IEventListener>(owner, ear));
        var classAdd = Assert.Throws<ArgumentException>(() => hub.Source.AddListener<Ear>(owner, ear));

        Assert.Contains("not IEventListener itself", baseCreate.Message);
        Assert.Contains("not IEventListener itself", baseAdd.Message);
        Assert.Contains($"{typeof(Ear).FullName} is a class", classCreate.Message);
        Assert.Contains($"{typeof(Ear).FullName} is a class", classAdd.Message);
        Assert.Equal(1, hubs.HubCount);
    }

    [Fact]
    public void TheSameTypeCannotBeWrittenTwiceWhenCreating()
    {
        var hubs = Hubs();

        var refused = Assert.Throws<ArgumentException>(() => hubs.Create<IHubPingListener, IHubPingListener>(Module("a")));

        Assert.Contains("the same listener type is written twice", refused.Message);
        Assert.Equal(0, hubs.HubCount);
    }

    [Fact]
    public void AnInstanceAddedTwiceIsCalledOnce()
    {
        var owner = Module("a");
        var hub = Hubs().Create<IHubPingListener>(owner);
        var ear = new Ear(_heard, "ear");

        hub.Source.AddListener<IHubPingListener>(owner, ear);
        hub.Source.AddListener<IHubPingListener>(owner, ear);
        hub.Fire<IHubPingListener>(l => l.OnPing(new HubPing(1)));

        Assert.Equal(["ear:1"], _heard);
    }

    [Fact]
    public void AnInstanceAddedByOneOwnerCannotBeAddedByAnother()
    {
        var a = Module("a");
        var hub = Hubs().Create<IHubPingListener>(a);
        var ear = new Ear(_heard, "ear");
        hub.Source.AddListener<IHubPingListener>(a, ear);

        var refused = Assert.Throws<InvalidOperationException>(() => hub.Source.AddListener<IHubPingListener>(Module("b"), ear));

        Assert.Contains("has already been added as IHubPingListener by 'a'", refused.Message);
    }

    [Fact]
    public void OnlyTheOwnerThatAddedAListenerCanRemoveIt()
    {
        var a = Module("a");
        var b = Module("b");
        var hub = Hubs().Create<IHubPingListener>(a);
        var ear = new Ear(_heard, "ear");
        hub.Source.AddListener<IHubPingListener>(b, ear);

        var refused = Assert.Throws<InvalidOperationException>(() => hub.Source.RemoveListener<IHubPingListener>(a, ear));
        hub.Fire<IHubPingListener>(l => l.OnPing(new HubPing(1)));

        hub.Source.RemoveListener<IHubPingListener>(b, ear);
        hub.Source.RemoveListener<IHubPingListener>(b, ear);
        hub.Source.RemoveListener<IHubPingListener>(b, new Ear(_heard, "never added"));
        hub.Fire<IHubPingListener>(l => l.OnPing(new HubPing(2)));

        Assert.Contains("added as IHubPingListener by 'b', so only 'b' can remove it", refused.Message);
        Assert.Equal(["ear:1"], _heard);
    }

    /// <summary>listener が投げても残りへ配る。投げたものは、どのモジュールのどの listener かが分かる形で残る。</summary>
    [Fact]
    public void AThrowingListenerDoesNotStopTheOthersAndIsLoggedWithItsOwner()
    {
        var publisher = Module("publisher");
        var hub = Hubs().Create<IHubPingListener>(publisher);
        var breaking = new Ear(_heard, "breaking") { WhenPinged = () => throw new InvalidOperationException("boom") };
        hub.Source.AddListener<IHubPingListener>(Module("broken"), breaking);
        hub.Source.AddListener<IHubPingListener>(publisher, new Ear(_heard, "after"));

        hub.Fire<IHubPingListener>(l => l.OnPing(new HubPing(1)));

        Assert.Equal(["breaking:1", "after:1"], _heard);
        var warning = Assert.Single(_logs.Warnings);
        Assert.Contains($"{typeof(Ear).FullName} of broken threw while publisher was giving out IHubPingListener", warning);
    }

    [Fact]
    public void StopEndsTheRoundAndCarriesItsReason()
    {
        var owner = Module("a");
        var hub = Hubs().Create<IHubAskListener>(owner);
        hub.Source.AddListener<IHubAskListener>(owner, new Ear(_heard, "first"));
        hub.Source.AddListener<IHubAskListener>(Module("vetoer"), new Ear(_heard, "vetoer") { Answer = () => EventResult.Stop("not now") });
        hub.Source.AddListener<IHubAskListener>(owner, new Ear(_heard, "last"));

        var result = hub.FireCancellable<IHubAskListener>(l => l.OnAsk(new HubPing(1)));

        Assert.Equal(EventResultKind.Stop, result.Kind);
        Assert.Equal("not now", result.Reason);
        Assert.Equal(["first:ask", "vetoer:ask"], _heard);
        Assert.Contains(_logs.Infos, line => line.Contains($"{typeof(Ear).FullName} of vetoer stopped IHubAskListener of a: not now"));
    }

    [Fact]
    public void HandledEndsTheRound()
    {
        var owner = Module("a");
        var hub = Hubs().Create<IHubAskListener>(owner);
        hub.Source.AddListener<IHubAskListener>(owner, new Ear(_heard, "taker") { Answer = () => EventResult.Handled });
        hub.Source.AddListener<IHubAskListener>(owner, new Ear(_heard, "last"));

        var result = hub.FireCancellable<IHubAskListener>(l => l.OnAsk(new HubPing(1)));

        Assert.Same(EventResult.Handled, result);
        Assert.Equal(["taker:ask"], _heard);
    }

    [Fact]
    public void AThrowingOrNullAnswerIsTakenAsContinue()
    {
        var owner = Module("a");
        var hub = Hubs().Create<IHubAskListener>(owner);
        hub.Source.AddListener<IHubAskListener>(owner, new Ear(_heard, "thrower") { Answer = () => throw new InvalidOperationException("boom") });
        hub.Source.AddListener<IHubAskListener>(Module("nuller"), new Ear(_heard, "nuller") { Answer = () => null });
        hub.Source.AddListener<IHubAskListener>(owner, new Ear(_heard, "last"));

        var result = hub.FireCancellable<IHubAskListener>(l => l.OnAsk(new HubPing(1)));

        Assert.Same(EventResult.Continue, result);
        Assert.Equal(["thrower:ask", "nuller:ask", "last:ask"], _heard);
        Assert.Contains(_logs.Warnings, line => line.Contains("of nuller answered null to IHubAskListener of a; taken as Continue"));
    }

    /// <summary>理由の無い Stop は作れない。作れるのは 3 つだけ（公開の constructor も、書き換えられる項目も無い）。</summary>
    [Fact]
    public void AStopNeedsAReasonAndNoOtherResultCanBeMade()
    {
        Assert.Throws<ArgumentException>(() => EventResult.Stop(""));
        Assert.Throws<ArgumentException>(() => EventResult.Stop("  "));
        Assert.Throws<ArgumentNullException>(() => EventResult.Stop(null!));

        Assert.Empty(typeof(EventResult).GetConstructors());
        Assert.All(typeof(EventResult).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.Equal(EventResultKind.Continue, EventResult.Continue.Kind);
        Assert.Null(EventResult.Continue.Reason);
        Assert.Equal(EventResultKind.Handled, EventResult.Handled.Kind);
    }

    [Fact]
    public void FireCollectGathersTheAnswersInOrderLeavingOutThrowsAndNulls()
    {
        var owner = Module("a");
        var hub = Hubs().Create<IHubCountListener>(owner);
        hub.Source.AddListener<IHubCountListener>(owner, new Ear(_heard, "one") { Counted = () => "one" });
        hub.Source.AddListener<IHubCountListener>(owner, new Ear(_heard, "thrower") { Counted = () => throw new InvalidOperationException("boom") });
        hub.Source.AddListener<IHubCountListener>(owner, new Ear(_heard, "nothing") { Counted = () => null });
        hub.Source.AddListener<IHubCountListener>(owner, new Ear(_heard, "first", priority: 1) { Counted = () => "first" });

        var answers = hub.FireCollect<IHubCountListener, string?>(l => l.Count(new HubPing(1)));

        Assert.Equal(["first", "one"], answers);
        Assert.Single(_logs.Warnings);
    }

    /// <summary>配っている最中に外したものは、まだ呼んでいなければもう呼ばない（listener が自分を外してもよい）。</summary>
    [Fact]
    public void AListenerRemovedWhileFiringIsNotCalledLaterInThatRound()
    {
        var owner = Module("a");
        var hub = Hubs().Create<IHubPingListener>(owner);
        var later = new Ear(_heard, "later");
        Ear? self = null;
        self = new Ear(_heard, "self")
        {
            WhenPinged = () =>
            {
                hub.Source.RemoveListener<IHubPingListener>(owner, self!);
                hub.Source.RemoveListener<IHubPingListener>(owner, later);
            },
        };
        hub.Source.AddListener<IHubPingListener>(owner, self);
        hub.Source.AddListener<IHubPingListener>(owner, later);

        hub.Fire<IHubPingListener>(l => l.OnPing(new HubPing(1)));
        hub.Fire<IHubPingListener>(l => l.OnPing(new HubPing(2)));

        Assert.Equal(["self:1"], _heard);
    }

    [Fact]
    public void AListenerAddedWhileFiringIsCalledFromTheNextRound()
    {
        var owner = Module("a");
        var hub = Hubs().Create<IHubPingListener>(owner);
        var newcomer = new Ear(_heard, "newcomer");
        hub.Source.AddListener<IHubPingListener>(owner, new Ear(_heard, "adder")
        {
            WhenPinged = () => hub.Source.AddListener<IHubPingListener>(owner, newcomer),
        });

        hub.Fire<IHubPingListener>(l => l.OnPing(new HubPing(1)));
        hub.Fire<IHubPingListener>(l => l.OnPing(new HubPing(2)));

        Assert.Equal(["adder:1", "adder:2", "newcomer:2"], _heard);
    }

    /// <summary>受ける側に見せる口から、配る口は出てこない（よそのモジュールの出来事を配れない。取り違えの防止で、境界ではない）。</summary>
    [Fact]
    public void TheSourceCannotBeTurnedIntoTheHub()
    {
        var hub = Hubs().Create<IHubPingListener>(Module("a"));

        Assert.False(hub.Source is IEventHub);
        Assert.NotSame(hub, hub.Source);
    }

    /// <summary>
    /// owner は読み込まれたモジュールか、アプリ自身（<see cref="HostOwner"/>）だけ。黙って受けると、畳まれたモジュールの listener が外れずに残る。
    /// </summary>
    [Fact]
    public void TheOwnerIsALoadedModuleOrTheAppItself()
    {
        var hubs = Hubs();
        var hub = hubs.Create<IHubPingListener>(HostOwner.Instance);
        var stranger = new FakeModule("stranger");

        hub.Source.AddListener<IHubPingListener>(HostOwner.Instance, new Ear(_heard, "host"));
        var adding = Assert.Throws<InvalidOperationException>(() => hub.Source.AddListener<IHubPingListener>(stranger, new Ear(_heard, "stranger")));
        var creating = Assert.Throws<InvalidOperationException>(() => hubs.Create<IHubPingListener>(stranger));
        hub.Fire<IHubPingListener>(l => l.OnPing(new HubPing(1)));

        Assert.Contains($"'stranger' ({typeof(FakeModule).FullName}) is not a loaded module", adding.Message);
        Assert.Contains("is not a loaded module", creating.Message);
        Assert.Equal(["host:1"], _heard);
        Assert.Equal("host", HostOwner.Instance.Id);
    }

    /// <summary>
    /// 畳まれたモジュール: 足した listener は全部の口から外れ、作った口は閉じる（閉じた口に足しても届かず、記録に残る）。
    /// そのあとは owner として通らない。
    /// </summary>
    [Fact]
    public void ForgettingAnOwnerTakesItsListenersOffEveryHubAndClosesItsHubs()
    {
        var hubs = Hubs();
        var a = Module("a");
        var b = Module("b");
        var ofA = hubs.Create<IHubPingListener>(a);
        var ofHost = hubs.Create<IHubPingListener>(HostOwner.Instance);
        ofA.Source.AddListener<IHubPingListener>(b, new Ear(_heard, "b-on-a"));
        ofHost.Source.AddListener<IHubPingListener>(b, new Ear(_heard, "b-on-host"));
        ofHost.Source.AddListener<IHubPingListener>(a, new Ear(_heard, "a-on-host"));

        hubs.Forget(b);
        Unload(b);
        ofA.Fire<IHubPingListener>(l => l.OnPing(new HubPing(1)));
        ofHost.Fire<IHubPingListener>(l => l.OnPing(new HubPing(1)));
        Assert.Equal(["a-on-host:1"], _heard);
        Assert.Throws<InvalidOperationException>(() => ofHost.Source.AddListener<IHubPingListener>(b, new Ear(_heard, "late")));

        hubs.Forget(a);
        Unload(a);
        ofA.Source.AddListener<IHubPingListener>(HostOwner.Instance, new Ear(_heard, "on-closed"));
        ofA.Fire<IHubPingListener>(l => l.OnPing(new HubPing(2)));
        ofHost.Fire<IHubPingListener>(l => l.OnPing(new HubPing(2)));

        Assert.Equal(["a-on-host:1"], _heard);
        Assert.Equal(1, hubs.HubCount);
        Assert.Equal(0, hubs.ListenerCount);
        Assert.Contains(_logs.Warnings, line => line.Contains("host added a IHubPingListener to the events of a, which are closed"));
    }

    /// <summary>
    /// 閉じた口は、閉じる前に足されていたものにも、もう配らない（畳まれたモジュールの thread が残っていて配っても、よそへ届かない）。
    /// Host が 1 つずつ閉じる口（モジュールごとの設定の口）も同じ。
    /// </summary>
    [Fact]
    public void AClosedHubNoLongerCallsWhatWasAddedBeforeItClosed()
    {
        var hubs = Hubs();
        var a = Module("a");
        var b = Module("b");
        var ofA = hubs.Create<IHubPingListener>(a);
        var settings = hubs.CreateHub(HostOwner.Instance, [typeof(IHubThingListener<string>)]);
        ofA.Source.AddListener<IHubPingListener>(b, new Ear(_heard, "b-on-a"));
        settings.Source.AddListener<IHubThingListener<string>>(b, new Thing<string>(_heard));

        hubs.Forget(a);
        settings.Close();
        ofA.Fire<IHubPingListener>(l => l.OnPing(new HubPing(1)));
        settings.Fire<IHubThingListener<string>>(l => l.OnThing("x"));

        Assert.Empty(_heard);
        Assert.Equal(0, ((EventHub)ofA).ListenerCount);
        Assert.Equal(0, settings.ListenerCount);
    }

    /// <summary>
    /// モジュールごとの設定の口は Host が作り、listener を足すのはそのモジュール（作った owner と寿命が別）。
    /// 読み込んで畳むのを繰り返しても、口も listener も増えない。
    /// </summary>
    [Fact]
    public void LoadingAndShuttingDownAgainAndAgainLeavesNothingBehind()
    {
        var hubs = Hubs();
        var always = hubs.Create<IHubPingListener>(HostOwner.Instance);
        always.Source.AddListener<IHubPingListener>(HostOwner.Instance, new Ear(_heard, "host"));

        for (var round = 0; round < 50; round++)
        {
            var module = Module($"p{round}");
            var settings = hubs.CreateHub(HostOwner.Instance, [typeof(IHubThingListener<string>)]);
            var own = hubs.Create<IHubPingListener>(module);
            settings.Source.AddListener<IHubThingListener<string>>(module, new Thing<string>(_heard));
            always.Source.AddListener<IHubPingListener>(module, new Ear(_heard, "module"));
            own.Source.AddListener<IHubPingListener>(HostOwner.Instance, new Ear(_heard, "host-on-module"));

            settings.Close();
            hubs.Forget(module);
            Unload(module);
        }

        Assert.Equal(1, hubs.HubCount);
        Assert.Equal(1, hubs.ListenerCount);
    }

    /// <summary>設定の口（<c>IConfigEventListener&lt;TConfig&gt;</c>）の形。<c>&lt;A&gt;</c> の口に <c>&lt;B&gt;</c> は足せない。</summary>
    [Fact]
    public void AClosedGenericListenerTypeWorksAndAnotherArgumentIsRefused()
    {
        var owner = Module("a");
        var hub = Hubs().Create<IHubThingListener<string>>(owner);
        hub.Source.AddListener<IHubThingListener<string>>(owner, new Thing<string>(_heard));

        var otherType = Assert.Throws<ArgumentException>(() => hub.Source.AddListener<IHubThingListener<int>>(owner, new Thing<int>(_heard)));
        var otherInstance = Assert.Throws<ArgumentException>(() => hub.Source.AddListener<IHubThingListener<string>>(owner, new Thing<int>(_heard)));
        hub.Fire<IHubThingListener<string>>(l => l.OnThing("x"));

        Assert.Equal(["thing:x"], _heard);
        Assert.Contains("the events of 'a' give out IHubThingListener<String>, not IHubThingListener<Int32>", otherType.Message);
        Assert.Contains("does not implement", otherInstance.Message);
    }

    /// <summary>
    /// 共有の contract が 2 つ読み込まれている（見た目は実装しているのに型が別）とき、断る文がそれを言う。モジュールを書く AI が最初に踏む所。
    /// 別の写しは、同じ名前の interface を別の assembly に作って真似る。
    /// </summary>
    [Fact]
    public void AnotherCopyOfTheContractIsNamedInTheRefusal()
    {
        var copy = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("ContractCopy"), AssemblyBuilderAccess.Run);
        var module = copy.DefineDynamicModule("ContractCopy");
        var lookalike = module.DefineType(typeof(IHubPingListener).FullName!,
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract, null, [typeof(IEventListener)]).CreateType();
        var listenerType = module.DefineType("ContractCopy.Ear",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed, typeof(object), [lookalike]);
        listenerType.DefineDefaultConstructor(MethodAttributes.Public);
        var listener = (IEventListener)Activator.CreateInstance(listenerType.CreateType())!;
        var owner = Module("a");
        var hub = Hubs().Create<IHubPingListener>(owner);

        var refused = Assert.Throws<ArgumentException>(() => hub.Source.AddListener<IHubPingListener>(owner, listener));

        Assert.Contains($"implements an interface named {typeof(IHubPingListener).FullName}, but from another copy of its assembly", refused.Message);
        Assert.Contains("ContractCopy", refused.Message);
        Assert.Contains(typeof(IHubPingListener).Assembly.Location, refused.Message);
        Assert.Contains("The contract was loaded twice", refused.Message);
    }

    /// <summary>
    /// ログの行は UI の外の thread から続けて来る。その間に別の thread から足す・外す・畳むが起きても、配る側は投げず、取りこぼしも起きない
    /// （ずっと足したままの listener には、配った回数だけ届く）。
    /// </summary>
    [Fact]
    public void FiringWhileOtherThreadsAddRemoveAndForgetDoesNotBreak()
    {
        var hubs = Hubs();
        var hub = hubs.Create<IHubPingListener>(HostOwner.Instance);
        var steady = 0;
        hub.Source.AddListener<IHubPingListener>(HostOwner.Instance, new Ear([], "steady") { WhenPinged = () => Interlocked.Increment(ref steady) });

        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var churn = Enumerable.Range(0, 3).Select(worker => Task.Run(() =>
        {
            var round = 0;
            while (!stop.IsCancellationRequested)
            {
                var module = Module($"w{worker}-{round++}");
                var ear = new Ear([], "churn");
                hub.Source.AddListener<IHubPingListener>(module, ear);
                hub.Source.AddListener<IHubPingListener>(module, new Ear([], "kept"));
                hub.Source.RemoveListener<IHubPingListener>(module, ear);
                hubs.Forget(module);
                Unload(module);
            }
        })).ToArray();

        var fired = 0;
        var firing = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                hub.Fire<IHubPingListener>(l => l.OnPing(new HubPing(0)));
                fired++;
            }
        });

        Task.WaitAll([.. churn, firing]);

        Assert.True(fired > 100, $"配れた回数が少なすぎる ({fired})");
        Assert.Equal(fired, steady);
        Assert.Equal(1, hubs.ListenerCount);
        Assert.Empty(_logs.Warnings);
    }

    /// <summary>Host の中だけの口の listener（試験用。本番は言語・保存・ログのファイルの 3 つ）。</summary>
    internal interface IHostOnlyListener : IEventListener
    {
        void OnSomething(HubPing e);
    }

    private sealed class HostOnlyEar(List<string> heard) : IHostOnlyListener
    {
        public void OnSomething(HubPing e)
        {
            lock (heard) heard.Add($"host:{e.N}");
        }
    }

    /// <summary>
    /// <b>Host の中だけの口（<see cref="EventHubs.HostOnly{TListener}"/>）には、アプリ自身しか足せない</b> —
    /// 読み込まれているモジュールでも断る（口がうっかり外へ出ても、モジュールは聞けない）。投げた受け手の記録は、渡した logger に残る。
    /// </summary>
    [Fact]
    public void AHostOnlyHubTakesListenersFromTheAppAlone()
    {
        var hub = EventHubs.HostOnly<IHostOnlyListener>(_logs);
        var module = Module("p");

        var refused = Assert.Throws<InvalidOperationException>(() =>
            hub.Source.AddListener<IHostOnlyListener>(module, new HostOnlyEar(_heard)));
        Assert.Contains("'p'", refused.Message);
        Assert.Contains("is not a loaded module", refused.Message);

        hub.Source.AddListener<IHostOnlyListener>(HostOwner.Instance, new HostOnlyEar(_heard));
        hub.Fire<IHostOnlyListener>(l => l.OnSomething(new HubPing(1)));
        Assert.Equal(["host:1"], _heard);

        hub.Source.AddListener<IHostOnlyListener>(HostOwner.Instance, new ThrowingHostOnly());
        hub.Fire<IHostOnlyListener>(l => l.OnSomething(new HubPing(2)), () => "row 2");
        Assert.Equal(["host:1", "host:2"], _heard);
        Assert.Contains(_logs.Warnings, w => w.Contains("IHostOnlyListener") && w.Contains("(row 2)"));
    }

    private sealed class ThrowingHostOnly : IHostOnlyListener
    {
        public void OnSomething(HubPing e) => throw new InvalidOperationException("boom");
    }

    private sealed class ManyEar(Func<IEnumerable<string?>?> answer) : IHubManyListener
    {
        public IEnumerable<string?>? Many(HubPing e) => answer();
    }

    private static IEnumerable<string?> ThrowsHalfway()
    {
        yield return "lost";
        throw new InvalidOperationException("boom while counting");
    }

    /// <summary>
    /// <b>答えの並びを 1 つにつなげる</b>（配る順）。null の並び・並びの中の null・投げた listener の分は入らない。
    /// 並びを数えている途中で投げた分も入らず（半分だけ入らない）、記録が残る。
    /// </summary>
    [Fact]
    public void FireCollectManyJoinsTheAnswersInOrderAndSkipsWhatBroke()
    {
        var owner = Module("a");
        var hub = Hubs().Create<IHubManyListener>(owner);
        hub.Source.AddListener<IHubManyListener>(owner, new ManyEar(() => ["x", null, "y"]));
        hub.Source.AddListener<IHubManyListener>(owner, new ManyEar(() => null));
        hub.Source.AddListener<IHubManyListener>(owner, new ManyEar(ThrowsHalfway));
        hub.Source.AddListener<IHubManyListener>(owner, new ManyEar(() => throw new InvalidOperationException("boom")));
        hub.Source.AddListener<IHubManyListener>(owner, new ManyEar(() => ["z"]));

        var all = hub.FireCollectMany<IHubManyListener, string?>(l => l.Many(new HubPing(1)));

        Assert.Equal(["x", "y", "z"], all);
        Assert.Equal(2, _logs.Warnings.Count(w => w.Contains("IHubManyListener")));
    }

    /// <summary>
    /// <b>当てはまる最初の答えを返し、そこから先には聞かない</b>。誰も当てはまらなければ決めておいた答え。投げた listener は飛ばす。
    /// </summary>
    [Fact]
    public void FireWithResultReturnsTheFirstAnswerThatStopsAndAsksNoOneAfter()
    {
        var owner = Module("a");
        var hub = Hubs().Create<IHubCountListener>(owner);
        hub.Source.AddListener<IHubCountListener>(owner, new Ear(_heard, "empty") { Counted = () => null });
        hub.Source.AddListener<IHubCountListener>(owner, new Ear(_heard, "broken") { Counted = () => throw new InvalidOperationException("boom") });
        hub.Source.AddListener<IHubCountListener>(owner, new Ear(_heard, "first") { Counted = () => "1" });
        hub.Source.AddListener<IHubCountListener>(owner, new Ear(_heard, "never") { Counted = () => { lock (_heard) _heard.Add("never asked"); return "2"; } });

        var found = hub.FireWithResult<IHubCountListener, string?>(l => l.Count(new HubPing(1)), stop: s => s is not null, whenNoneStops: "none");

        Assert.Equal("1", found);
        Assert.DoesNotContain("never asked", _heard);
        Assert.Contains(_logs.Warnings, w => w.Contains("IHubCountListener"));

        var nobody = Hubs().Create<IHubCountListener>(owner);
        nobody.Source.AddListener<IHubCountListener>(owner, new Ear(_heard, "empty") { Counted = () => null });
        Assert.Equal("none", nobody.FireWithResult<IHubCountListener, string?>(l => l.Count(new HubPing(1)), stop: s => s is not null, whenNoneStops: "none"));
    }
}
