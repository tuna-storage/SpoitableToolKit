using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.Events.VrcLog;
using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.Events;

namespace SpoitableToolKit.Tests.Fakes;

/// <summary>
/// 何も監視しない <see cref="IVrcLogBus"/>（本物は FileSystemWatcher でログを tail し始める）。
/// 行は本物と同じく <see cref="Hub"/> から配る。owner の名簿は見ない。
/// </summary>
internal sealed class FakeVrcLogBus : IVrcLogBus
{
    public FakeVrcLogBus(EventHub? hub = null) =>
        Hub = hub ?? new EventHubs(_ => true, NullLoggerFactory.Instance).CreateHub(HostOwner.Instance, [typeof(IVrcLogEventListener)]);

    public EventHub Hub { get; }

    public string? CurrentLogFile => null;

    public void AddListener<TListener>(ISpoitableModule owner, IEventListener listener) where TListener : class, IEventListener =>
        Hub.Source.AddListener<TListener>(owner, listener);

    public void RemoveListener<TListener>(ISpoitableModule owner, IEventListener listener) where TListener : class, IEventListener =>
        Hub.Source.RemoveListener<TListener>(owner, listener);

    public void Emit(VrcLogLine line) => Hub.Fire<IVrcLogEventListener>(l => l.OnLogLine(line));

    public void Emit(VrcLogEventParams logEvent) => Hub.Fire<IVrcLogEventListener>(l => l.OnLogEvent(logEvent));
}
