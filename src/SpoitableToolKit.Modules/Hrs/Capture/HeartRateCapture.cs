using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Modules.Hrs.Avatar;
using SpoitableToolKit.Modules.Hrs.Config;
using SpoitableToolKit.Modules.Hrs.Devices;
using SpoitableToolKit.Modules.Hrs.Devices.Models;
using SpoitableToolKit.Modules.Hrs.Events.HeartRate;
using SpoitableToolKit.Modules.Hrs.Recording;

namespace SpoitableToolKit.Modules.Hrs.Capture;

/// <summary>
/// 心拍を受ける周回: 窓口（<see cref="HeartRateManager"/>）と session（<see cref="HeartRateSession"/>）を走らせる・止める・作り直す。
/// パネルの「接続」「切断」もここ（手で切る・止める・また受ける）。
/// </summary>
internal sealed class HeartRateCapture(
    ISpoitableModule owner,
    Func<IModuleHost?> host,
    Func<HeartRateManager?> source,
    Func<IEventHub?> sessionEvents,
    HrsConfig config,
    Func<ILogger> logger,
    HeartBeatRecorder beats,
    Action pushStatus)
{
    private CancellationTokenSource? _running;
    private Task _loop = Task.CompletedTask;

    public HeartRateSession? Session { get; private set; }

    /// <summary>1 拍を記録・配信・パネルへ。周回ごとに session の後に足し、周回の終わりに外す。</summary>
    private sealed class Recorder(HeartBeatRecorder beats) : IHeartRateEventListener
    {
        public void OnHeartBeat(HeartBeatEventParams e) => beats.Record(e.Bpm);
    }

    /// <summary>
    /// 動かし直す。前の周回の後片付けが終わってから次を始める
    /// （待たずに始めると、前の周回の finally が、新しく始めたスキャンを止めてしまう）。
    /// 待つのはここではなく起こした先で、この呼び出し自体はすぐ返る（OnInit も OnConfigChanged も UI のスレッドから来る）。
    /// </summary>
    public void Start()
    {
        var previous = StopAsync();

        if (host() is not { } moduleHost || source() is not { } hub || sessionEvents() is not { } events) return;
        if (!DeviceSelector.TryParse(config.Device, out var selector, out _)) return;

        var running = new CancellationTokenSource();
        _running = running;

        logger().LogInformation("Heart rate capture started (looking for {Target}; HDS {Hds})",
            selector.Describe(), config.Hds.Enabled ? $"on port {config.Hds.Port}" : "off");

        // 黙ったのを数えるのは窓口（入口ごとの長さで）。session は「心拍なし」を受けて送るだけ（OnSignalLost）。
        var session = new HeartRateSession(moduleHost.Osc, config, events, logger: logger());
        Session = session;

        // アバターへ「止まった」を送った瞬間を知っているのは session だけ。配信にも同じ瞬間を伝える（モジュールの SessionEar。OnInit で足してある）。
        // 伝えないと、オーバーレイが外れたベルトの最後の心拍を出したまま固まる。
        var recorder = new Recorder(beats);

        _loop = Task.Run(async () =>
        {
            await previous.ConfigureAwait(false);

            try
            {
                // 受け手を足すのも try の中で。前の周回が詰まってアプリが畳むのを待ち切れなかったとき、ここへ来たときには
                // モジュールの口が閉じていて投げる — 外で投げると周回が黙って落ち、session だけ足したままになる。
                hub.Listeners.AddListener<IHeartRateEventListener>(owner, session);
                hub.Listeners.AddListener<IHeartRateEventListener>(owner, recorder);

                await Task.WhenAll(
                    hub.RunAsync(selector, running.Token),
                    session.RunAsync(running.Token)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger().LogWarning(ex, "Heart rate capture stopped");
            }
            finally
            {
                hub.Listeners.RemoveListener<IHeartRateEventListener>(owner, session);
                hub.Listeners.RemoveListener<IHeartRateEventListener>(owner, recorder);
            }
        });
    }

    /// <summary>
    /// 止める。戻り値を待つと後片付けの完了まで見られる（GATT を外し、アバターへ「繋がっていない」を送るところまで）。
    /// <see cref="CancellationTokenSource"/> は周回が終わってから畳む（動いている最中に捨てると、その token を使っている
    /// <c>Task.Delay</c> が <c>ObjectDisposedException</c> で落ちる）。
    /// </summary>
    public Task StopAsync()
    {
        var running = _running;
        var loop = _loop;

        _running = null;
        _loop = Task.CompletedTask;
        Session = null;

        if (running is null) return loop;

        running.Cancel();
        return loop.ContinueWith(_ => running.Dispose(), TaskScheduler.Default);
    }

    /// <summary>
    /// パネルの「切断」。繋がっていれば、その心拍計だけを切る — 探すのと時計の待ち受けは続き、「接続」を押すまでその 1 台には繋ぎ直さない。
    /// アバターへは「動いていない」。何も繋がっていなければ（探している最中）、受けるのを止める（<see cref="Suspend"/>）。
    /// </summary>
    public Task<string> Disconnect()
    {
        if (source()?.Disconnect() is not { } monitor) return Suspend();

        logger().LogInformation("Heart rate source: disconnected {Device} from the panel; it stays off until Connect", monitor.ToSuggestion());
        pushStatus();

        return Task.FromResult("{}");
    }

    /// <summary>
    /// パネルの「接続」。手で切った心拍計があれば、また繋いでよいことにする（周回は作り直さない。止めてあれば受け始める）。
    /// 無ければ受け始める（<see cref="Resume"/>）。
    /// </summary>
    public Task<string> Connect()
    {
        if (source() is not { HeldOff: { } held } hub || !hub.Reconnect()) return Resume();

        logger().LogInformation("Heart rate source: {Device} may connect again (from the panel)", held.ToSuggestion());
        if (_running is null) Start();
        pushStatus();

        return Task.FromResult("{}");
    }

    private Task<string> Resume()
    {
        Start();
        pushStatus();

        return Task.FromResult("{}");
    }

    /// <summary>
    /// 止める。<b>畳むのと同じ道を通る</b>ので、アバターには「繋がっていない」が届く。
    /// 設定を変えるまで止まったままにはしない — 次に設定を触ると繋ぎ直る。
    /// </summary>
    private Task<string> Suspend()
    {
        logger().LogInformation("Heart rate capture stopped from the panel");
        _ = StopAsync();
        pushStatus();

        return Task.FromResult("{}");
    }
}
