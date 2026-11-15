namespace SpoitableToolKit.Abstractions.VrcLog;

/// <summary>
/// 動画の再生が実際に始まった（<c>[AVProVideo] Using playback path: ...</c>）。
/// <b>AVPro バックエンドでしか出ない。</b> Unity Video Player は成功時も何も書かない。
/// </summary>
public sealed record VideoPlaybackStarted : VrcLogEventParams
{
    public required string Url { get; init; }

    public string? ResolvedUrl { get; init; }

    /// <summary><c>MF-MediaEngine-Hardware</c> など、実際に使われた再生パス。</summary>
    public string? PlaybackPath { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }
}
