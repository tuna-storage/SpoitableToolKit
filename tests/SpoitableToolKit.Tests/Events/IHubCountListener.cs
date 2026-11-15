using SpoitableToolKit.Abstractions.Events;

namespace SpoitableToolKit.Tests.Events;

internal interface IHubCountListener : IEventListener
{
    string? Count(HubPing e) => null;
}
