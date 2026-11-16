using Microsoft.Extensions.Logging;
using SpoitableToolKit.Modules.Hrs.Config;
using SpoitableToolKit.Modules.Hrs.Devices.Interfaces;
using SpoitableToolKit.Modules.Hrs.Devices.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace SpoitableToolKit.Modules.Hrs.Devices.Ble;

/// <summary>
/// Windows の BLE から心拍を拾う。
/// <para>
/// 追加の依存は無い。<c>Windows.Devices.Bluetooth</c> は OS の API で、
/// TFM に OS のバージョンを付けると SDK から射影が生える。
/// </para>
/// <para>
/// <b>Bluetooth が無い機械でも例外を外へ出さない。</b> ここで落ちるとモジュールごと読み込み失敗に
/// なってしまい、設定画面からも消える。繋がらないまま黙って待つのが正しい姿。
/// </para>
/// <para>
/// ログは、繋がらない理由を追うのに要るので細かく書く。探し始め・見つけた機器・GATT の段ごとの失敗理由・広告への切り替え・電池・切断を INF / WRN で書く。
/// 文頭の <c>BLE:</c> / <c>GATT:</c> / <c>Broadcast:</c> は、どの段の話かの印。
/// </para>
/// </summary>
internal sealed class WinRtHeartRateSource(HrsConfig config, ILogger logger, TimeProvider? time = null)
    : IHeartRateSource, IDisposable
{
    /// <summary>Heart Rate Service / Heart Rate Measurement。</summary>
    private static readonly Guid HeartRateService = BleUuid(0x180D);
    private static readonly Guid HeartRateMeasurement = BleUuid(0x2A37);

    /// <summary>Battery Service / Battery Level。</summary>
    private static readonly Guid BatteryService = BleUuid(0x180F);
    private static readonly Guid BatteryLevel = BleUuid(0x2A19);

    private static readonly TimeSpan BatteryEvery = TimeSpan.FromSeconds(60);

    /// <summary>通知を外す書き込みを待つ上限。相手が離れていると返ってこない。</summary>
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(2);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly BleSightings _discovery = new();
    private readonly PolarBroadcastWatch _broadcastWatch = new();
    private readonly SearchWatch _search = new(TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(5));

    /// <summary>
    /// 後片付けを 1 本にする。繋ぎ直しと畳みが重なると、同じ <c>BluetoothLEDevice</c> を
    /// 2 つのスレッドから二重に捨てる。
    /// </summary>
    private readonly Lock _teardown = new();

    // **フィールドで持つ。** ローカルのままだと GC に回収されて、
    // エラーも出さずに通知だけ止まる（一番気付きにくい壊れ方）。
    private BluetoothLEAdvertisementWatcher? _watcher;
    private BluetoothLEDevice? _device;

    /// <summary>
    /// 心拍のサービス。<b>これも持って、切るときに捨てる。</b> Windows は <c>GattDeviceService</c> が 1 つでも
    /// 生きている間は GATT を張ったままにするので、機器だけ捨てても繋がりは残る。
    /// </summary>
    private GattDeviceService? _service;

    private GattCharacteristic? _measurement;

    /// <summary>
    /// Windows に「線が落ちても繋ぎ直して」と頼む口（<c>MaintainConnection</c>）。<b>持ち続ける</b> — 捨てると頼みも消える。
    /// 頼まないと、相手が戻っても Windows が繋ぎ直さないことがある（腕に戻した Polar Sense に 3 分以上繋ぎ直さなかった実機がある）。
    /// 放すときは頼みを取り下げてから捨てる（取り下げないと、別のアプリが GATT を掴めない）。
    /// </summary>
    private GattSession? _session;

    /// <summary>
    /// 広告に心拍を載せてくる相手（Polar）。GATT が張れなかったときだけ埋まる。
    /// <b>広告は誰にでも見えるので、繋ぎに行かずに読めるのが利点</b>
    /// （他のアプリが GATT を掴んでいても、こちらは読める）。
    /// </summary>
    private ulong? _broadcasting;

    /// <summary>広告から最後に心拍を読めた時刻。読めない相手なら GATT を試し直す判断に使う。</summary>
    private DateTimeOffset _lastBroadcast;

    private DateTimeOffset _nextBattery;

    /// <summary>線が戻った。次の周で、時刻を待たずに電池を読み直す（線の知らせは別のスレッドから来る）。</summary>
    private volatile bool _batteryDue;

    /// <summary>GATT の線が上がっているか。落ちている間は <see cref="Connected"/> を倒す。</summary>
    private readonly GattLinkWatch _link = new();

    public IReadOnlyList<MonitorIdentity> Discovered => _discovery.All;

    public IReadOnlyList<MonitorSighting> Sightings => _discovery.Seen;

    public MonitorIdentity? Connected { get; private set; }

    public HeartRateConnection Connection => Connected is null
        ? HeartRateConnection.None
        : _broadcasting is null ? HeartRateConnection.BleGatt : HeartRateConnection.BleBroadcastPolar;

    public int? Battery { get; private set; }

    public TimeSpan SilenceAfter => TimeSpan.FromSeconds(config.NoSignalSeconds);

    private volatile IHeartRateSink? _sink;

    public void ReportTo(IHeartRateSink sink) => _sink = sink;

    /// <summary>手で切った心拍計のアドレス（パネルの「切断」）。「接続」まで繋ぎに行かない。書くのは <see cref="_teardown"/> の中（差し替えるだけ）。</summary>
    private ulong[] _heldOff = [];

    /// <summary>周回の待ちを起こす（「切断」「接続」をすぐ効かせる — 待ちは <see cref="HrsConfig.ReconnectSeconds"/>）。</summary>
    private readonly SemaphoreSlim _wake = new(0);

    private bool IsHeld(ulong address) => Array.IndexOf(Volatile.Read(ref _heldOff), address) >= 0;

    /// <summary>
    /// いま掴んでいる相手。<b>線が落ちていても GATT は掴んだまま</b>（<c>_link.Held</c>。<see cref="Connected"/> は落ちている間 null）。広告のときは <see cref="Connected"/>。
    /// 手で切った相手を放すかは、これで見る（<see cref="Connected"/> で見ると、線が落ちている間に切ったとき放さない）。
    /// </summary>
    private MonitorIdentity? Grasped
    {
        get
        {
            lock (_teardown) return _link.Held ?? Connected;
        }
    }

    /// <summary>
    /// 手で切る（その 1 台だけ）。<b>線を放すのは周回の上で</b>（次の頭。繋いでいる途中と重ならない）— ここでは印を付けて周回を起こすだけ。
    /// 「接続」まで <see cref="FindTarget"/> はその 1 台を選ばない（指定が空なら、ほかの心拍計を探し続ける）。
    /// </summary>
    public void Disconnect(MonitorIdentity monitor)
    {
        lock (_teardown) _heldOff = [.. _heldOff, monitor.Address];

        Wake();
    }

    public void Connect(MonitorIdentity monitor)
    {
        lock (_teardown) _heldOff = [.. _heldOff.Where(address => address != monitor.Address)];

        Wake();
    }

    private void Wake()
    {
        if (_wake.CurrentCount == 0) _wake.Release();
    }

    public async Task RunAsync(DeviceSelector selector, CancellationToken cancellationToken)
    {
        // 探し直すたびに数え直す。同じ入口を指定を変えて動かし直すと、前の探し始めから数えてしまう。
        _search.Found();

        if (!TryStartWatching(selector)) return;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (Grasped is { } held && IsHeld(held.Address))
                    await ReleaseAsync("disconnected by hand").ConfigureAwait(false);

                // 広告から読めているうちは GATT を試し直さない。毎周期つつくと、
                // 繋ぎ直しのたびに「繋がった / 切れた」がぱたぱたする。
                if (_broadcasting is not null && _time.GetUtcNow() - _lastBroadcast > SilenceAfter)
                {
                    logger.LogInformation(
                        "Broadcast: no heart rate from {Name}'s advertisements for {Seconds}s; trying GATT again",
                        Connected?.Name, SilenceAfter.TotalSeconds);
                    await ReleaseAsync("the advertisements went quiet").ConfigureAwait(false);
                }

                if (_measurement is null && _broadcasting is null)
                {
                    if (FindTarget(selector) is { } target)
                    {
                        _search.Found();
                        await TryConnectAsync(target, cancellationToken).ConfigureAwait(false);
                    }
                    else if (IsHeldInSight(_discovery.All, selector, Volatile.Read(ref _heldOff)))
                    {
                        // 見えているが手で切ってあるだけ。「見当たらない」とは書かない。
                        _search.Found();
                    }
                    else if (_search.StillLooking(_time.GetUtcNow()))
                    {
                        ReportStillLooking(selector);
                    }
                }
                else if (_device is { } device && _measurement is not null
                         && (_batteryDue || _time.GetUtcNow() >= _nextBattery))
                {
                    await ReadBatteryAsync(device, cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    // 「切断」「接続」が押されたら待たずに次の周回へ（Wake）。
                    await _wake.WaitAsync(TimeSpan.FromSeconds(config.ReconnectSeconds), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            await ReleaseAsync("stopped").ConfigureAwait(false);
            StopWatching();
        }
    }

    /// <summary>
    /// 広告を見張って、心拍計だけ拾う。<b>絞りは広告の service UUID で行う</b> —
    /// 名前で絞ると、名前を出さないデバイスが一覧から消える。
    /// </summary>
    private bool TryStartWatching(DeviceSelector selector)
    {
        try
        {
            var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
            watcher.Received += OnAdvertisement;
            watcher.Stopped += OnWatcherStopped;
            watcher.Start();
            _watcher = watcher;

            logger.LogInformation("BLE: scanning for heart rate monitors (looking for {Target})", selector.Describe());
            return true;
        }
        catch (Exception ex)
        {
            // Bluetooth が無い / 切ってある機械ではここに来る。モジュールは生かしたまま黙る。
            logger.LogWarning(ex, "BLE: could not start the scan; the heart rate monitor is unusable (is Bluetooth on?)");
            return false;
        }
    }

    /// <summary>
    /// スキャンが止まった。<b>こちらが止めたとき以外は理由を書く</b> —
    /// Bluetooth を切られると、ここが黙って止まり、以降は何も見つからなくなる。
    /// </summary>
    private void OnWatcherStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        if (args.Error == BluetoothError.Success) return;

        logger.LogWarning("BLE: the scan stopped by itself ({Error}); is Bluetooth still on?", args.Error);
    }

    private void ReportStillLooking(DeviceSelector selector)
    {
        var inSight = _discovery.All;
        var elapsed = _search.Elapsed(_time.GetUtcNow());

        if (inSight.Count == 0)
        {
            logger.LogInformation(
                "BLE: no heart rate monitor in sight after {Seconds:0}s (looking for {Target}); is it on and worn?",
                elapsed.TotalSeconds, selector.Describe());
            return;
        }

        logger.LogInformation(
            "BLE: {Target} is not among the {Count} heart rate monitor(s) in sight after {Seconds:0}s: {Devices}",
            selector.Describe(), inSight.Count, elapsed.TotalSeconds,
            string.Join(", ", inSight.Select(d => d.ToSuggestion())));
    }

    private void OnAdvertisement(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        // 広告から読む相手なら、まずそれを見る。**この広告は心拍サービスを名乗らない**ので、
        // 下の絞りより前に置かないと 1 通も通らない。電波の強さと時刻もここで差し替える（下の See まで来ない）。
        if (_broadcasting == args.BluetoothAddress)
        {
            ReadBroadcast(args);
            _discovery.Touch(args.BluetoothAddress, args.RawSignalStrengthInDBm, _time.GetUtcNow());
        }

        if (!args.Advertisement.ServiceUuids.Contains(HeartRateService)) return;

        // パネルへは push しない（能動スキャンでは 1 秒に何通も来る）。「心拍計」タブは開いている間、自分で読み直す。
        var (device, what) = _discovery.See(
            args.BluetoothAddress, args.Advertisement.LocalName, args.RawSignalStrengthInDBm, _time.GetUtcNow());

        switch (what)
        {
            case Sighting.New:
                logger.LogInformation("BLE: found {Device} (signal {Rssi} dBm)", device.ToSuggestion(), args.RawSignalStrengthInDBm);
                break;

            case Sighting.Named:
                logger.LogInformation("BLE: {Address} calls itself {Name}", MonitorIdentity.FormatAddress(device.Address), device.Name);
                break;
        }
    }

    /// <summary>広告の製造元データから心拍を拾う。Polar のものだけ見る。</summary>
    private void ReadBroadcast(BluetoothLEAdvertisementReceivedEventArgs args)
    {
        int? heartRate = null;
        var sections = new List<(ushort Company, byte[] Data)>();

        foreach (var section in args.Advertisement.ManufacturerData)
        {
            var reader = DataReader.FromBuffer(section.Data);
            var data = new byte[section.Data.Length];
            reader.ReadBytes(data);
            sections.Add((section.CompanyId, data));

            if (heartRate is null && section.CompanyId == HrsMeasurement.PolarCompanyId)
                heartRate = HrsMeasurement.ParsePolarBroadcast(data);
        }

        // 心拍サービスを名乗る広告は、製造元データの無い「ふつうの」広告。数えるのは中身のある 1 通だけ
        // （数えると、読める Polar でも名乗りの広告の数だけ「読めない」が積もる）。
        if (sections.Count > 0)
        {
            switch (_broadcastWatch.Feed(heartRate is not null))
            {
                case BroadcastNote.First:
                    logger.LogInformation("Broadcast: first advertisement from {Name}: {Data}",
                        Connected?.Name, Describe(sections));
                    break;

                case BroadcastNote.Unsupported:
                    logger.LogWarning(
                        "Broadcast: could not read a heart rate from {Name}'s advertisements {Count} times in a row; the device may not be supported ({Data})",
                        Connected?.Name, PolarBroadcastWatch.UnsupportedAfter, Describe(sections));
                    break;
            }
        }

        if (heartRate is not { } bpm) return;

        _lastBroadcast = _time.GetUtcNow();
        _sink?.Beat(this, bpm);
    }

    /// <summary>製造元データを、1 バイトずつ添字付きの 16 進で読める形に。</summary>
    internal static string Describe(IEnumerable<(ushort Company, byte[] Data)> sections) => string.Join("; ",
        sections.Select(s => $"company 0x{s.Company:X4}, {s.Data.Length} bytes: "
            + string.Join(' ', s.Data.Select((b, i) => $"[{i}]{b:X2}"))));

    /// <summary>
    /// GATT が張れなかった相手を、広告から読む側へ回す。
    /// <b>繋ぎに行かないので、他のアプリが掴んでいても読める</b>。
    /// </summary>
    private async Task FallBackToBroadcastAsync(MonitorIdentity target, string reason)
    {
        await ReleaseAsync(null).ConfigureAwait(false);

        _broadcastWatch.Reset();

        // まだ 1 通も読めていないが、猶予を持たせる（すぐ「読めない」と判断しないため）。
        _lastBroadcast = _time.GetUtcNow();

        // **繋がった印を、広告を読み始めるより先に立てる**（GATT と同じ理由。1 拍目が数え直しで消えないように）。
        Connected = target;
        _broadcasting = target.Address;

        logger.LogWarning("GATT: could not open {Name} ({Reason}); reading its advertisements instead", target.Name, reason);
        _sink?.LinkChanged(this);
    }

    private MonitorIdentity? FindTarget(DeviceSelector selector) => PickTarget(_discovery.All, selector, Volatile.Read(ref _heldOff));

    /// <summary>見えている心拍計から、繋ぐ相手を選ぶ。<b>手で切った相手（<paramref name="heldOff"/> のアドレス）は選ばない</b>。試験から叩けるように分けてある。</summary>
    internal static MonitorIdentity? PickTarget(IReadOnlyList<MonitorIdentity> sightings, DeviceSelector selector, IReadOnlyCollection<ulong> heldOff)
    {
        foreach (var device in sightings)
            if (selector.Matches(device) && !heldOff.Contains(device.Address)) return device;

        return null;
    }

    /// <summary>指定に合う心拍計が見えているが、手で切ってあるだけか（「見当たらない」とは書かない）。</summary>
    internal static bool IsHeldInSight(IReadOnlyList<MonitorIdentity> sightings, DeviceSelector selector, IReadOnlyCollection<ulong> heldOff) =>
        sightings.Any(device => selector.Matches(device) && heldOff.Contains(device.Address));

    private async Task TryConnectAsync(MonitorIdentity target, CancellationToken cancellationToken)
    {
        logger.LogInformation("GATT: connecting to {Device}", target.ToSuggestion());

        try
        {
            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(target.Address).AsTask(cancellationToken)
                .ConfigureAwait(false);

            if (device is null)
            {
                // 広告は見えているのに機器として開けない。Windows 側の Bluetooth が詰まっていることが多い。
                logger.LogWarning("GATT: Windows could not open {Device}; trying again in {Seconds}s",
                    target.ToSuggestion(), config.ReconnectSeconds);
                return;
            }

            _device = device;
            device.ConnectionStatusChanged += OnLinkChanged;

            // 広告に名前が無くても、繋げば名乗ることが多い。ログとパネルにアドレスを 2 回出さないため。
            target = _discovery.Rename(target, device.Name);

            var services = await device.GetGattServicesForUuidAsync(HeartRateService)
                .AsTask(cancellationToken).ConfigureAwait(false);

            var service = services.Status == GattCommunicationStatus.Success && services.Services.Count > 0
                ? services.Services[0]
                : null;

            // 使わないものはその場で捨てる。**持ったままだと GATT を握り続ける。**
            foreach (var other in services.Services)
                if (!ReferenceEquals(other, service)) other.Dispose();

            if (service is null)
            {
                await FallBackToBroadcastAsync(target, services.Status == GattCommunicationStatus.Success
                    ? "it does not offer the heart rate service"
                    : $"the heart rate service answered {Status(services.Status, services.ProtocolError)}").ConfigureAwait(false);
                return;
            }

            _service = service;

            var characteristics = await service.GetCharacteristicsForUuidAsync(HeartRateMeasurement)
                .AsTask(cancellationToken).ConfigureAwait(false);

            if (characteristics.Status != GattCommunicationStatus.Success || characteristics.Characteristics.Count == 0)
            {
                await FallBackToBroadcastAsync(target, characteristics.Status == GattCommunicationStatus.Success
                    ? "it has no heart rate measurement"
                    : $"the heart rate measurement answered {Status(characteristics.Status, characteristics.ProtocolError)}").ConfigureAwait(false);
                return;
            }

            var measurement = characteristics.Characteristics[0];

            var subscribed = await measurement
                .WriteClientCharacteristicConfigurationDescriptorWithResultAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify)
                .AsTask(cancellationToken).ConfigureAwait(false);

            if (subscribed.Status != GattCommunicationStatus.Success)
            {
                await FallBackToBroadcastAsync(target,
                    $"turning on notifications answered {Status(subscribed.Status, subscribed.ProtocolError)}").ConfigureAwait(false);
                return;
            }

            var session = await KeepLinkAsync(device, target, cancellationToken).ConfigureAwait(false);

            // **繋がった印を受け口より先に立てる。** 逆だと、その隙間に届いた 1 拍目を数えたあとで
            // モジュールが「新しく繋がった」と見て最小・平均・最大を数え直し、1 拍目が数字から消える。
            lock (_teardown)
            {
                _session = session;
                _measurement = measurement;
                _link.Hold(target);
                Connected = target;
            }

            measurement.ValueChanged += OnMeasurement;

            // 知らせる前に書く。モジュールが「入口が変わった」を書くので、逆だと順が前後して読みにくい。
            logger.LogInformation("GATT: connected to {Device}; receiving the heart rate", target.ToSuggestion());
            _sink?.LinkChanged(this);

            // 通知を受け始めるまでの間に線が落ちていたら、その知らせはもう来ない（掴む前の知らせは数えない）。
            // 今の様子を 1 回見る。
            NoteLink(device, log: false);

            await ReadBatteryAsync(device, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 繋がらないのはふつうに起きる（電源が入っていない、離れている、他のアプリが掴んでいる）。
            // 理由は残す（ログに無いと、繋がらない理由が追えない）。
            await FallBackToBroadcastAsync(target, $"{ex.GetType().Name}: {ex.Message.Trim()}").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Windows に、線が落ちても繋ぎ直してもらう（<see cref="_session"/>）。頼めなくても繋がりは使う（相手が戻れば、運が良ければ繋ぎ直す）。
    /// </summary>
    private async Task<GattSession?> KeepLinkAsync(BluetoothLEDevice device, MonitorIdentity target, CancellationToken cancellationToken)
    {
        try
        {
            var session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId).AsTask(cancellationToken).ConfigureAwait(false);
            session.MaintainConnection = true;
            logger.LogInformation("GATT: asked Windows to keep the link to {Name} and to reconnect when it is back", target.Name);
            return session;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("GATT: could not ask Windows to keep the link to {Name} ({Type}: {Message}); it may not reconnect by itself",
                target.Name, ex.GetType().Name, ex.Message.Trim());
            return null;
        }
    }

    /// <summary><c>Unreachable</c> などに、あれば ATT のエラー番号を添える。</summary>
    private static string Status(GattCommunicationStatus status, byte? protocolError) =>
        protocolError is { } code ? $"{status} (ATT error 0x{code:X2})" : status.ToString();

    /// <summary>
    /// 繋がりの上がり下がり。<b>こちらが切ったときは来ない</b>（受け口を先に外す）ので、
    /// ここに来るのは離れた・電源が落ちたとき。戻れば Windows が繋ぎ直す（<see cref="_session"/> で頼んである）。
    /// </summary>
    private void OnLinkChanged(BluetoothLEDevice sender, object args) => NoteLink(sender, log: true);

    /// <summary>
    /// 線の様子を繋がりに写す。<b>落ちている間は「繋がっている」と言わない</b>（<see cref="GattLinkWatch"/>）。
    /// <para>
    /// 掴んだ物（機器・サービス・通知）は<b>手放さない</b>。手放すと周回が繋ぎ直しに行き、電源の落ちた相手に
    /// 「GATT が張れない → 広告から読む」を繰り返して、繋がった / 切れたがぱたぱたする。
    /// </para>
    /// </summary>
    private void NoteLink(BluetoothLEDevice device, bool log)
    {
        BluetoothConnectionStatus status;
        string name;
        LinkChange change;

        lock (_teardown)
        {
            // 手放した機器から遅れて届いた知らせは、今の繋がりを変えない（捨てた機器はもう読めない）。
            if (!ReferenceEquals(device, _device)) return;

            // **様子を読むのもロックの中で。** 外で読むと、2 つの知らせが前後したときに古い方が後から入り、
            // 線は上がっているのに落ちたまま残る。
            status = device.ConnectionStatus;
            name = _link.Held?.Name ?? BleSightings.Clean(device.Name ?? "");
            change = _link.Feed(status == BluetoothConnectionStatus.Connected);

            if (change != LinkChange.None)
            {
                Connected = _link.Connected;

                // 電池は線が落ちている間に変わる（充電していたかもしれない）。古い数字を出したままにせず、戻ったら読み直す。
                if (change == LinkChange.Lost) Battery = null;
                else _batteryDue = true;
            }
        }

        if (log) logger.LogInformation("GATT: the link to {Name} is now {Status}", name, status);

        // 通知はロックの外で。受け取る側が何をするか分からない。
        if (change != LinkChange.None) _sink?.LinkChanged(this);
    }

    private void OnMeasurement(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        var reader = DataReader.FromBuffer(args.CharacteristicValue);
        var data = new byte[args.CharacteristicValue.Length];
        reader.ReadBytes(data);

        if (HrsMeasurement.Parse(data) is { } heartRate) _sink?.Beat(this, heartRate);
    }

    /// <summary>
    /// 電池を読む。<b>読んだサービスはその場で捨てる</b>（持ったままだと、心拍を切ったあとも GATT が残る）。
    /// 書くのは最初と、値が変わったときだけ。
    /// </summary>
    private async Task ReadBatteryAsync(BluetoothLEDevice device, CancellationToken cancellationToken)
    {
        _nextBattery = _time.GetUtcNow() + BatteryEvery;
        _batteryDue = false;

        try
        {
            var services = await device.GetGattServicesForUuidAsync(BatteryService)
                .AsTask(cancellationToken).ConfigureAwait(false);

            try
            {
                if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0) return;

                var characteristics = await services.Services[0]
                    .GetCharacteristicsForUuidAsync(BatteryLevel)
                    .AsTask(cancellationToken).ConfigureAwait(false);

                if (characteristics.Status != GattCommunicationStatus.Success || characteristics.Characteristics.Count == 0)
                    return;

                var read = await characteristics.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached)
                    .AsTask(cancellationToken).ConfigureAwait(false);

                if (read.Status != GattCommunicationStatus.Success || read.Value.Length < 1) return;

                int level = DataReader.FromBuffer(read.Value).ReadByte();
                MonitorIdentity? held;

                lock (_teardown)
                {
                    // 読んでいる間に線が落ちた・手放したなら出さない（出すと、繋がっていないのに電池だけ残る）。
                    if (!ReferenceEquals(device, _device) || _link.Connected is null) return;
                    if (Battery == level) return;

                    Battery = level;
                    held = _link.Held;
                }

                logger.LogInformation("GATT: {Name}'s battery is at {Level}%", held?.Name, level);
            }
            finally
            {
                foreach (var service in services.Services) service.Dispose();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "GATT: could not read the battery");
        }
    }

    /// <summary>
    /// GATT を解放する。<b>順は</b>通知を外す（相手に「もう送らなくていい」と書く）→
    /// 受け口を外す → サービスを捨てる → 機器を捨てる。サービスを捨てないと Windows は GATT を張ったままにする。
    /// 何度呼んでもよい。
    /// </summary>
    /// <param name="why">切った理由（ログに書く）。<c>null</c> なら書かない（広告へ切り替える途中など）。</param>
    private async Task ReleaseAsync(string? why)
    {
        MonitorIdentity? was;
        bool broadcasting;
        GattCharacteristic? measurement;
        GattDeviceService? service;
        BluetoothLEDevice? device;
        GattSession? session;

        lock (_teardown)
        {
            was = Connected;
            broadcasting = _broadcasting is not null;
            measurement = _measurement;
            service = _service;
            device = _device;
            session = _session;

            Connected = null;
            Battery = null;
            _link.Drop();
            _broadcasting = null;
            _measurement = null;
            _service = null;
            _device = null;
            _session = null;
        }

        if (measurement is not null)
        {
            measurement.ValueChanged -= OnMeasurement;

            try
            {
                await measurement
                    .WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None)
                    .AsTask().WaitAsync(ReleaseTimeout).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 相手が先に居なくなっていれば書けない。捨てるのは続ける。
                logger.LogDebug(ex, "GATT: could not turn the notifications off before letting go");
            }
        }

        // 繋ぎ直しの頼みを取り下げてから捨てる。取り下げないと、Windows が GATT を張ったままにする（別のアプリが掴めない）。
        if (session is not null)
        {
            try
            {
                session.MaintainConnection = false;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "GATT: could not withdraw the request to keep the link");
            }

            session.Dispose();
        }

        service?.Dispose();

        if (device is not null)
        {
            device.ConnectionStatusChanged -= OnLinkChanged;
            device.Dispose();
        }

        if (was is null) return;

        if (why is not null)
        {
            logger.LogInformation("{Via}: let go of {Device} ({Why})",
                broadcasting ? "Broadcast" : "GATT", was.Value.ToSuggestion(), why);
        }

        // 通知はロックの外で。受け取る側が何をするか分からない。
        _sink?.LinkChanged(this);
    }

    private void StopWatching()
    {
        lock (_teardown)
        {
            if (_watcher is not { } watcher) return;

            _watcher = null;
            watcher.Received -= OnAdvertisement;
            watcher.Stopped -= OnWatcherStopped;

            try
            {
                watcher.Stop();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "BLE: could not stop the scan");
            }
        }
    }

    /// <summary>16 ビットの短縮 UUID を BLE の基底 UUID へ広げる。</summary>
    private static Guid BleUuid(ushort assigned) =>
        new(assigned, 0x0000, 0x1000, 0x80, 0x00, 0x00, 0x80, 0x5F, 0x9B, 0x34, 0xFB);

    public void Dispose()
    {
        try
        {
            // 畳む道（RunAsync の finally）で済んでいれば何もしない。
            ReleaseAsync("disposed").Wait(ReleaseTimeout + TimeSpan.FromSeconds(1));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "GATT: could not let go cleanly while disposing");
        }

        StopWatching();
    }
}
