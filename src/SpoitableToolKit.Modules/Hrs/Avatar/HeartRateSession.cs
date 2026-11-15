using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.Osc;
using SpoitableToolKit.Modules.Hrs.Avatar.Presets;
using SpoitableToolKit.Modules.Hrs.Config;
using SpoitableToolKit.Modules.Hrs.Events.HeartRate;
using SpoitableToolKit.Modules.Hrs.Events.Session;

namespace SpoitableToolKit.Modules.Hrs.Avatar;

/// <summary>
/// 心拍を OSC のパラメータに翻訳して送り続ける。時間の進み方以外に外の世界へ触らないので、実機の BLE 無しで試験できる。
/// <para>
/// 心拍が来ている間は <see cref="SendInterval"/> ごとに、いま選ばれているプリセットの全部の名前へ送る（<see cref="IHrsOscSender"/>）。
/// UDP は届いた保証が無いので、変わったものだけ送ると取りこぼしたまま固まる。
/// </para>
/// <para>
/// 拍は <c>60000/HR</c> ms ごとに立てて、<see cref="BeatPulse"/> だけ立てたまま送る。拍の立ち上がりと立ち下がりの瞬間にも送るので、
/// 拍の長さは 50ms の刻みに丸められない。
/// </para>
/// <para>
/// 拍・拍の数・直近 10 秒の平均・なめらかに寄せた値を作るのはここ。どのプリセットにも同じ <see cref="HeartRateInfo"/> を渡す。
/// </para>
/// </summary>
internal sealed class HeartRateSession(
    IOscBus osc,
    HrsConfig config,
    IEventHub events,
    TimeProvider? time = null,
    ILogger? logger = null) : IHeartRateEventListener
{
    public static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// ビートを立てておく長さ。短すぎるとアバター側のアニメーションが拾えない。
    /// <b>1 周期の半分より長くはしない</b> — 速い心拍（600 bpm なら周期が 100ms）で立ちっぱなしになり、拍が見えなくなる。
    /// </summary>
    private static readonly TimeSpan BeatPulse = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan Idle = TimeSpan.FromMilliseconds(500);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private IHrsOscSender _sender = HrsOscSenders.For(config);

    private readonly HeartRateAverage _average = new();

    private int _heartRate;
    private bool _reportedLost;

    private long _beats;

    /// <summary>次の拍を立てる時刻。止まっている間と、届き始めの 1 手目までは無い（1 手目ですぐ立てる）。</summary>
    private DateTimeOffset? _nextBeat;

    private DateTimeOffset _beatUntil = DateTimeOffset.MinValue;

    private float? _shownBpm;
    private float? _shownAverage;

    /// <summary>前の 1 手の時刻。なめらかに寄せる量は、手と手の間の時間で決める（拍の瞬間にも起きるので、間は一定ではない）。</summary>
    private DateTimeOffset? _lastStep;

    public int HeartRate => Volatile.Read(ref _heartRate);

    void IHeartRateEventListener.OnHeartBeat(HeartBeatEventParams e) => Report(e.Bpm);

    /// <summary>
    /// アバターへ「動いていない」を送った、と配る。送った瞬間を知っているのはここだけなので、配信にも同じ瞬間を伝える
    /// （伝えないと、オーバーレイが最後の心拍を出したまま固まる）。1 回の無信号につき 1 回しか出ない。
    /// </summary>
    private void AnnounceStopped(bool closing)
    {
        var stopped = new HeartRateSessionStoppedEventParams(closing);
        events.Fire<IHeartRateSessionEventListener>(l => l.OnStopped(stopped));
    }

    /// <summary>心拍が届いた。<b>BLE のスレッドから呼ばれる。</b></summary>
    public void Report(int heartRate)
    {
        _average.Add(_time.GetUtcNow(), heartRate);
        Volatile.Write(ref _heartRate, heartRate);
        _reportedLost = false;
    }

    /// <summary>繋がりが切れた。次の 1 手で「動いていない」を送る。</summary>
    public void Lost()
    {
        Volatile.Write(ref _heartRate, 0);
        _reportedLost = false;
    }

    /// <summary>
    /// 窓口が「繋がっているが心拍が来ない」と言った。黙ったのを数えるのは窓口だけで、ここでは数えない。
    /// 次の 1 手で「動いていない」を送る。0 が来ていた（計れていない）ときはモジュールが書いているので、ここでは書かない。
    /// </summary>
    public void SignalLost(TimeSpan silentFor)
    {
        if (HeartRate > 0)
            logger?.LogInformation("No heart rate for {Seconds}s; telling the avatar it stopped", silentFor.TotalSeconds);

        Lost();
    }

    /// <summary>次の 1 手。送るものを送って、次の手までの待ち時間を返す（待つのは呼び出し側なので、試験から時計を進めながら叩ける）。</summary>
    public TimeSpan Step()
    {
        RefreshSender();

        var now = _time.GetUtcNow();

        // 時計が戻った（手で直した・NTP の大きな補正）。拍の予定と平均の窓は未来の時刻のまま残り、
        // そのままだと戻った時間ぶん拍が立たない。取り直す（次の手ですぐ 1 拍立てる）。
        if (_lastStep is { } previous && now < previous)
        {
            _nextBeat = null;
            _beatUntil = DateTimeOffset.MinValue;
            _average.Clear();
        }

        var elapsed = _lastStep is { } last && now > last ? now - last : TimeSpan.Zero;
        _lastStep = now;

        var heartRate = HeartRate;

        // 0 は「計れていない」・切れた・黙った（窓口が知らせて 0 にする — SignalLost・Lost）。
        if (heartRate <= 0)
        {
            // 「動いていない」は 1 回だけ送る。毎回送っても中身は同じで、
            // 心拍が止まっている間ずっと UDP を出し続けることになる。
            if (!_reportedLost)
            {
                SendStopped();
                _reportedLost = true;
                AnnounceStopped(closing: false);
            }

            Reset();
            return Idle;
        }

        // 拍。1 手目はすぐ立てる。周期は立てた時点の心拍で決める。
        if (_nextBeat is not { } next || now >= next)
        {
            var cycle = TimeSpan.FromMilliseconds(60_000d / heartRate);

            // 次の拍は予定の時刻から数える。起きた時刻から数えると、待ちが遅れて起きた分（Windows で数 ms）が周期ごとに積もり、
            // 72 bpm が 71 bpm 相当になる。半周期より遅れたとき（スリープ明けなど）は、今から数え直す（取り返そうとして拍を詰めない）。
            var from = _nextBeat is { } planned && now - planned < cycle / 2 ? planned : now;

            _beats++;
            _beatUntil = now + (cycle / 2 < BeatPulse ? cycle / 2 : BeatPulse);
            _nextBeat = from + cycle;
        }

        var beat = now < _beatUntil;

        var smooth = config.SmoothValues;
        _shownBpm = HeartRateSmoothing.Approach(_shownBpm, heartRate, elapsed, smooth);
        _shownAverage = HeartRateSmoothing.Approach(_shownAverage, _average.Of(now) ?? heartRate, elapsed, smooth);

        _sender.Send(osc, new HeartRateInfo(_shownBpm.Value, _shownAverage.Value, beat, _beats));

        // 次の手: 50ms 後か、拍を下ろす・立てる時刻の早い方。どちらも now より後なので、待ちは 0 にならない。
        var untilEdge = (beat ? _beatUntil : _nextBeat!.Value) - now;
        return untilEdge < SendInterval ? untilEdge : SendInterval;
    }

    /// <summary>
    /// 止まったら、拍と平均と寄せた値を全部捨てる。戻ったあとに、止まる前のものを混ぜない。
    /// <para>
    /// 拍の数を戻すのは、「拍ごとに入れ替える」形で戻った 1 拍目が見えなくなるのを防ぐため（止まったときに false にそろえているので、
    /// 続きから数えると false のまま変わらない）。寄せた値を捨てるのは、戻った 1 つ目を 0 から寄せないため。
    /// </para>
    /// </summary>
    private void Reset()
    {
        _beats = 0;
        _nextBeat = null;
        _beatUntil = DateTimeOffset.MinValue;
        _shownBpm = null;
        _shownAverage = null;
        _average.Clear();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var wait = Step();

            try
            {
                await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // 畳むときは「繋がっていない」を残す。送らずに消えると、
        // アバターは最後に受け取った心拍を出したままになる。
        SendStopped();
        AnnounceStopped(closing: true);
    }

    /// <summary>
    /// 設定でパラメータ名かプリセットが変わっていたら差し替える。
    /// <para>
    /// <b>前の名前へは「止まった」を 1 回送ってから替える。</b> 送らずに替えると、前の名前（前のプリセット）は
    /// 最後に受けた心拍を出したまま残る。<b>新しい組にも残る名前へは送らない</b>（<see cref="OscBusExcept"/>）—
    /// 名前を 1 つ変えただけで、ほかの 8 個が一瞬 0 と false になる。
    /// </para>
    /// <para>
    /// <b>止まっている間なら、新しい名前へもここで「止まった」を送る。</b> 送らないと、心拍が戻るまで新しい名前へ 1 通も届かず、
    /// アバターは繋がっていないことすら分からない。ラッチは掛けたままにする — 外すと次の手でもう一度配り、配信に同じ「止まった」が 2 回出る。
    /// </para>
    /// </summary>
    private void RefreshSender()
    {
        var sender = HrsOscSenders.For(config);
        if (sender.Equals(_sender)) return;

        _sender.Send(new OscBusExcept(osc, sender.Addresses), HeartRateInfo.Stopped);
        _sender = sender;

        if (_reportedLost) SendStopped();
    }

    private void SendStopped() => _sender.Send(osc, HeartRateInfo.Stopped);
}
