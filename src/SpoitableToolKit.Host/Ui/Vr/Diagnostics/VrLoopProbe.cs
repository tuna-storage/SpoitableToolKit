using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SpoitableToolKit.Host.Ui.Vr.Diagnostics;

/// <summary>
/// 周が実際に何回回ったかと 1 周の重さを、繋ぐたびに 1 回だけログに出す。
/// 周はヘッドセットの速さで打つが、UI のスレッドが前の周を片付けていなければ打たない（<see cref="VrPacer"/>）ので、打った回数と回った回数がずれたら UI のスレッドが重い。
/// 繋いで最初の <see cref="SkipSeconds"/> 秒は数えない（browser を作るので重い）。そのあと <see cref="WindowSeconds"/> 秒。
/// 板に届いた絵の枚数も並べる — CEF は中身が変わったときしか描かないので、止まった画面なら 0 で正しい。速さを測るときは画面を動かしながら見る。
/// <c>SPOITABLE_VR_PROBE=1</c> を入れると、1 回で止めずに <see cref="WindowSeconds"/> 秒ごとに出し続ける。
/// </summary>
internal sealed class VrLoopProbe(ILogger logger, bool repeat = false)
{
    public const double SkipSeconds = 2;

    public const double WindowSeconds = 5;

    private long? _connectedAt;

    private bool _done;

    private int _ticks;

    private long _busy;

    private long _worst;

    private long _firstPaint;

    public void Note(bool running, long start, long end, long paints)
    {
        if (!running)
        {
            _connectedAt = null;
            _done = false;
            return;
        }

        if (_done) return;

        _connectedAt ??= start;

        var since = (start - _connectedAt.Value) / (double)Stopwatch.Frequency;

        if (since < SkipSeconds)
        {
            _ticks = 0;
            _busy = 0;
            _worst = 0;
            _firstPaint = paints;
            return;
        }

        _ticks++;
        _busy += end - start;
        _worst = Math.Max(_worst, end - start);

        if (since < SkipSeconds + WindowSeconds) return;

        var ms = 1000.0 / Stopwatch.Frequency;

        logger.LogInformation(
            "The VR loop came round {Rate:0.0} times a second (a tick took {Average:0.00} ms on average, {Worst:0.0} ms at worst); " +
            "the dashboard picture changed {Paints:0.0} times a second",
            _ticks / WindowSeconds, _busy * ms / _ticks, _worst * ms, (paints - _firstPaint) / WindowSeconds);

        if (repeat)
        {
            _connectedAt = start - (long)(SkipSeconds * Stopwatch.Frequency);
            _ticks = 0;
            _busy = 0;
            _worst = 0;
            _firstPaint = paints;
            return;
        }

        _done = true;
    }
}
