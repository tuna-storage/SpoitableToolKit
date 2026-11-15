namespace SpoitableToolKit.Abstractions.Events.VRChat;

/// <summary>VRChat が起動した。作るのはホストだけ（項目はあとから足せるように、位置引数ではなく init で持つ）。</summary>
public sealed record VRChatStartedEventParams
{
    /// <summary>ホストの見張りが気づいた時刻（起動から最大 2 秒ほど遅れる）。</summary>
    public required DateTimeOffset NoticedAt { get; init; }
}
