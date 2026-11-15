using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Events;

namespace SpoitableToolKit.Host.Events;

/// <summary>
/// 覚えておくのは、畳まれたモジュールの分をまとめて片付けるため（<see cref="Forget"/>）: その owner が足した listener を全部の口から外し、
/// その owner が作った口を閉じる。閉じた口はここから消すので、読み込んで畳むのを繰り返しても数は増えない。
/// 口の寿命は、作った owner の寿命と同じとは限らない。モジュールごとの設定の口は Host（<see cref="HostOwner"/>）が作り、
/// listener を足すのはそのモジュール — そういう口は、そのモジュールが畳まれたときに Host が <see cref="EventHub.Close"/> で 1 つずつ閉じる。
/// </summary>
internal sealed class EventHubs : IEventHubs
{
    private readonly Func<ISpoitableModule, bool> _isLoaded;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly List<EventHub> _hubs = [];

    /// <summary>
    /// 畳んだ owner（<see cref="Forget"/>）。畳むのと、足す・作るが別の thread で重なったときのための印 —
    /// 「読み込まれているか」を lock の外で聞いてから足すと、その間に畳まれた owner の listener が、外す相手のいないまま残る。
    /// 足す側（<see cref="EventHub"/> の lock の中）と作る側（<see cref="_gate"/> の中）は、入れる直前にこれを見る。弱い参照で持つ（畳んだモジュールを生かさない）。
    /// </summary>
    private readonly ConditionalWeakTable<ISpoitableModule, object> _gone = [];

    public EventHubs(Func<ISpoitableModule, bool> isLoaded, ILoggerFactory loggerFactory)
        : this(isLoaded, loggerFactory.CreateLogger<EventHubs>())
    {
    }

    private EventHubs(Func<ISpoitableModule, bool> isLoaded, ILogger logger)
    {
        _isLoaded = isLoaded;
        _logger = logger;
    }

    /// <summary>
    /// Host の中だけで配る口。足せるのはアプリ自身（<see cref="HostOwner"/>）だけ — モジュールは owner として通らないので、口がうっかり外へ出ても足せない。
    /// listener の型も Host の中の interface にする（モジュールからは実装できない）。
    /// </summary>
    internal static EventHub HostOnly<TListener>(ILogger logger) where TListener : class, IEventListener =>
        new EventHubs(_ => false, logger).CreateHub(HostOwner.Instance, [typeof(TListener)]);

    public IEventHub Create<TListener>(ISpoitableModule owner) where TListener : class, IEventListener =>
        CreateHub(owner, [typeof(TListener)]);

    public IEventHub Create<TListener1, TListener2>(ISpoitableModule owner)
        where TListener1 : class, IEventListener
        where TListener2 : class, IEventListener =>
        CreateHub(owner, [typeof(TListener1), typeof(TListener2)]);

    internal EventHub CreateHub(ISpoitableModule owner, IReadOnlyList<Type> served)
    {
        ArgumentNullException.ThrowIfNull(owner);

        foreach (var type in served) EventHub.CheckListenerType(type, nameof(Create));

        if (served.Distinct().Count() != served.Count)
            throw new ArgumentException(
                $"Create<{string.Join(", ", served.Select(EventHub.NameOf))}>: the same listener type is written twice. Write each type once.");

        var hub = new EventHub(this, owner, served, _logger);
        lock (_gate)
        {
            // owner を確かめるのは、一覧に入れる lock の中で。Forget は同じ lock の中で印を付けてから一覧を写すので、
            // ここで通れば、その写しにこの口も入る（外で確かめると、その間に畳まれた owner の口が、閉じられないまま残る）。
            CheckOwner(owner);
            _hubs.Add(hub);
        }

        return hub;
    }

    /// <summary><paramref name="owner"/> が畳まれた: 足した listener を全部の口から外し、作った口を閉じる。呼んだあとで同じ owner が足そうとしても断る（<see cref="CheckOwner"/>）。</summary>
    public void Forget(ISpoitableModule owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        EventHub[] hubs;
        lock (_gate)
        {
            _gone.AddOrUpdate(owner, Gone);
            hubs = [.. _hubs];
        }

        var closed = 0;
        foreach (var hub in hubs)
        {
            if (ReferenceEquals(hub.Owner, owner))
            {
                hub.Close();
                closed++;
            }
            else
            {
                hub.RemoveAllOf(owner);
            }
        }

        if (closed > 0) _logger.LogDebug("Closed {Count} event hub(s) of {Owner}", closed, owner.Id);
    }

    /// <summary>owner として受けるか。黙って受けると、畳まれたモジュールの listener が外れずに残る（Host が外す目印にならない）。</summary>
    internal void CheckOwner(ISpoitableModule owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (!IsGone(owner) && (ReferenceEquals(owner, HostOwner.Instance) || _isLoaded(owner))) return;

        throw NotLoaded(owner);
    }

    internal bool IsGone(ISpoitableModule owner) => _gone.TryGetValue(owner, out _);

    internal static InvalidOperationException NotLoaded(ISpoitableModule owner) =>
        new($"'{owner.Id}' ({owner.GetType().FullName}) is not a loaded module, so it cannot own listeners or event hubs. " +
            "Pass the module itself (this) as the owner, from OnInit or later, and not after OnShutdown.");

    private static readonly object Gone = new();

    internal void Closed(EventHub hub)
    {
        lock (_gate) _hubs.Remove(hub);
    }

    internal int HubCount
    {
        get
        {
            lock (_gate) return _hubs.Count;
        }
    }

    internal int ListenerCount
    {
        get
        {
            EventHub[] hubs;
            lock (_gate) hubs = [.. _hubs];
            return hubs.Sum(h => h.ListenerCount);
        }
    }
}
