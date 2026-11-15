namespace SpoitableToolKit.Abstractions.VrcLog;

public sealed record PlayerLeft : VrcLogEventParams
{
    public required string DisplayName { get; init; }

    public string? UserId { get; init; }
}
