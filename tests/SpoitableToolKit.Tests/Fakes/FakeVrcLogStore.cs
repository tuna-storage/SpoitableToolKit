using SpoitableToolKit.Abstractions.VrcLog;

namespace SpoitableToolKit.Tests.Fakes;

/// <summary>何も残っていない <see cref="IVrcLogStore"/>（読むと空）。</summary>
internal sealed class FakeVrcLogStore : IVrcLogStore
{
    public Task<IReadOnlyList<StoredLogEvent>> QueryRecentAsync(
        int limit, string? kind = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StoredLogEvent>>([]);
}
