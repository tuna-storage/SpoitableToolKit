namespace SpoitableToolKit.Modules.VideoUrl.Failures;

/// <summary>パネルに 1 行として出る、再生に失敗した動画。</summary>
public sealed record VideoFailure
{
    /// <summary>ワールドに貼られていた元の URL。解決し直すのはこちら（直リンクでは解決できない）。</summary>
    public required string Url { get; init; }

    /// <summary>プレイヤーが報告した理由。Unity バックエンドでは取れないので null になる。</summary>
    public string? Reason { get; init; }

    /// <summary>この一覧に載ってから数えた失敗回数。</summary>
    public required int Failures { get; init; }

    public required DateTimeOffset LastSeen { get; init; }
}
