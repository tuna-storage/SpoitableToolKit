using SpoitableToolKit.Abstractions.Tools;

namespace SpoitableToolKit.Tests.Fakes;

internal sealed class FakeExternalToolProvider : IExternalToolProvider
{
    public Task<ExternalTool?> GetAsync(string toolId, CancellationToken cancellationToken = default) =>
        Task.FromResult<ExternalTool?>(null);
}
