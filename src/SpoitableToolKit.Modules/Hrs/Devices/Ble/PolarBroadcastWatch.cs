namespace SpoitableToolKit.Modules.Hrs.Devices.Ble;

/// <summary>
/// 広告の見張り。<b>続けて <see cref="UnsupportedAfter"/> 通読めなかったら</b>
/// 「対応していない形かもしれない」と書く。1 通でも読めたら数え直す。
/// </summary>
internal sealed class PolarBroadcastWatch
{
    public const int UnsupportedAfter = 5;

    private bool _sawFirst;
    private int _misses;
    private bool _warned;

    public BroadcastNote Feed(bool read)
    {
        var first = !_sawFirst;
        _sawFirst = true;

        if (read) _misses = 0;
        else _misses++;

        if (first) return BroadcastNote.First;
        if (_warned || _misses < UnsupportedAfter) return BroadcastNote.None;

        _warned = true;
        return BroadcastNote.Unsupported;
    }

    /// <summary>見張りをやめた。次に広告へ回ったら、最初の 1 通からまた書く。</summary>
    public void Reset()
    {
        _sawFirst = false;
        _misses = 0;
        _warned = false;
    }
}
