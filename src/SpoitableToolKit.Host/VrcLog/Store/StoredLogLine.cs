using SpoitableToolKit.Abstractions.VrcLog;

namespace SpoitableToolKit.Host.VrcLog.Store;

internal sealed record StoredLogLine
{
    public required long Id { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required VrcLogLevel Level { get; init; }

    public string? Tag { get; init; }

    public required string Message { get; init; }
}
