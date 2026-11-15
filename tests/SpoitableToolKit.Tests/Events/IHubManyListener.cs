using SpoitableToolKit.Abstractions.Events;

namespace SpoitableToolKit.Tests.Events;

internal interface IHubManyListener : IEventListener
{
    IEnumerable<string?>? Many(HubPing e) => null;
}
