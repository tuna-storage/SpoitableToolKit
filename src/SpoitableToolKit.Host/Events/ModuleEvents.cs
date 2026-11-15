using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.VrcLog;

namespace SpoitableToolKit.Host.Events;

/// <summary>
/// owner を参照で照合するのが役目 — Id が同じだけの別のインスタンスや、ほかのモジュールを owner として通さない。
/// ほかのモジュールが見せている口（<see cref="IEventSource"/>）は包まない（受ける側が誰かは、その口からは分からない）。
/// </summary>
internal sealed class ModuleEvents(ISpoitableModule module, HostEvents events)
{
    public IEventHubs Hubs { get; } = new OwnedHubs(module, events.Hubs);

    public IEventSource VRChat { get; } = new OwnedSource(module, events.VRChat.Source, nameof(IModuleHost.VRChat));

    public IEventSource Videos { get; } = new OwnedSource(module, events.Videos.Source, nameof(IModuleHost.Videos));

    /// <summary>いま読んでいるファイルはそのまま見せ、足す・外すは owner を照合してから渡す。</summary>
    public IVrcLogBus LogOf(IVrcLogBus bus) => new OwnedVrcLog(module, bus);

    /// <summary>足す・外すは owner を照合してから渡す。設定が無ければ断るだけの口。</summary>
    public IEventSource ConfigOf(IEventSource config) => new OwnedSource(module, config, nameof(IModuleHost.Config));

    private static void MustBe(ISpoitableModule module, ISpoitableModule owner, string where)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (ReferenceEquals(owner, module)) return;

        throw new InvalidOperationException(
            $"Tried to use '{owner.Id}' as the owner through host.{where} of '{module.Id}'. Pass the module itself (this) as the owner.");
    }

    private sealed class OwnedHubs(ISpoitableModule module, IEventHubs inner) : IEventHubs
    {
        public IEventHub Create<TListener>(ISpoitableModule owner) where TListener : class, IEventListener
        {
            MustBe(module, owner, nameof(IModuleHost.Events));
            return inner.Create<TListener>(owner);
        }

        public IEventHub Create<TListener1, TListener2>(ISpoitableModule owner)
            where TListener1 : class, IEventListener
            where TListener2 : class, IEventListener
        {
            MustBe(module, owner, nameof(IModuleHost.Events));
            return inner.Create<TListener1, TListener2>(owner);
        }
    }

    private sealed class OwnedVrcLog(ISpoitableModule module, IVrcLogBus inner) : IVrcLogBus
    {
        public string? CurrentLogFile => inner.CurrentLogFile;

        public void AddListener<TListener>(ISpoitableModule owner, IEventListener listener) where TListener : class, IEventListener
        {
            MustBe(module, owner, nameof(IModuleHost.VrcLog));
            inner.AddListener<TListener>(owner, listener);
        }

        public void RemoveListener<TListener>(ISpoitableModule owner, IEventListener listener) where TListener : class, IEventListener
        {
            MustBe(module, owner, nameof(IModuleHost.VrcLog));
            inner.RemoveListener<TListener>(owner, listener);
        }
    }

    private sealed class OwnedSource(ISpoitableModule module, IEventSource inner, string where) : IEventSource
    {
        public void AddListener<TListener>(ISpoitableModule owner, IEventListener listener) where TListener : class, IEventListener
        {
            MustBe(module, owner, where);
            inner.AddListener<TListener>(owner, listener);
        }

        public void RemoveListener<TListener>(ISpoitableModule owner, IEventListener listener) where TListener : class, IEventListener
        {
            MustBe(module, owner, where);
            inner.RemoveListener<TListener>(owner, listener);
        }
    }
}
