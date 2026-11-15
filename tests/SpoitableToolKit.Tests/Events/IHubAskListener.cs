using SpoitableToolKit.Abstractions.Events;

namespace SpoitableToolKit.Tests.Events;

internal interface IHubAskListener : IEventListener
{
    EventResult OnAsk(HubPing e) => EventResult.Continue;
}
