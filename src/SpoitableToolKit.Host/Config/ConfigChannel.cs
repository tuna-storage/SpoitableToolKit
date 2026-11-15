using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.Events.Config;
using SpoitableToolKit.Host.Config.Interfaces;
using SpoitableToolKit.Host.Config.Models;
using SpoitableToolKit.Host.Events;

namespace SpoitableToolKit.Host.Config;

internal static class ConfigChannel
{
    /// <summary>
    /// 口の owner はアプリ自身（配るのはホストだけ）。配る型は <c>IConfigEventListener&lt;その型&gt;</c> だけで、
    /// 違う設定の型で足すと、口が分かる文で断る。
    /// </summary>
    public static IConfigChannel Create(EventHubs hubs, Type configType)
    {
        var listener = typeof(IConfigEventListener<>).MakeGenericType(configType);
        var hub = hubs.CreateHub(HostOwner.Instance, [listener]);
        return (IConfigChannel)Activator.CreateInstance(typeof(ConfigChannel<>).MakeGenericType(configType), hub)!;
    }

    /// <summary>設定を持たないモジュール用。足そうとしたら、設定が無いと言って断る。</summary>
    public static IEventSource None(string moduleId) => new NoConfig(moduleId);

    private sealed class NoConfig(string moduleId) : IEventSource
    {
        public void AddListener<TListener>(ISpoitableModule owner, IEventListener listener) where TListener : class, IEventListener =>
            throw Refused(typeof(TListener));

        public void RemoveListener<TListener>(ISpoitableModule owner, IEventListener listener) where TListener : class, IEventListener =>
            throw Refused(typeof(TListener));

        private InvalidOperationException Refused(Type listener) => new(
            $"'{moduleId}' has no settings (ISpoitableModule.Config is null), so host.Config has no changes to give {EventHub.NameOf(listener)}. " +
            "Return the module's IModuleConfig from ISpoitableModule.Config to have settings, then add the listener with " +
            "host.Config.AddListener<IConfigEventListener<YourConfig>>(this, listener).");
    }
}

internal sealed class ConfigChannel<TConfig>(EventHub hub) : IConfigChannel where TConfig : class, IModuleConfig
{
    public IEventSource Source => hub.Source;

    public int ListenerCount => hub.ListenerCount;

    public ConfigAnswer Ask(IModuleConfig proposed, IReadOnlyList<string> changed, IReadOnlyCollection<string> settings)
    {
        var e = new ConfigChangingEventParams<TConfig>((TConfig)proposed, changed, settings);
        var result = hub.FireCancellable<IConfigEventListener<TConfig>>(l => l.OnConfigChanging(e));
        return new ConfigAnswer(result, e.Rejections);
    }

    public void Tell(IModuleConfig config, IReadOnlyList<string> entered, IReadOnlyCollection<string> settings)
    {
        var e = new ConfigChangedEventParams<TConfig>((TConfig)config, entered, settings);
        hub.Fire<IConfigEventListener<TConfig>>(l => l.OnConfigChanged(e));
    }

    public void Close() => hub.Close();
}
