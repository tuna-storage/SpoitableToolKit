using SpoitableToolKit.Abstractions.Events;

namespace SpoitableToolKit.Tests.Events;

internal interface IHubPingListener : IEventListener
{
    void OnPing(HubPing e)
    {
    }
}
