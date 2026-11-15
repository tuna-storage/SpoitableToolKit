using SpoitableToolKit.Abstractions.Events;

namespace SpoitableToolKit.Tests.Events;

/// <summary>設定の口（<c>IConfigEventListener&lt;TConfig&gt;</c>）と同じ形: 閉じた generic の listener。</summary>
internal interface IHubThingListener<T> : IEventListener
{
    void OnThing(T value)
    {
    }
}
