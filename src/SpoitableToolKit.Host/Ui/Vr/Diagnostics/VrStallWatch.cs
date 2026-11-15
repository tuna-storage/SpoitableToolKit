namespace SpoitableToolKit.Host.Ui.Vr.Diagnostics;

/// <summary>
/// VR の周（<see cref="VrHost.Tick"/>）が回らなくなったら、どこで止まっていたかを残す。
/// SteamVR が閉じるときに送る Quit に 5 秒応えないと SteamVR に殺される（vrserver のログ: <c>Kill process ... because it didn't quit in time</c>）が、どこで止まっていたかはどのログにも残らないため。
/// 周を回す側（UI のスレッド）が <see cref="Enter"/> で「いま何の中か」を書き、1 周終えるたびに <see cref="Beat"/> する。
/// 見張るのは別のスレッド（<see cref="Check"/>）— 止まっているのが UI のスレッドなら、そこでは見張れない。
/// 時計は外から渡す（時間を待たずに試験するため）。
/// </summary>
/// </summary>
internal sealed class VrStallWatch
{
    /// <summary>周と周の間。これのまま止まっていたら、VR の外で UI のスレッドが塞がっている。</summary>
    public const string BetweenTicks = "between ticks (the UI thread is somewhere else)";

    public const string InsideTick = "inside the tick";

    private readonly Func<long> _clock;

    private readonly long _limitMs;

    private readonly Lock _gate = new();

    private long _beat;

    private string _where = BetweenTicks;

    /// <summary>止まったと報せた周の拍。0 は「報せていない」。</summary>
    private long _reportedBeat;

    private bool _reported;

    /// <param name="limitMs">これだけ拍が無ければ止まったとみなす。周の間隔（繋がっていない間は 2 秒）より長く、SteamVR が待つ 5 秒より短くする — 殺される前に残したい。</param>
    public VrStallWatch(Func<long> clock, long limitMs)
    {
        _clock = clock;
        _limitMs = limitMs;
        _beat = clock();
    }

    public string Where => Volatile.Read(ref _where);

    /// <summary>周を回す側から呼ぶ。</summary>
    public void Enter(string where) => Volatile.Write(ref _where, where);

    /// <summary><paramref name="where"/> の中で <paramref name="call"/> を呼ぶ。抜けたら <see cref="InsideTick"/> に戻す — 戻さないと、とっくに抜けた呼び出しの名前で止まっていたことにされる。</summary>
    public T Inside<T>(string where, Func<T> call)
    {
        Enter(where);
        try
        {
            return call();
        }
        finally
        {
            Enter(InsideTick);
        }
    }

    /// <inheritdoc cref="Inside{T}(string, Func{T})"/>
    public void Inside(string where, Action call)
    {
        Enter(where);
        try
        {
            call();
        }
        finally
        {
            Enter(InsideTick);
        }
    }

    public void Beat()
    {
        Volatile.Write(ref _beat, _clock());
        Enter(BetweenTicks);
    }

    /// <summary>止まった最初の 1 回と、また回り出した 1 回だけ報せる（それ以外は <c>null</c>）。毎回報せると、その行がアプリログの欄へ流れて再描画を起こす（自分で立てた波を測ることになる）。</summary>
    /// </summary>
    public VrStall? Check()
    {
        lock (_gate)
        {
            var beat = Volatile.Read(ref _beat);
            var now = _clock();

            if (_reported)
            {
                if (beat == _reportedBeat) return null;

                _reported = false;
                return new VrStall(Stuck: false, TimeSpan.FromMilliseconds(beat - _reportedBeat), Where);
            }

            if (now - beat < _limitMs) return null;

            _reported = true;
            _reportedBeat = beat;
            return new VrStall(Stuck: true, TimeSpan.FromMilliseconds(now - beat), Where);
        }
    }
}
