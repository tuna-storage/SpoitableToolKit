namespace SpoitableToolKit.Abstractions.Events.Video;

/// <summary>1 本の動画についての、いま分かっていること。</summary>
public sealed record VideoPlaybackInfo
{
    /// <summary>ワールドに貼られた元の URL。モジュールが解決し直すならこれを使う。</summary>
    public required string Url { get; init; }

    /// <summary>VRChat が実際に開こうとした直リンク。対応づけできなかった場合は null。</summary>
    public string? ResolvedUrl { get; init; }

    /// <summary>プレイヤーが報告した理由。Unity バックエンドでは取れないので null になる。</summary>
    public string? Reason { get; init; }

    /// <summary>この URL について何回目の試行か（1 始まり）。</summary>
    public required int AttemptCount { get; init; }

    public required DateTimeOffset Timestamp { get; init; }
}
