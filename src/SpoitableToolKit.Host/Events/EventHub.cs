using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.Events.VRChat;

namespace SpoitableToolKit.Host.Events;

/// <summary>
/// 配る側は lock を取らない。型ごとの枠（<see cref="Slot"/>）は constructor で作り切り、あとから key を増やさない。
/// 足す・外すは lock の中で並べ直した新しい配列に差し替えるだけなので、配っている最中の回は、始めたときの配列を最後まで読む
/// （ログの行は UI の外の thread から 1 行ずつ続けて来る。その間に別の thread から足す・外すが起きても壊れない）。
/// 外したものには印（<see cref="Registration.Removed"/>）を付けるので、配っている最中の回でも、まだ呼んでいなければもう呼ばない。
/// 優先度を読むのは足したときの 1 回だけ（配るたびに読むと、途中で変えた listener が並びを崩す）。
/// </summary>
internal sealed class EventHub : IEventHub
{
    private readonly EventHubs _hubs;
    private readonly ILogger _logger;
    private readonly Dictionary<Type, Slot> _slots;
    private readonly Lock _gate = new();
    private long _order;
    private bool _closed;

    internal EventHub(EventHubs hubs, ISpoitableModule owner, IReadOnlyList<Type> served, ILogger logger)
    {
        _hubs = hubs;
        _logger = logger;
        Owner = owner;
        _slots = served.ToDictionary(t => t, _ => new Slot());
        Source = new EventSource(this);
    }

    public ISpoitableModule Owner { get; }

    public IEventSource Source { get; }

    internal int ListenerCount => _slots.Values.Sum(s => s.Listeners.Length);

    public void Fire<TListener>(Action<TListener> invoke) where TListener : class, IEventListener => Fire(invoke, about: null);

    /// <summary>配る。<paramref name="about"/> は、listener が投げたときの記録に足す「何を配っていたか」。呼ぶのは投げたときだけ。</summary>
    internal void Fire<TListener>(Action<TListener> invoke, Func<string>? about) where TListener : class, IEventListener
    {
        ArgumentNullException.ThrowIfNull(invoke);

        foreach (var registration in SlotFor(typeof(TListener), nameof(Fire)).Listeners)
        {
            if (registration.Removed) continue;

            try
            {
                invoke((TListener)registration.Listener);
            }
            catch (Exception ex)
            {
                Threw(ex, registration, typeof(TListener), about);
            }
        }
    }

    public EventResult FireCancellable<TListener>(Func<TListener, EventResult> invoke) where TListener : class, IEventListener
    {
        ArgumentNullException.ThrowIfNull(invoke);

        foreach (var registration in SlotFor(typeof(TListener), nameof(FireCancellable)).Listeners)
        {
            if (registration.Removed) continue;

            EventResult? result;
            try
            {
                result = invoke((TListener)registration.Listener);
            }
            catch (Exception ex)
            {
                Threw(ex, registration, typeof(TListener));
                continue;
            }

            switch (result?.Kind)
            {
                case null:
                    _logger.LogWarning(
                        "{Listener} of {Owner} answered null to {Type} of {Publisher}; taken as Continue. Return EventResult.Continue instead",
                        FullNameOf(registration.Listener.GetType()), registration.Owner.Id, NameOf(typeof(TListener)), Owner.Id);
                    continue;

                case EventResultKind.Continue:
                    continue;

                case EventResultKind.Handled:
                    _logger.LogDebug("{Listener} of {Owner} handled {Type} of {Publisher}",
                        FullNameOf(registration.Listener.GetType()), registration.Owner.Id, NameOf(typeof(TListener)), Owner.Id);
                    return result;

                case EventResultKind.Stop:
                    _logger.LogInformation("{Listener} of {Owner} stopped {Type} of {Publisher}: {Reason}",
                        FullNameOf(registration.Listener.GetType()), registration.Owner.Id, NameOf(typeof(TListener)), Owner.Id, result.Reason);
                    return result;
            }
        }

        return EventResult.Continue;
    }

    public IReadOnlyList<TResult> FireCollect<TListener, TResult>(Func<TListener, TResult> invoke) where TListener : class, IEventListener
    {
        ArgumentNullException.ThrowIfNull(invoke);

        List<TResult> results = [];
        foreach (var registration in SlotFor(typeof(TListener), nameof(FireCollect)).Listeners)
        {
            if (registration.Removed) continue;

            try
            {
                if (invoke((TListener)registration.Listener) is { } result) results.Add(result);
            }
            catch (Exception ex)
            {
                Threw(ex, registration, typeof(TListener));
            }
        }

        return results;
    }

    /// <summary>閉じる: 足されていた listener を全部外し、この先は足されても届かない（記録に残して受け流す）。何度呼んでもよい。</summary>
    internal void Close()
    {
        lock (_gate)
        {
            _closed = true;
            foreach (var slot in _slots.Values) slot.Listeners = Without(slot.Listeners, _ => true);
        }

        _hubs.Closed(this);
    }

    internal void RemoveAllOf(ISpoitableModule owner)
    {
        lock (_gate)
            foreach (var slot in _slots.Values)
                slot.Listeners = Without(slot.Listeners, r => ReferenceEquals(r.Owner, owner));
    }

    private void Add(Type type, ISpoitableModule owner, IEventListener listener)
    {
        CheckListenerType(type, nameof(IEventSource.AddListener));
        ArgumentNullException.ThrowIfNull(listener);
        CheckNotAStruct(listener, nameof(IEventSource.AddListener));
        var slot = SlotFor(type, nameof(IEventSource.AddListener));
        _hubs.CheckOwner(owner);

        if (!type.IsInstanceOfType(listener))
            throw new ArgumentException(NotImplementing(type, listener), nameof(listener));

        var priority = PriorityOf(listener, owner);

        lock (_gate)
        {
            // 上の確かめと、ここで入れるまでの間に owner が畳まれていたら入れない（優先度を読む間はモジュールのコードが走る）。
            // 畳む側（EventHubs.Forget）は印を付けてから、この lock を取って外すので、印が無いうちに入れたものは、そのあとで外れる。
            if (_hubs.IsGone(owner)) throw EventHubs.NotLoaded(owner);

            if (_closed)
            {
                _logger.LogWarning(
                    "{Owner} added a {Type} to the events of {Publisher}, which are closed (that module was shut down); it will not be called",
                    owner.Id, NameOf(type), Owner.Id);
                return;
            }

            var current = slot.Listeners;
            if (Array.Find(current, r => ReferenceEquals(r.Listener, listener)) is { } existing)
            {
                if (ReferenceEquals(existing.Owner, owner)) return;

                throw new InvalidOperationException(
                    $"This {FullNameOf(listener.GetType())} has already been added as {NameOf(type)} by '{existing.Owner.Id}'. " +
                    "An instance is added by one owner only; give each module its own instance.");
            }

            var added = new Registration(owner, listener, priority, _order++);
            slot.Listeners = [.. current.Append(added).OrderByDescending(r => r.Priority).ThenBy(r => r.Order)];
        }
    }

    private void Remove(Type type, ISpoitableModule owner, IEventListener listener)
    {
        CheckListenerType(type, nameof(IEventSource.RemoveListener));
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(listener);
        CheckNotAStruct(listener, nameof(IEventSource.RemoveListener));
        var slot = SlotFor(type, nameof(IEventSource.RemoveListener));

        lock (_gate)
        {
            if (Array.Find(slot.Listeners, r => ReferenceEquals(r.Listener, listener)) is not { } found) return;

            if (!ReferenceEquals(found.Owner, owner))
            {
                throw new InvalidOperationException(
                    $"This {FullNameOf(listener.GetType())} was added as {NameOf(type)} by '{found.Owner.Id}', so only '{found.Owner.Id}' can remove it " +
                    $"(it was asked by '{owner.Id}').");
            }

            slot.Listeners = Without(slot.Listeners, r => ReferenceEquals(r, found));
        }
    }

    /// <summary>外すものに印を付けてから、残りの配列を返す（配っている最中の回が、印を見て飛ばせるように）。</summary>
    private static Registration[] Without(Registration[] current, Func<Registration, bool> removing)
    {
        foreach (var registration in current)
            if (removing(registration)) registration.Removed = true;

        return [.. current.Where(r => !r.Removed)];
    }

    private Slot SlotFor(Type type, string method)
    {
        if (_slots.TryGetValue(type, out var slot)) return slot;

        throw new ArgumentException(
            $"{method}<{NameOf(type)}>: the events of '{Owner.Id}' give out {string.Join(" and ", _slots.Keys.Select(NameOf))}, not {NameOf(type)}. " +
            $"Use the source that gives out {NameOf(type)}.");
    }

    /// <summary>型引数が listener の interface か（<c>where TListener : class, IEventListener</c> だけでは、クラスや土台そのものが通る）。</summary>
    internal static void CheckListenerType(Type type, string method)
    {
        if (!typeof(IEventListener).IsAssignableFrom(type))
            throw new ArgumentException($"{method}<{NameOf(type)}>: {FullNameOf(type)} is not a listener interface (it does not extend {nameof(IEventListener)}).");

        if (type == typeof(IEventListener))
            throw new ArgumentException(
                $"{method}<{nameof(IEventListener)}>: write the listener interface of the events (for example {nameof(IVRChatEventListener)}), not {nameof(IEventListener)} itself.");

        if (!type.IsInterface)
            throw new ArgumentException(
                $"{method}<{NameOf(type)}>: {FullNameOf(type)} is a class. Write the listener interface it implements, not the class.");
    }

    /// <summary>
    /// struct の listener は断る。<c>IEventListener</c> として渡すたびに別の箱になるので、
    /// 同じものを 2 回足すと 2 回届き、外そうとしても見つからない（黙って二重に届く・黙って外れない）。
    /// </summary>
    private static void CheckNotAStruct(IEventListener listener, string method)
    {
        if (!listener.GetType().IsValueType) return;

        throw new ArgumentException(
            $"{method}: {FullNameOf(listener.GetType())} is a struct. Pass an instance of a class: a struct is boxed anew every time it is passed, " +
            "so adding it twice delivers twice and removing it finds nothing.", nameof(listener));
    }

    /// <summary>「実装していない」の文。共有の contract が 2 つ読み込まれているとき（見た目は実装しているのに型が別）は、その手掛かりを足す。</summary>
    private static string NotImplementing(Type type, IEventListener listener)
    {
        var listenerType = listener.GetType();
        var lookalike = listenerType.GetInterfaces().FirstOrDefault(i => i != type && i.FullName == type.FullName);

        if (lookalike is null)
            return $"{FullNameOf(listenerType)} does not implement {FullNameOf(type)}. Implement {NameOf(type)}, or add it as the listener type it does implement.";

        return $"{FullNameOf(listenerType)} implements an interface named {FullNameOf(type)}, but from another copy of its assembly " +
               $"({Where(lookalike)}) than the one these events give out ({Where(type)}). The contract was loaded twice: " +
               "put the contract dll in the shared/ folder of the module that defines it (modules/<Id>/shared/) and do not ship your own copy of it.";

        static string Where(Type t) => t.Assembly.Location is { Length: > 0 } location ? location : $"{t.Assembly.FullName}, in memory";
    }

    /// <summary>
    /// 文に出す型の名前。generic は <c>IConfigEventListener&lt;HrsConfig&gt;</c> の形にする
    /// （そのままだと <c>IConfigEventListener`1</c> になり、「<c>&lt;A&gt;</c> の口に <c>&lt;B&gt;</c>」を断る文が、同じ名前を 2 つ並べて読めない）。
    /// </summary>
    internal static string NameOf(Type type) =>
        type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(NameOf))}>"
            : type.Name;

    internal static string FullNameOf(Type type) =>
        type.IsGenericType
            ? (type.Namespace is { Length: > 0 } space ? $"{space}.{NameOf(type)}" : NameOf(type))
            : type.FullName ?? type.Name;

    private int PriorityOf(IEventListener listener, ISpoitableModule owner)
    {
        try
        {
            return listener.ListenerPriority;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ListenerPriority of {Listener} ({Owner}) threw; taken as 0", FullNameOf(listener.GetType()), owner.Id);
            return 0;
        }
    }

    private void Threw(Exception ex, Registration registration, Type type, Func<string>? about = null)
    {
        if (about is null)
        {
            _logger.LogWarning(ex, "{Listener} of {Owner} threw while {Publisher} was giving out {Type}",
                FullNameOf(registration.Listener.GetType()), registration.Owner.Id, Owner.Id, NameOf(type));
            return;
        }

        _logger.LogWarning(ex, "{Listener} of {Owner} threw while {Publisher} was giving out {Type} ({About})",
            FullNameOf(registration.Listener.GetType()), registration.Owner.Id, Owner.Id, NameOf(type), about());
    }

    /// <summary>型ごとの枠。中の配列は差し替えるだけ（読む側は lock を取らない）。</summary>
    private sealed class Slot
    {
        public volatile Registration[] Listeners = [];
    }

    private sealed class Registration(ISpoitableModule owner, IEventListener listener, int priority, long order)
    {
        public ISpoitableModule Owner { get; } = owner;

        public IEventListener Listener { get; } = listener;

        public int Priority { get; } = priority;

        public long Order { get; } = order;

        public volatile bool Removed;
    }

    /// <summary>受ける側に見せる口。<b>別の object</b>なので、cast しても <see cref="IEventHub"/> は出てこない。</summary>
    private sealed class EventSource(EventHub hub) : IEventSource
    {
        public void AddListener<TListener>(ISpoitableModule owner, IEventListener listener) where TListener : class, IEventListener =>
            hub.Add(typeof(TListener), owner, listener);

        public void RemoveListener<TListener>(ISpoitableModule owner, IEventListener listener) where TListener : class, IEventListener =>
            hub.Remove(typeof(TListener), owner, listener);
    }
}
