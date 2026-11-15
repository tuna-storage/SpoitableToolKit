namespace SpoitableToolKit.Abstractions.VrcLog;

/// <summary>
/// 動画の再生に失敗した。
/// <para>
/// 失敗の形は <c>[Video Playback]</c> ではなく <c>[AVProVideo]</c> 側に出る。
/// URL の解決自体は成功するので <c>[Video Playback]</c> だけを見ていると成功に見え、
/// 直リンクを開いた先で <c>Error: Loading failed.</c> になる（nsig / 403 の典型的な症状）。
/// VRChat はこのあと同じ動画を数回やり直すため、1 本の動画で複数回発火しうる。
/// </para>
/// </summary>
public sealed record VideoPlaybackFailed : VrcLogEventParams
{
    /// <summary>ワールドに貼られた元の URL。対応づけできなかった場合は <see cref="ResolvedUrl"/> と同じ。</summary>
    public required string Url { get; init; }

    /// <summary>実際に開こうとした直リンク。</summary>
    public string? ResolvedUrl { get; init; }

    /// <summary>プレイヤーが報告した理由（<c>Loading failed. ...</c> など）。</summary>
    public string? Reason { get; init; }
}
