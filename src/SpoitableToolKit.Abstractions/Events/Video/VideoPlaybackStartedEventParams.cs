namespace SpoitableToolKit.Abstractions.Events.Video;

/// <summary>再生が始まった。作るのはホストだけ（項目はあとから足せるように、位置引数ではなく init で持つ）。</summary>
public sealed record VideoPlaybackStartedEventParams
{
    /// <summary>始まった動画。</summary>
    public required VideoPlaybackInfo Video { get; init; }
}
