namespace SpoitableToolKit.Abstractions.Events.Video;

/// <summary>再生に失敗した。作るのはホストだけ（項目はあとから足せるように、位置引数ではなく init で持つ）。</summary>
public sealed record VideoPlaybackFailedEventParams
{
    /// <summary>失敗した動画（URL・理由・何回目の試行か）。</summary>
    public required VideoPlaybackInfo Video { get; init; }

    /// <summary>同じ URL で 2 回目以降の失敗なら true（VRChat は同じ URL で 4〜5 回やり直す）。</summary>
    public required bool IsRepeatedFail { get; init; }
}
