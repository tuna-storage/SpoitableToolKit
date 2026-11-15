namespace SpoitableToolKit.Abstractions.VrcLog;

public sealed record PlayerJoined : VrcLogEventParams
{
    public required string DisplayName { get; init; }

    /// <summary>VRChat のバージョンによっては行に含まれないので null になりうる。</summary>
    public string? UserId { get; init; }
}
