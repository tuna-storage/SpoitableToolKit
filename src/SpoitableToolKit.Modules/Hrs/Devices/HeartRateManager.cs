using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Modules.Hrs.Devices.Interfaces;
using SpoitableToolKit.Modules.Hrs.Devices.Models;
using SpoitableToolKit.Modules.Hrs.Events.HeartRate;

namespace SpoitableToolKit.Modules.Hrs.Devices;

/// <summary>
/// 心拍の窓口。見つける係（<see cref="IHeartRateDeviceDiscovery"/>）が差し出す心拍計を並べて<b>早い者勝ち</b>で 1 本にまとめる。
/// 先に心拍を送ってきた心拍計が持ち主になり、黙るまで持ち主でいる。受け手へは型の付いた出来事で配る（<see cref="Listeners"/>・<see cref="IHeartRateEventListener"/>）。
/// <para>
/// こうしてあるので、設定を触らずにベルトから時計へ乗り換えられる。混ぜないのが肝で、
/// <b>2 つの入口の心拍を交互に流すと 1 本の線に別人が混ざる</b>（グラフも OSC も）。
/// </para>
/// <para>
/// 黙ったと判断する長さは<b>心拍計ごと</b>（<see cref="IHeartRateDevice.SilenceAfter"/>）。
/// BLE の 10 秒で時計を切ると、手首を下ろしただけで持ち主が入れ替わる。
/// </para>
/// <para>
/// <b>出来事の約束</b>（<see cref="IHeartRateEventListener"/>）: 繋がった・切れたは対で交互に、1 拍はその間にだけ。
/// 知らせのたびに「いまの繋がり」を出し、最後に知らせたものと比べて出す（同じなら出さない）。積むのは lock の中、配るのは lock の外（<see cref="HeartRateDispatch"/>）。
/// </para>
/// <para>
/// <b>黙ったのを数えるのはここだけ</b>（session は数えない）。黙った持ち主を手放しても同じ繋がりのままなら「心拍なし」
/// （<see cref="HeartRateSignalLostEventParams"/>）、繋がりごと消えたなら「切れた（黙った）」— 1 回の手放しで両方は出さない。
/// ページの「受信中」は <see cref="Current"/> の状態（<see cref="HeartRateDeviceStatus"/>）— <b>状態を持つのは窓口</b>。
/// </para>
/// </summary>
internal sealed class HeartRateManager : IHeartRateDeviceSink, IDisposable
{
    /// <summary>
    /// 持ち主が黙っていないか見に行く間隔。黙ったと知らせるのがこれだけ遅れる（アバターへの HR=0 も）ので、1 秒より細かくする。
    /// </summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);

    private readonly IHeartRateDeviceDiscovery[] _discoveries;

    /// <summary>
    /// 知っている心拍計（見つける係が 1 度でも知らせてきたもの）を、<b>知らせてきた順</b>に。書くのは <see cref="_gate"/> の中（差し替えるだけ）、読むのは外からもよい。
    /// </summary>
    private IHeartRateDevice[] _devices = [];
    private readonly TimeProvider _time;
    private readonly IEventHub _hub;
    private readonly HeartRateDispatch _dispatch;
    private readonly Lock _gate = new();

    private IHeartRateDevice? _owner;
    private DateTimeOffset _lastSample;

    /// <summary>最後に知らせた繋がり（無ければ null）。これと比べて、繋がった・切れたを出す。</summary>
    private (MonitorIdentity Monitor, HeartRateConnection Connection)? _announced;

    /// <summary>受けるのをやめている最中（<see cref="RunAsync"/> の取り消しのあと）。その間に切れたら理由は <see cref="HeartRateDisconnectReason.Shutdown"/>。</summary>
    private bool _stopping;

    /// <summary>
    /// 手で切った心拍計（パネルの「切断」）と、そのときの名乗り。「接続」（<see cref="Reconnect"/>）まで<b>繋がっていない扱い</b>。
    /// 書くのは <see cref="_gate"/> の中（差し替えるだけ）。読むのは外からもよい（<see cref="Active"/>）。覚えるのはメモリだけ（アプリを閉じれば忘れる）。
    /// </summary>
    private (IHeartRateDevice Device, MonitorIdentity Monitor)[] _heldOff = [];

    /// <summary>
    /// 「切断」「接続」を 1 本ずつ通す（心拍計へ伝えるところまで）。デスクトップと VR の窓の両方から押せるので、重なると窓口と心拍計の「切った」が
    /// 食い違ったまま残る（窓口は切っていないのに心拍計は繋ぎ直さない、など）。中で取るのは <see cref="_gate"/> と配る口だけ。
    /// </summary>
    private readonly Lock _holdGate = new();

    /// <summary>
    /// 繋がっている心拍計を<b>繋がった順</b>に（手で切った心拍計は入れない）。持ち主が居ないうちに出す 1 台は、この先頭 — <b>先に繋いだ方が勝ち</b>。
    /// 線が一度落ちた心拍計は抜け、戻ると後ろに並ぶ（戻っても、繋がったままの方から取り返さない — 行ったり来たりしない）。
    /// 書くのは <see cref="_gate"/> の中（差し替えるだけ）、読むのは外からもよい。
    /// </summary>
    private IHeartRateDevice[] _order = [];

    /// <summary>
    /// 最後に知らせた繋がり（<see cref="_announced"/>）の状態（<see cref="_gate"/> の中で読み書き）。繋がりが変わるたびに
    /// <see cref="HeartRateDeviceStatus.NoHeartRate"/>（まだ 1 拍も無い）へ戻す — 繋がりが無くなったときは <see cref="HeartRateDeviceStatus.Disconnected"/>。
    /// 1 拍で <see cref="HeartRateDeviceStatus.Connected"/>、黙ったら <see cref="HeartRateDeviceStatus.NoHeartRate"/>。
    /// <b>知らせた繋がりが無い間は読まない</b>（<see cref="Current"/> は null を返す — 止めたあとに戻さなくてよいのはこのため）。
    /// </summary>
    private HeartRateDeviceStatus _status = HeartRateDeviceStatus.Disconnected;

    /// <summary>この繋がりで「心拍なし」を知らせたか（<see cref="_gate"/> の中）。次の 1 拍の前に「戻った」を出すのは、知らせたあとだけ。</summary>
    private bool _lostAnnounced;

    public HeartRateManager(IReadOnlyList<IHeartRateDeviceDiscovery> discoveries, IEventHub hub, TimeProvider? time = null)
    {
        _discoveries = [.. discoveries];
        _time = time ?? TimeProvider.System;
        _hub = hub;
        _dispatch = new HeartRateDispatch(hub);
    }

    public IEventSource Listeners => _hub.Source;

    /// <summary>設定の候補（BLE のスキャンで見えているもの）。<see cref="SourceAsDevice"/> が持っている名簿を出す。</summary>
    public IReadOnlyList<MonitorIdentity> Discovered =>
        [.. Volatile.Read(ref _devices).OfType<SourceAsDevice>().SelectMany(device => device.Discovered)];

    /// <summary>パネルの「心拍計」タブの一覧（<see cref="Discovered"/> に、最後に見た様子を添えたもの）。</summary>
    public IReadOnlyList<MonitorSighting> Sightings =>
        [.. Volatile.Read(ref _devices).OfType<SourceAsDevice>().SelectMany(device => device.Sightings)];

    public MonitorIdentity? Connected => Active is { } active && active.Connection != HeartRateConnection.None ? active.Identity : null;

    public HeartRateConnection Connection => Active?.Connection ?? HeartRateConnection.None;

    public int? Battery => Active?.Battery;

    /// <summary>
    /// 心拍が届いているか — 繋がっていて、この繋がりで 1 拍届き、そのあと黙り切っていない。ページの「受信中」はこれだけで、
    /// 繋がっているのに false なら「接続済み・心拍なし」（充電器の上・繋がった直後でまだ 1 拍も無い）。
    /// </summary>
    public bool Receiving => Current?.Status == HeartRateDeviceStatus.Connected;

    /// <summary>
    /// いま使っている 1 台（最後に「繋がった」と知らせた心拍計）と、その状態（<see cref="HeartRateDeviceStatus.Connected"/> か <see cref="HeartRateDeviceStatus.NoHeartRate"/>）。
    /// 無ければ null。手で切った心拍計はここには出ない（<see cref="HeldOff"/>）— 切ってある間に別の心拍計が繋がることがある。
    /// </summary>
    public HeartRateDeviceState? Current
    {
        get
        {
            lock (_gate)
                return _announced is { } announced
                    ? new HeartRateDeviceState(announced.Monitor, announced.Connection, _status, Active?.Battery)
                    : null;
        }
    }

    public TimeSpan SilenceAfter => (Active ?? Volatile.Read(ref _devices).FirstOrDefault())?.SilenceAfter ?? TimeSpan.FromSeconds(10);

    /// <summary>
    /// 持ち主の繋ぎ先。持ち主が居ないうちは<b>先に繋がった入口</b>を出す（<see cref="_order"/>）—
    /// GATT が張れた直後はまだ 1 拍も来ていないが、繋がっていることは見せたい。手で切った心拍計（<see cref="_heldOff"/>）は、見つける係がまだ放していなくても数えない。
    /// 並びにまだ入っていない心拍計（繋がったが、まだ並べ直していない）は、並びの誰も繋がっていないときだけ、知っている順に見る。
    /// </summary>
    private IHeartRateDevice? Active
    {
        get
        {
            var owner = Volatile.Read(ref _owner);

            // 切った心拍計が持ち主のまま、はふつう起きない（Disconnect が持ち主を外し、Beat も捨てる）— 守りとして見る。
            if (owner is not null && !(owner.Connection != HeartRateConnection.None && IsHeld(owner.Identity))) return owner;

            foreach (var device in Volatile.Read(ref _order))
                if (device.Connection != HeartRateConnection.None && !IsHeld(device.Identity)) return device;

            foreach (var device in Volatile.Read(ref _devices))
                if (device.Connection != HeartRateConnection.None && !IsHeld(device.Identity)) return device;

            return null;
        }
    }

    /// <summary>
    /// 繋がった順（<see cref="_order"/>）を、いまの繋がりで並べ直す（<b>lock の中で呼ぶ</b>）。切れた・手で切った心拍計は抜き、新しく繋がった心拍計は後ろに足す
    /// （同時に見つけたものは知っている順）。
    /// </summary>
    private void RefreshOrderLocked()
    {
        var order = new List<IHeartRateDevice>(_devices.Length);

        foreach (var device in _order)
            if (device.Connection != HeartRateConnection.None && !IsHeld(device.Identity)) order.Add(device);

        foreach (var device in _devices)
            if (!order.Contains(device) && device.Connection != HeartRateConnection.None && !IsHeld(device.Identity)) order.Add(device);

        _order = [.. order];
    }

    /// <summary>知らない心拍計なら覚える（<b>lock の中で呼ぶ</b>）。並びは知らせてきた順。</summary>
    private void KnowLocked(IHeartRateDevice device)
    {
        if (Array.IndexOf(_devices, device) < 0) _devices = [.. _devices, device];
    }

    private bool IsHeld(MonitorIdentity monitor)
    {
        foreach (var held in Volatile.Read(ref _heldOff))
            if (held.Monitor.IsSame(monitor)) return true;

        return false;
    }

    /// <summary>
    /// 手で切ってある心拍計（最後に切ったもの）。無ければ null。パネルの「接続」の相手。続けて別の心拍計も切ると印は両方に付き、
    /// ここが出すのは最後に切ったもの・「接続」は全部を外す。
    /// </summary>
    public MonitorIdentity? HeldOff => Volatile.Read(ref _heldOff) is [.., var last] ? last.Monitor : null;

    /// <summary>
    /// 手で切る — <b>いま繋がっている 1 台だけ</b>。探すのと時計の待ち受けは続き、
    /// 「接続」（<see cref="Reconnect"/>）まで、その 1 台は繋がっていない扱い（遅れて届く 1 拍も捨てる）。ほかの入口が繋がっていれば、そちらを出す。
    /// 何も繋がっていなければ何もせず null を返す。
    /// </summary>
    public MonitorIdentity? Disconnect()
    {
        lock (_holdGate)
        {
            IHeartRateDevice device;
            MonitorIdentity monitor;

            lock (_gate)
            {
                if (Active is not { } active || active.Connection == HeartRateConnection.None) return null;

                device = active;
                monitor = active.Identity;
                _heldOff = [.. _heldOff, (active, monitor)];
                if (ReferenceEquals(_owner, active)) _owner = null;

                AnnounceLocked(HeartRateDisconnectReason.ByUser, beatFollows: false);
            }

            _dispatch.Drain();

            // 心拍計が線を放すのは、窓口が「切れた（手で）」と配ったあと。先に放させると、係の知らせや見張りが先に「線が落ちた」「黙った」と言う。
            device.Disconnect();
            return monitor;
        }
    }

    /// <summary>
    /// 手で切った心拍計を、また繋いでよいことにする（パネルの「接続」）。いまの繋がりを見直すので、係がまだ放していなければその場で「繋がった」が出る
    /// （心拍を運んでいる心拍計があればそちらのまま — 持ち主が先）。切っていなければ false。
    /// </summary>
    public bool Reconnect()
    {
        lock (_holdGate)
        {
            (IHeartRateDevice Device, MonitorIdentity Monitor)[] held;

            lock (_gate)
            {
                held = _heldOff;
                if (held.Length == 0) return false;

                _heldOff = [];
                AnnounceLocked(HeartRateDisconnectReason.LinkLost, beatFollows: false);
            }

            _dispatch.Drain();

            foreach (var (device, _) in held) device.Connect();
            return true;
        }
    }

    /// <summary>
    /// 受ける。見つける係を全部走らせ、持ち主が黙っていないか見張る。取り消されるまで戻らない。
    /// 戻る前に、繋がっていれば「切れた（<see cref="HeartRateDisconnectReason.Shutdown"/>）」を配る（口を閉じる前に届く）。
    /// </summary>
    public async Task RunAsync(DeviceSelector selector, CancellationToken cancellationToken)
    {
        lock (_gate) _stopping = false;

        using var stopping = cancellationToken.Register(() =>
        {
            lock (_gate) _stopping = true;
        });

        try
        {
            var running = _discoveries.Select(d => d.DiscoverAsync(selector, this, cancellationToken)).ToList();
            running.Add(WatchAsync(cancellationToken));

            await Task.WhenAll(running).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _stopping = true;
                _owner = null;
                if (_announced is { } was)
                {
                    _dispatch.Enqueue(new HeartRateDisconnectedEventParams(was.Monitor, was.Connection, HeartRateDisconnectReason.Shutdown));
                    _announced = null;
                }
            }

            _dispatch.Drain();
        }
    }

    /// <summary>
    /// 持ち主が黙ったら手放す。<b>手放す係が要る</b>のは、心拍計は「もう送らない」とは言わずに
    /// 黙るだけだから（時計を外す、ベルトが外れる）。手放して初めて別の入口が入れる。
    /// </summary>
    private async Task WatchAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Tick, _time, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            Release();
        }

        lock (_gate) _owner = null;
    }

    /// <summary>
    /// 持ち主が黙っていれば手放す。手放しても同じ繋がりのまま（GATT の線は上がっている・広告の受け方はまだ諦めていない）なら
    /// 「心拍なし」を出す。時計（HDS）は黙ると繋がりごと消えるので「切れた（黙った）」になり、心拍なしは出さない（混ぜない）。
    /// 試験から時間を進めて叩けるように分けてある。
    /// </summary>
    internal void Release()
    {
        lock (_gate)
        {
            // 繋がった順も見直す — 持ち主が心拍を運んでいる間に、時計（HDS）が知らせずに時刻で消えて、また届き始めることがある。
            // 見直さないと、消えていた心拍計が前の順のまま残る（戻ったなら後ろに並ぶ）。
            RefreshOrderLocked();

            if (_owner is { } owner)
            {
                if (_time.GetUtcNow() - _lastSample < owner.SilenceAfter) return;

                _owner = null;
                AnnounceLocked(HeartRateDisconnectReason.Silent, beatFollows: false);

                // 繋がりが変わっていれば、AnnounceLocked が NoHeartRate か Disconnected に戻している（= ここへは入らない）。
                if (_announced is { } still && _status == HeartRateDeviceStatus.Connected)
                {
                    _status = HeartRateDeviceStatus.NoHeartRate;
                    _lostAnnounced = true;
                    _dispatch.Enqueue(new HeartRateSignalLostEventParams(still.Monitor, still.Connection, owner.SilenceAfter));
                }
            }
            else
            {
                // 持ち主が居なくても、いまの繋がりを見直す。時計（HDS）は黙ると時刻で消えるだけで何も知らせてこないので、
                // 見直さないと「繋がっている」と知らせたまま残る（持ち主が線を落として手放したあと、拍を捨てられていた時計へ移った形）。
                AnnounceLocked(HeartRateDisconnectReason.Silent, beatFollows: false);
            }
        }

        _dispatch.Drain();
    }

    void IHeartRateDeviceSink.Found(IHeartRateDevice device)
    {
        lock (_gate) KnowLocked(device);
    }

    void IHeartRateDeviceSink.Beat(IHeartRateDevice from, int heartRate)
    {
        lock (_gate)
        {
            KnowLocked(from);

            // 繋がっていない心拍計の 1 拍（切れた直後に遅れて届いたもの）は捨てる。**持ち主にもしない** — 持ち主にすると、
            // 配らない拍のせいで、その心拍計が黙り切るまで（BLE 10 秒・HDS 60 秒）繋がっているほかの心拍計の拍まで捨ててしまう。
            // 手で切った心拍計の 1 拍も同じ（係が線を放し終わるまでに届く）。
            if (from.Connection == HeartRateConnection.None || IsHeld(from.Identity)) return;

            // 持ち主が居て、まだ黙っていないなら捨てる。**混ぜない**のがこの層の仕事。
            if (_owner is { } owner
                && !ReferenceEquals(owner, from)
                && _time.GetUtcNow() - _lastSample < owner.SilenceAfter)
                return;

            _owner = from;
            _lastSample = _time.GetUtcNow();
            AnnounceLocked(HeartRateDisconnectReason.LinkLost, beatFollows: true);

            // 1 拍は「繋がっている」と知らせた間にだけ配る（約束）。上で繋がっているのを見たので、ふつうは必ず通る —
            // 通らないのは、見たあとで線が切れた（係は lock の外で印を倒す）ときだけ。
            // 心拍なしのあとなら、先に「戻った」を出す（約束: 心拍なしの間に 1 拍は来ない）。0 も「届いた」に数える（心拍計は喋っている）。
            if (_announced is { } current)
            {
                if (_lostAnnounced) _dispatch.Enqueue(new HeartRateSignalBackEventParams(current.Monitor, current.Connection));

                _status = HeartRateDeviceStatus.Connected;
                _lostAnnounced = false;
                _dispatch.Enqueue(new HeartBeatEventParams(heartRate));
            }
        }

        _dispatch.Drain();
    }

    void IHeartRateDeviceSink.LinkChanged(IHeartRateDevice from)
    {
        lock (_gate)
        {
            KnowLocked(from);
            AnnounceLocked(HeartRateDisconnectReason.LinkLost, beatFollows: false);
        }

        _dispatch.Drain();
    }

    /// <summary>
    /// いまの繋がりを、最後に知らせたものと比べて積む（<b>lock の中で呼ぶ</b>）。無 → 有 = 繋がった・有 → 無 = 切れた（<paramref name="whenGone"/>。
    /// 受けるのをやめている最中なら <see cref="HeartRateDisconnectReason.Shutdown"/>）・有 X → 有 Y = X が切れた → Y が繋がった・同じ = 何も積まない。
    /// <para>
    /// X が切れた理由が「乗り換え（<see cref="HeartRateDisconnectReason.Replaced"/>）」になるのは、<b>Y の 1 拍で移ったとき</b>（<paramref name="beatFollows"/>）だけ —
    /// すぐ 1 拍が続くので、受け手はアバターへ「繋がっていない」を送らない。黙って手放した先や線が変わった先へ移ったときは、まだ 1 拍も無いので
    /// 切れた理由（<paramref name="whenGone"/>）のまま（乗り換えにすると、アバターが X の最後の心拍を出したまま残る）。
    /// </para>
    /// </summary>
    private void AnnounceLocked(HeartRateDisconnectReason whenGone, bool beatFollows)
    {
        RefreshOrderLocked();

        (MonitorIdentity Monitor, HeartRateConnection Connection)? now =
            Active is { } active && active.Connection is var connection && connection != HeartRateConnection.None ? (active.Identity, connection) : null;
        if (now == _announced) return;

        if (_announced is { } was)
        {
            var reason = now is not null && beatFollows ? HeartRateDisconnectReason.Replaced
                : _stopping ? HeartRateDisconnectReason.Shutdown
                : whenGone;
            _dispatch.Enqueue(new HeartRateDisconnectedEventParams(was.Monitor, was.Connection, reason));
        }

        if (now is { } current) _dispatch.Enqueue(new HeartRateConnectedEventParams(current.Monitor, current.Connection));

        _announced = now;

        // 繋がりが変わったら、心拍が届いているかは数え直し（切れた・乗り換えた = 心拍なしも閉じる。約束）。
        _status = now is null ? HeartRateDeviceStatus.Disconnected : HeartRateDeviceStatus.NoHeartRate;
        _lostAnnounced = false;
    }

    public void Dispose()
    {
        foreach (var discovery in _discoveries) (discovery as IDisposable)?.Dispose();
    }
}
