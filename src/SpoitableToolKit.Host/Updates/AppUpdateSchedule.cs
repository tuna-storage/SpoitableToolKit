namespace SpoitableToolKit.Host.Updates;

internal sealed class AppUpdateSchedule(TimeProvider clock, TimeSpan firstDelay)
{
    /// <summary>起動の邪魔をしないよう、少し経ってから。</summary>
    public static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(20);

    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <summary>e2e の印: 最初に確かめるまでの秒数（待たずに確かめさせる）。</summary>
    public const string FirstDelayVariable = "SPOITABLE_UPDATE_FIRST_CHECK_SECONDS";

    private readonly Lock _gate = new();
    private readonly DateTimeOffset _started = clock.GetUtcNow();
    private DateTimeOffset? _last;

    public TimeProvider Clock => clock;

    public DateTimeOffset NextAt
    {
        get
        {
            lock (_gate) return _last is { } last ? last + Interval : _started + firstDelay;
        }
    }

    public TimeSpan Wait
    {
        get
        {
            var left = NextAt - clock.GetUtcNow();
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public void Looked()
    {
        var now = clock.GetUtcNow();
        lock (_gate) _last = now;
    }

    public static TimeSpan FirstDelayFrom(string? seconds) =>
        int.TryParse(seconds, out var value) && value >= 0 ? TimeSpan.FromSeconds(value) : FirstDelay;
}
