using SpoitableToolKit.Abstractions.Events;

namespace SpoitableToolKit.Tests.Events;

internal interface IHubPongListener : IEventListener
{
    void OnPong(HubPing e)
    {
    }
}
