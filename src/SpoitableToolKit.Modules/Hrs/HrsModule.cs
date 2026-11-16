using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.Events.Config;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Modules.Hrs.Avatar;
using SpoitableToolKit.Modules.Hrs.Broadcast;
using SpoitableToolKit.Modules.Hrs.Capture;
using SpoitableToolKit.Modules.Hrs.Config;
using SpoitableToolKit.Modules.Hrs.Devices;
using SpoitableToolKit.Modules.Hrs.Devices.Ble;
using SpoitableToolKit.Modules.Hrs.Devices.Hds;
using SpoitableToolKit.Modules.Hrs.Devices.Interfaces;
using SpoitableToolKit.Modules.Hrs.Devices.Models;
using SpoitableToolKit.Modules.Hrs.Events.Broadcast;
using SpoitableToolKit.Modules.Hrs.Events.HeartRate;
using SpoitableToolKit.Modules.Hrs.Events.Session;
using SpoitableToolKit.Modules.Hrs.Panel;
using SpoitableToolKit.Modules.Hrs.Recording;

namespace SpoitableToolKit.Modules.Hrs;

/// <summary>
/// BLE 心拍 → OSC。
/// <para>
/// 構成は 3 段。<b>プラットフォームに触るのは真ん中だけ</b>で、前後は実機無しで試験できる。
/// </para>
/// <code>
/// BLE の広告と通知   → IHeartRateSource   … WinRtHeartRateSource（ここだけ Windows 依存）
/// 心拍 1 つ          → HeartRateSession   … ビートの周期とパラメータの計算
/// avatar parameter   → IOscBus            … ホストの Core サービス
/// </code>
/// <para>
/// パネルは<b>見るだけ</b>。繋ぎ先を決めるのは設定画面で、こちらは「いまどうなっているか」を出す。
/// 正となる状態は全部 C# 側にあり、パネルは開いたときに取り直す。
/// </para>
/// <para>
/// ここが持つのは<b>組み立てと畳み・出来事の受け口・パネルの入口・設定の変更</b>。ほかは係に渡す:
/// 受ける周回（<see cref="HeartRateCapture"/>）・配信の待ち受け（<see cref="HrsBroadcastRunner"/>）・繋がりの記録（<see cref="HeartRateConnectionWatch"/>）・
/// 1 拍の記録（<see cref="HeartBeatRecorder"/>）・記録を読む（<see cref="HeartRateRecordQueryService"/>）・CSV（<see cref="HeartRateExportService"/>）・
/// 札と配信の数字（<see cref="HeartRateAnnouncer"/>）・ファイアウォール（<see cref="HdsFirewallWatch"/>）・パネルの形（<see cref="HrsPanelStatus"/>）。
/// </para>
/// </summary>
public sealed class HrsModule : ISpoitableModule, IConfigEventListener<HrsConfig>, IUiEndpoint
{
    private readonly HrsConfig _config = new();

    private readonly ModuleStatus _status = new();

    private readonly HeartRateLog _log = new();

    /// <summary>畳むときに後片付けを待つ上限。BLE は返ってこないことがある。</summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);

    private HeartRateManager? _source;

    private IEventHub? _sessionEvents;
    private ILogger _logger = NullLogger.Instance;
    private IModuleHost? _host;

    /// <summary>
    /// 心拍の記録。<b>無効でも開いておく</b> — 設定はいつでも変わるし、
    /// パネルは「どれだけ貯まっているか」を出す（無効にした瞬間に過去が消えるわけではない）。
    /// </summary>
    private HeartRateRecords? _records;

    /// <summary>
    /// 配信。<b>心拍の入口とは寿命を分ける</b> — デバイスを変えるたびに切っていると、
    /// OBS のブラウザソースが真っ黒になって貼り直す羽目になる。
    /// </summary>
    private HrsBroadcastServer? _broadcast;

    private readonly HdsFirewallWatch _firewall;
    private readonly HeartRateRecordQueryService _recordQuery;
    private readonly HeartRateExportService _export;
    private readonly HeartRateAnnouncer _announcer;
    private readonly HeartBeatRecorder _beats;
    private readonly HrsPanelStatus _panel;
    private readonly HeartRateConnectionWatch _connections;
    private readonly HrsBroadcastRunner _broadcastRunner;
    private readonly HeartRateCapture _capture;

    public HrsModule()
    {
        // 係は最初に作る。相手（窓口・配信・記録・host・logger）は読むときに引く — 立ち上がる途中（窓口や配信が出来る前）や
        // 畳んだあとにも、裏の仕事（ファイアウォール・数え直し・書き出し）から push が来る。試験の口（CheckFirewall など）も呼ぶときに読む。
        _firewall = new HdsFirewallWatch(_config,
            port => CheckFirewall?.Invoke() ?? HdsFirewall.Check(port, _logger),
            port => AddFirewall?.Invoke() ?? HdsFirewall.Fix(port, _logger),
            PushStatus);
        _recordQuery = new HeartRateRecordQueryService(() => _records, () => _logger, PushStatus);
        _export = new HeartRateExportService(() => _records, () => _host, () => _logger, _recordQuery, PushStatus);
        _announcer = new HeartRateAnnouncer(_status, _log, _config, () => _host, () => _broadcast, () => _source);
        _beats = new HeartBeatRecorder(_log, _config, () => _records, () => _source, () => _logger, _recordQuery, _announcer, PushStatus);
        _capture = new HeartRateCapture(this, () => _host, () => _source, () => _sessionEvents, _config, () => _logger, _beats, PushStatus);
        _panel = new HrsPanelStatus(_config, _log, () => _host, () => _source, () => _capture.Session, () => _broadcast, _firewall, _recordQuery, _export);
        _connections = new HeartRateConnectionWatch(() => _source, _log, _announcer, () => _logger);
        _broadcastRunner = new HrsBroadcastRunner(() => _broadcast, () => _logger);
    }

    public string? Author => "fltuna";

    public string? Icon => "❤";

    public string? IconPath => HeartPath;

    internal const string HeartPath =
        "M19 14c1.5-1.5 3-3.2 3-5.5A5.5 5.5 0 0 0 16.5 3c-1.8 0-3 .5-4.5 2-1.5-1.5-2.7-2-4.5-2A5.5 5.5 0 0 0 2 8.5c0 2.3 1.5 4 3 5.5l7 7Z";

    public IModuleConfig? Config => _config;

    public bool OnInit(IModuleHost host)
    {
        _host = host;
        _logger = host.Logger;

        // 画面まわりの登録（OnInit の中だけ）: サイドバーのページ・そのページから呼ばれる口（このクラス）・札。
        host.Ui.RegisterPanel("index.html");
        host.Ui.RegisterEndpoint(this);
        host.Ui.RegisterStatus(_status);

        // 手首のショートカット。パネルを開くもの（本体が用意する。中身は「72 bpm」の字）のほかに、タイルの中に心拍と線を描くもの。
        host.Shortcuts.Register(new HrsLiveTile(host.Ui));

        // 設定の変更で繋ぎ直したい・値を確かめたいので listener を足す。外すのはホストの仕事。
        host.Config.AddListener<IConfigEventListener<HrsConfig>>(this, this);

        // サイドバーに出す初期値。**繋がる前から居る**ので、心拍計が無い人にも「まだ繋がっていない」が見える。
        // 押すとこの画面が開く（ホストが決める）。
        _status.Text = host.Text["hrs.status.off"];

        // 設定ファイルを直接書き換えられていたら、読めないパラメータ名をここで既定へ戻す
        // （設定画面から来るものは OnConfigChanging で断っている）。
        ParameterNames.Repair(_config, _logger);

        _config.Discovered = () => _source?.Discovered ?? [];

        // ファイアウォールは**アプリからは足さない**。初回の待ち受けで Windows 自身が聞く。
        // ここで見るのは「通れそうか」だけで、駄目そうならパネルがボタンを出す。
        _firewall.Refresh();

        _records = CreateRecords?.Invoke()
            ?? new HeartRateRecords(Path.Combine(host.DataDirectory, "records.db"), _logger);

        // 後から繋いだ相手には直近の心拍を渡す。心拍は 1 秒に 1 回しか来ないので、
        // 渡さないと OBS を開いた直後だけグラフが空になる。
        var broadcastEvents = host.Events.Create<IHrsBroadcastEventListener>(this);
        broadcastEvents.Source.AddListener<IHrsBroadcastEventListener>(this, new BroadcastEar(this));
        // オーバーレイには拍だけを渡す（切れていた所の 0 はパネルのグラフのためのもの）。
        _broadcast = new HrsBroadcastServer(_config, _logger, broadcastEvents, () => [.. _log.Recent.Where(s => s.Bpm > 0).Select(s => s.Bpm)]);

        // 心拍の窓口。繋がった・切れたは OnInit で 1 回だけ受ける（1 拍は周回ごとに session と記録が受ける — HeartRateCapture.Start）。
        _source = new HeartRateManager(
            [.. (CreateSources?.Invoke() ?? [new WinRtHeartRateSource(_config, _logger), new HdsHeartRateSource(_config, _logger)])
                .Select(source => new SourceAsDevice(source))],
            host.Events.Create<IHeartRateEventListener>(this),
            Time);
        _source.Listeners.AddListener<IHeartRateEventListener>(this, new ConnectionEar(this));

        _sessionEvents = host.Events.Create<IHeartRateSessionEventListener>(this);
        _sessionEvents.Source.AddListener<IHeartRateSessionEventListener>(this, new SessionEar(this));

        _broadcastRunner.Restart();
        _capture.Start();
        _recordQuery.Refresh();

        return true;
    }

    /// <summary>
    /// 心拍の入口の並び。既定は BLE と HDS（窓口が早い者勝ちで束ねる）で、
    /// <b>試験からはここを差し替える</b>（実機の BLE 無しで畳み方まで見るため。偽物も本物の窓口を通る）。
    /// </summary>
    internal Func<IReadOnlyList<IHeartRateSource>>? CreateSources;

    /// <summary>
    /// 窓口の時計（黙ったのを数える）。<b>試験からはここを差し替える</b> — 時間を進めて「心拍なし」を起こす（本物の秒数を待たない）。
    /// </summary>
    internal TimeProvider? Time;

    /// <summary>
    /// 記録の置き場。<b>試験からはここを差し替える</b>（本物は DataDirectory に DB を作る）。
    /// </summary>
    internal Func<HeartRateRecords>? CreateRecords;

    /// <summary>
    /// ファイアウォールを見る係 / 足す係。<b>試験からはここを差し替える</b> —
    /// 本物は netsh を起こすし、足す方は管理者へ上げるので試験機に UAC が出てしまう。
    /// </summary>
    internal Func<HdsFirewall.State>? CheckFirewall;

    internal Func<bool>? AddFirewall;

    /// <summary>
    /// 繋がった（窓口から）。<b>ここからは配信へ流さない。</b> 配信の <c>connected</c> は「心拍が届いているか」で、
    /// それを知っているのはセッション（<see cref="HeartRateSession.Stopped"/> と次の 1 拍）。
    /// 繋がった時点で流すと、<b>まだ 1 拍も来ていないのに「届いている」と言う</b>ことになり、
    /// 直後にセッションが取り消して、オーバーレイが一瞬だけ点いて消える。
    /// 心拍計の名前は次の 1 拍と一緒に届く。
    /// </summary>
    private void OnConnected()
    {
        _connections.Note();
        PushStatus();
    }

    /// <summary>
    /// 切れた（窓口から）。<b>無信号の秒数を待たずに倒す</b> — 待つと、外れたベルトの最後の心拍をアバターが数秒〜数十秒出したままになる
    /// （時計は 60 秒待つので、待たせると特に長い）。<b>乗り換えのときはアバターへ「繋がっていない」を送らない</b>（すぐ次が繋がる）。
    /// </summary>
    private void OnDisconnected(HeartRateDisconnectReason reason)
    {
        if (reason != HeartRateDisconnectReason.Replaced)
        {
            _capture.Session?.Lost();
            _log.Gap(DateTimeOffset.Now);
        }

        _connections.Note();
        PushStatus();
    }

    /// <summary>
    /// 繋がったまま心拍が来なくなった（窓口から）。<b>session へはここから渡す</b>（直に受け手にしない）— この受け手は session より先に足してあるので、
    /// 直に受けさせると、パネルへ送るときに session がまだ前の心拍を持っている（黙っている間は次に送る機会が無い）。
    /// </summary>
    private void OnSignalLost(HeartRateSignalLostEventParams e)
    {
        _capture.Session?.SignalLost(e.SilentFor);
        _log.Gap(DateTimeOffset.Now);
        PushStatus();
    }

    /// <summary>
    /// 繋がった・切れた・心拍なしを受ける（パネル・繋がりの記録・アバターへの「繋がっていない」「動いていない」）。OnInit で 1 回足す。
    /// 心拍が戻った（OnSignalBack）は受けない — すぐ次の 1 拍で記録がパネルへ送る。
    /// </summary>
    private sealed class ConnectionEar(HrsModule module) : IHeartRateEventListener
    {
        public void OnConnected(HeartRateConnectedEventParams e) => module.OnConnected();

        public void OnDisconnected(HeartRateDisconnectedEventParams e) => module.OnDisconnected(e.Reason);

        public void OnSignalLost(HeartRateSignalLostEventParams e) => module.OnSignalLost(e);
    }

    private sealed class SessionEar(HrsModule module) : IHeartRateSessionEventListener
    {
        public void OnStopped(HeartRateSessionStoppedEventParams e) => module.OnSessionStopped();
    }

    private sealed class BroadcastEar(HrsModule module) : IHrsBroadcastEventListener
    {
        public void OnStatusChanged(HrsBroadcastStatusEventParams e) => module.PushStatus();
    }

    private void OnSessionStopped() => _announcer.Broadcast(0, connected: false);

    private void PushStatus() => _panel.Push();

    public Task<string> HandleUiCallAsync(string method, string payload, CancellationToken cancellationToken = default) =>
        method switch
        {
            // パネルを開いた直後。push を取りこぼしていても、これで現状に追いつける。
            "getStatus" => _panel.GetAsync(),

            // 手首のタイル（tile.html）が開いた直後。**記録は数えない** — タイルはページを送るたびに開き直すので、DB を数えるには多すぎる。
            // 形は push（status）の一部と同じ（タイルはどちらも同じ描き方で受ける）。
            "getHeartRate" => Task.FromResult(_panel.Tile()),

            // 「心拍計」タブを開いている間、数秒おきに読み直す一覧。**記録は数えない**（getStatus は数える）。
            "getMonitors" => Task.FromResult(_panel.Monitors()),

            "exportCsv" => _export.ExportCsv(payload),

            // 統計タブの 1 日ぶん。範囲は {"from":…,"to":…}（UTC のエポックミリ秒、両端を含む）。
            "readRecords" => _recordQuery.ReadAsync(payload),

            // 手で切る / 繋ぎ直す。**繋がらないときに何が起きるかを見るのに要る** —
            // 他のアプリに GATT を掴ませて広告へ落ちるか、といった確かめ方ができる。
            "connect" => _capture.Connect(),
            "disconnect" => _capture.Disconnect(),

            // 見当たらないと言われたルールを足す。**押した人が居るので UAC が出てよい。**
            "addFirewallRule" => _firewall.AddRule(),
            _ => throw new ArgumentException($"Unknown method: {method}", nameof(method)),
        };

    /// <summary>
    /// 変えてよいかを聞かれた。デバイスの指定・パラメータ名・帯の順番が読めなければ<b>その値を断る</b>（ほかの値は入る）—
    /// どれも手元で分かる条件なので、繋ぎに行かずに答えられる。
    /// <para>
    /// <b>読むのは <c>e.Proposed</c></b>（入ったらこうなる写し）。自分の設定（<c>_config</c>）はまだ前の値なので、そちらを読むと何も断れない。
    /// 変わっていない値を断っても何も起きないので、変わったかどうかは見ずに確かめてよい。
    /// </para>
    /// </summary>
    public EventResult OnConfigChanging(ConfigChangingEventParams<HrsConfig> e)
    {
        var text = _host?.Text;
        var proposed = e.Proposed;

        if (!DeviceSelector.TryParse(proposed.Device, out _, out var unreadable))
            e.Reject(c => c.Device, text?.Format("hrs.error.badAddress", unreadable) ?? $"hrs.error.badAddress: {unreadable}");

        foreach (var (setting, labelKey, reasonKey) in ParameterNames.ErrorsIn(proposed))
            e.Reject(setting, text?.Format("hrs.error.paramName", text[labelKey], text[reasonKey]) ?? $"{labelKey}: {reasonKey}");

        // 帯の順番が崩れると上の帯へ二度と入れない。**関わる値を全部断る**（入れ物ごと）— 1 つだけ断ると、変わったほかの帯の値が入って順番が崩れる。
        if (HrsZones.ErrorIn(proposed) is { } zone)
            e.Reject(c => c.Zones, text?[zone] ?? zone);

        return EventResult.Continue;
    }

    /// <summary>
    /// 設定が変わった（入った値だけ。保存も済んでいる）。入口・待ち受け・配信のうち、変わったものだけを作り直す。
    /// </summary>
    public void OnConfigChanged(ConfigChangedEventParams<HrsConfig> e)
    {
        // 何を変えたかを残す。値は書かない —
        // パラメータ名やポートは見せてよいが、どれが見せてよいかをここで選び分けるほどの得が無い。
        _logger.LogInformation("Heart rate settings changed: {Members}", string.Join(", ", e.Changes.Select(c => c.Name)));

        // HDS を有効にした / ポートを変えたときだけ、ファイアウォールを聞き直す。
        // 断られたあとでも、人が「有効にした」ならもう一度聞いてよい。
        var hds = e.Changed(c => c.Hds.Enabled) || e.Changed(c => c.Hds.Port);

        if (hds) _firewall.Refresh();

        // 配信は**待ち受けの条件が変わったときだけ**開き直す。帯のしきい値や記録の ON/OFF は
        // 次に流す 1 通から効くので、繋がっている OBS を切る理由が無い。
        if (e.Changed(c => c.Broadcast.Enabled) || e.Changed(c => c.Broadcast.Port))
            _broadcastRunner.Restart();

        // 繋ぎ直すのは<b>入口そのものが変わったときだけ</b>。パラメータ名も間隔も無信号の秒数も
        // ループが設定から読み直すので、名前を 1 つ直すたびに心拍計が切れるのは困る。
        if (e.Changed(c => c.Device))
        {
            // 前のデバイスのグラフは畳む。後ろに繋げても意味が無い。
            _log.Clear();

            // 選び直したので、手で切った印は全部外す（残すと、新しい指定の心拍計が離れたときに「前に切った 1 台は切断してあります」と出て、
            // 探している最中の「切断」も出なくなる）。
            if (_source?.Reconnect() is true) _logger.LogInformation("Heart rate source: the monitor setting changed; forgetting the ones disconnected by hand");

            _capture.Start();
        }
        else if (hds)
        {
            // 待ち受けを開き直す。BLE 側も一緒に張り直すが、繋ぎ先は変わらないのでグラフは畳まない
            // （張り直す間は止まるので、グラフは一度 0 に落ちて、また上がる）。
            _capture.Start();
        }

        PushStatus();
    }

    /// <summary>
    /// 畳む。<b>ここでは待つ。</b>
    /// <para>
    /// 待たないと 2 つ困る。<b>GATT の解放が途中で終わる</b>（次の起動で繋がりにくくなる）のと、
    /// セッションが最後に送る「繋がっていない」が<b>ホストが OSC を畳んだあとになる</b> —
    /// つまり間に合わず、アバターは最後に受け取った心拍を出したままになる。
    /// </para>
    /// <para>
    /// 上限を切っておくのは、BLE の後片付けが返ってこないことがあるため。
    /// 畳めなかったからといってアプリの終了を止めない。
    /// </para>
    /// </summary>
    public void OnShutdown()
    {
        if (!_capture.StopAsync().Wait(ShutdownTimeout))
            _logger.LogWarning("Could not shut down within {Timeout}s; exiting anyway", ShutdownTimeout.TotalSeconds);

        // 受け手（ConnectionEar など）はホストが外す（モジュールが畳まれたとき）。
        _source?.Dispose();

        _source = null;

        // 配信を先に畳む。ポートを離してから戻らないと、次の起動が同じポートを掴めない。
        if (!_broadcastRunner.StopAsync().Wait(ShutdownTimeout))
            _logger.LogWarning("Could not shut down broadcasting; exiting anyway");

        _broadcast = null;

        // **記録は残りを書いてから閉じる。** 最後の数拍を落とすと、記録の終わりが実際より早く見える。
        if (_records is { } records)
        {
            try
            {
                records.DisposeAsync().AsTask().Wait(ShutdownTimeout);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not shut down the recorder");
            }
        }

        _records = null;
    }
}
