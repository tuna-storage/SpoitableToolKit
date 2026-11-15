namespace SpoitableToolKit.Modules.Hrs.Devices.Ble;

/// <summary>
/// 「まだ見つからない」を書く時。<b>探し始めてから <paramref name="first"/> 経ったら 1 回</b>、
/// その後は <paramref name="every"/> ごと。見つかったら数え直す。
/// <para>
/// 探している間ずっと黙っていると、「探していない」のか「見つからない」のかが分からない。
/// </para>
/// </summary>
internal sealed class SearchWatch(TimeSpan first, TimeSpan every)
{
    private DateTimeOffset? _since;
    private DateTimeOffset _next;

    /// <summary>見つかっていない。書く時なら <c>true</c>。</summary>
    public bool StillLooking(DateTimeOffset now)
    {
        if (_since is null)
        {
            _since = now;
            _next = now + first;
        }

        if (now < _next) return false;

        _next = now + every;
        return true;
    }

    /// <summary>探し始めてからの長さ。書く文に入れる。</summary>
    public TimeSpan Elapsed(DateTimeOffset now) => _since is { } since ? now - since : TimeSpan.Zero;

    public void Found() => _since = null;
}
