namespace SpoitableToolKit.Abstractions.VrcLog;

/// <summary>動画の URL 解決に成功した（<c>URL '...' resolved to '...'</c>）。</summary>
public sealed record VideoResolved : VrcLogEventParams
{
    public required string Url { get; init; }

    public required string ResolvedUrl { get; init; }
}
