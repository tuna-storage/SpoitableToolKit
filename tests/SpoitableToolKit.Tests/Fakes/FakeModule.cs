using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Events;

namespace SpoitableToolKit.Tests.Fakes;

internal sealed class FakeModule(string id) : ISpoitableModule
{
    public string Id { get; } = id;

    public string DisplayName => Id;

    public bool OnInit(IModuleHost host) => true;
}
