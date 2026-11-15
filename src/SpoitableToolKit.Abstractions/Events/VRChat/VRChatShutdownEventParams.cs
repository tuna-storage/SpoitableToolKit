namespace SpoitableToolKit.Abstractions.Events.VRChat;

/// <summary>VRChat が終わった。作るのはホストだけ（項目はあとから足せるように、位置引数ではなく init で持つ）。</summary>
public sealed record VRChatShutdownEventParams
{
    /// <summary>ホストの見張りが気づいた時刻（終了から最大 2 秒ほど遅れる）。</summary>
    public required DateTimeOffset NoticedAt { get; init; }
}
