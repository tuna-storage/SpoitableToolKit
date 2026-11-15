namespace SpoitableToolKit.Abstractions.VrcLog;

/// <summary>
/// VRChat 本体の API 呼び出し。全件ログを有効にしていると 1 セッションで 2000 行を超える最大のタグ。
/// </summary>
public sealed record ApiRequest : VrcLogEventParams
{
    /// <summary><c>Get</c> / <c>Post</c> / <c>Put</c> / <c>Delete</c>。</summary>
    public required string Method { get; init; }

    /// <summary><c>users/usr_...</c> や <c>economy/purchases</c> のようなエンドポイント。</summary>
    public required string Endpoint { get; init; }
}
