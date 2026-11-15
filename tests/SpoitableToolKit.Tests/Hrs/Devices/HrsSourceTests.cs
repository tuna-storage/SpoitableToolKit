using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Modules.Hrs.Config;
using SpoitableToolKit.Modules.Hrs.Devices;
using SpoitableToolKit.Modules.Hrs.Devices.Ble;
using SpoitableToolKit.Modules.Hrs.Devices.Hds;
using SpoitableToolKit.Modules.Hrs.Devices.Interfaces;
using SpoitableToolKit.Modules.Hrs.Devices.Models;
using SpoitableToolKit.Modules.Hrs.Events.HeartRate;
using Xunit;

namespace SpoitableToolKit.Tests.Hrs.Devices;

public sealed class HrsSourceTests
{
    /// <summary>仕様が公開されていないので、値の範囲で「読めた」を判断する。</summary>
    [Fact]
    public void TheHeartRateIsReadFromTheEndOfTheAdvertisement()
    {
        Assert.Equal(72, HrsMeasurement.ParsePolarBroadcast([0, 1, 2, 3, 4, 5, 6, 7, 8, 72]));
    }

    /// <summary>外れた値を流すとアバターが跳ねる。</summary>
    [Fact]
    public void ShortAndOutOfRangeAdvertisementsAreIgnored()
    {
        Assert.Null(HrsMeasurement.ParsePolarBroadcast([0, 1, 2, 3, 4, 5, 6, 7, 72]));
        Assert.Null(HrsMeasurement.ParsePolarBroadcast([0, 1, 2, 3, 4, 5, 6, 7, 8, 250]));
        Assert.Null(HrsMeasurement.ParsePolarBroadcast([0, 1, 2, 3, 4, 5, 6, 7, 8, 0]));
    }

    [Fact]
    public void TheHeartRateIsReadFromTheWatchBody()
    {
        Assert.Equal(72, HdsHeartRateSource.ParseHeartRate("""{"data":"heartRate:72"}"""));

        // 小数で来ることがある（HealthKit の値をそのまま入れてくる）。
        Assert.Equal(73, HdsHeartRateSource.ParseHeartRate("""{"data":"heartRate:72.6"}"""));
    }

    [Fact]
    public void ABodyWithoutAHeartRateIsDropped()
    {
        Assert.Null(HdsHeartRateSource.ParseHeartRate("""{"data":"stepCount:1200"}"""));
        Assert.Null(HdsHeartRateSource.ParseHeartRate("""{"data":"heartRate:0"}"""));
        Assert.Null(HdsHeartRateSource.ParseHeartRate("""{"data":"heartRate:とても速い"}"""));
        Assert.Null(HdsHeartRateSource.ParseHeartRate("{}"));
        Assert.Null(HdsHeartRateSource.ParseHeartRate("これは JSON ではない"));
    }

    /// <summary>ループバックへ実際に PUT を投げて、心拍が届くところまで見る。</summary>
    [Fact]
    public async Task APutFromTheWatchDeliversAHeartRate()
    {
        var config = new HrsConfig { Hds = { Enabled = true, Port = FreePort() } };
        var source = new HdsHeartRateSource(config, NullLogger.Instance, bindTo: IPAddress.Loopback);

        var received = new TaskCompletionSource<int>();
        source.ReportTo(new Sink(bpm => received.TrySetResult(bpm)));

        using var stop = new CancellationTokenSource();
        var running = source.RunAsync(DeviceSelector.Any, stop.Token);

        await PutAsync(config.Hds.Port, """{"data":"heartRate:81"}""");

        Assert.Equal(81, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(HeartRateConnection.Hds, source.Connection);

        // 送ってきた相手が出る。BLE のアドレスは無いので 0。
        Assert.NotNull(source.Connected);
        Assert.Equal(0UL, source.Connected!.Value.Address);

        await stop.CancelAsync();
        await running;
    }

    /// <summary>誰でも投げ込める受信口なので、使う人が有効にしたときだけ開く。</summary>
    [Fact]
    public async Task NothingListensWhileDisabled()
    {
        var config = new HrsConfig { Hds = { Enabled = false, Port = FreePort() } };
        var source = new HdsHeartRateSource(config, NullLogger.Instance, bindTo: IPAddress.Loopback);

        using var stop = new CancellationTokenSource();
        var running = source.RunAsync(DeviceSelector.Any, stop.Token);

        await Assert.ThrowsAnyAsync<SocketException>(() => PutAsync(config.Hds.Port, """{"data":"heartRate:81"}"""));

        await stop.CancelAsync();
        await running;
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }

    private static async Task PutAsync(int port, string body)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);

        var request = $"PUT / HTTP/1.1\r\nHost: localhost\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}";
        await client.GetStream().WriteAsync(Encoding.UTF8.GetBytes(request));

        // 応答の 1 バイト目を待つ。受け側が本文まで読み切ったことを、
        // こちら側で確かめられる唯一の合図。
        var response = new byte[1];
        await client.GetStream().ReadExactlyAsync(response);
    }

    /// <summary>持ち主の取り合いを試験から作るために使う。<paramref name="address"/> は繋がったときのアドレス（同じ心拍計かはアドレスで見るので、手で切る試験では心拍計ごとに変える。時計 = HDS は本物と同じく 0）。</summary>
    private sealed class FakeInput(TimeSpan silenceAfter, string name, ulong address = 1) : IHeartRateDevice, IHeartRateDeviceDiscovery
    {
        private readonly Lock _connectedGate = new();
        private MonitorIdentity? _connected;

        /// <summary>lock で読み書きする（別のスレッドから書く試験で、値がちぎれて別の機器に見えないように）。</summary>
        public MonitorIdentity? Connected
        {
            get
            {
                lock (_connectedGate) return _connected;
            }
            set
            {
                lock (_connectedGate) _connected = value;
            }
        }

        public MonitorIdentity Identity => Connected ?? new MonitorIdentity(address, name);

        /// <summary>窓口から見えるのは、繋がっている間だけこれ・切れていれば None。</summary>
        public HeartRateConnection Connection { get; set; } = HeartRateConnection.BleGatt;

        HeartRateConnection IHeartRateDevice.Connection => Connected is null ? HeartRateConnection.None : Connection;

        public int? Battery { get; set; }

        public TimeSpan SilenceAfter { get; } = silenceAfter;

        private IHeartRateDeviceSink? _sink;

        public bool AnnouncesItself { get; init; } = true;

        /// <summary>窓口を覚えて、自分を差し出すだけ（すぐ戻る）。</summary>
        public Task DiscoverAsync(DeviceSelector selector, IHeartRateDeviceSink sink, CancellationToken cancellationToken)
        {
            _sink = sink;
            if (AnnouncesItself) sink.Found(this);
            return Task.CompletedTask;
        }

        public void Send(int bpm)
        {
            Connected = new MonitorIdentity(address, name);
            _sink?.Beat(this, bpm);
        }

        public void Drop()
        {
            Connected = null;
            _sink?.LinkChanged(this);
        }

        /// <summary>窓口から「切断」「接続」と言われた順。線は放さない（放すのが遅い入口の形 — 窓口が自分で「繋がっていない」扱いにするかを見る）。</summary>
        public List<string> Calls { get; } = [];

        public ManualResetEventSlim? HoldDisconnect { get; set; }

        public ManualResetEventSlim DisconnectEntered { get; } = new();

        public void Disconnect()
        {
            DisconnectEntered.Set();
            HoldDisconnect?.Wait(TimeSpan.FromSeconds(10));
            lock (Calls) Calls.Add($"disconnect {Identity.Name}");
        }

        public void Connect()
        {
            lock (Calls) Calls.Add($"connect {Identity.Name}");
        }
    }

    /// <summary>窓口が「繋がっている」と見た直後に線が切れた形を、運に頼らずに作る（本物の係は lock の外で印を倒すので、窓口が見てから知らせるまでの間に切れることがある）。</summary>
    private sealed class DroppingInput : IHeartRateDevice, IHeartRateDeviceDiscovery
    {
        private int _reads;

        public MonitorIdentity Identity => new(1, "ベルト");

        public HeartRateConnection Connection => _reads++ == 0 ? HeartRateConnection.BleGatt : HeartRateConnection.None;

        public int? Battery => null;

        public TimeSpan SilenceAfter => TimeSpan.FromSeconds(10);

        public Task DiscoverAsync(DeviceSelector selector, IHeartRateDeviceSink sink, CancellationToken cancellationToken)
        {
            sink.Found(this);
            return Task.CompletedTask;
        }

        public void Disconnect()
        {
        }

        public void Connect()
        {
        }
    }

    private sealed class Sink(Action<int> beat) : IHeartRateSink
    {
        public void Beat(IHeartRateSource from, int bpm) => beat(bpm);

        public void LinkChanged(IHeartRateSource from)
        {
        }
    }

    private sealed class Ear : IHeartRateEventListener
    {
        public List<string> Seen { get; } = [];

        public List<int> Beats { get; } = [];

        public Action? WhenConnected { get; set; }

        public Action<int>? WhenBeat { get; set; }

        public void OnConnected(HeartRateConnectedEventParams e)
        {
            lock (Seen) Seen.Add($"connected {e.Monitor.Name} {e.Connection}");
            WhenConnected?.Invoke();
        }

        public void OnDisconnected(HeartRateDisconnectedEventParams e)
        {
            lock (Seen) Seen.Add($"disconnected {e.Monitor.Name} {e.Reason}");
        }

        public void OnHeartBeat(HeartBeatEventParams e)
        {
            lock (Seen)
            {
                Seen.Add($"beat {e.Bpm}");
                Beats.Add(e.Bpm);
            }

            WhenBeat?.Invoke(e.Bpm);
        }

        public void OnSignalLost(HeartRateSignalLostEventParams e)
        {
            lock (Seen) Seen.Add($"signal lost {e.Monitor.Name} {e.SilentFor.TotalSeconds}");
        }

        public void OnSignalBack(HeartRateSignalBackEventParams e)
        {
            lock (Seen) Seen.Add($"signal back {e.Monitor.Name}");
        }
    }

    /// <summary>偽の心拍計だけ。走らせなくても知らせられるように、作ったときに 1 回 <see cref="IHeartRateDeviceDiscovery.DiscoverAsync"/> を呼んで窓口を渡す（偽物はすぐ戻る。並びは渡した順）。口は試験の中だけで、owner はどれでも通す。</summary>
    private static (HeartRateManager Composite, Ear Ear) Composite(IReadOnlyList<IHeartRateDeviceDiscovery> fakes, TimeProvider clock)
    {
        var (composite, ear) = Build(fakes, clock);
        foreach (var fake in fakes) Assert.True(fake.DiscoverAsync(DeviceSelector.Any, composite, CancellationToken.None).IsCompleted);
        return (composite, ear);
    }

    /// <summary>知らせが届くのは <see cref="HeartRateManager.RunAsync"/> で走らせてから。</summary>
    private static (HeartRateManager Composite, Ear Ear) Composite(IReadOnlyList<IHeartRateSource> sources, TimeProvider clock) =>
        Build([.. sources.Select(source => new SourceAsDevice(source))], clock);

    private static (HeartRateManager Composite, Ear Ear) Build(IReadOnlyList<IHeartRateDeviceDiscovery> discoveries, TimeProvider clock)
    {
        var hub = new EventHubs(_ => true, NullLoggerFactory.Instance).CreateHub(HostOwner.Instance, [typeof(IHeartRateEventListener)]);
        var composite = new HeartRateManager(discoveries, hub, clock);
        var ear = new Ear();
        composite.Listeners.AddListener<IHeartRateEventListener>(HostOwner.Instance, ear);
        return (composite, ear);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>別のスレッドから進めても値がちぎれない時計（見張りの時計や、手放すスレッドと一緒に使う）。</summary>
    private sealed class SharedClock : TimeProvider
    {
        private long _ticks = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero).UtcTicks;

        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(50);

        Assert.True(condition());
    }

    /// <summary>交互に流すと 1 本の線に別人の心拍が混ざる（グラフも OSC も）。</summary>
    [Fact]
    public void WhileOneSourceHoldsTheOtherIsDropped()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var hds = new FakeInput(TimeSpan.FromSeconds(60), "時計") { Connection = HeartRateConnection.Hds };
        var clock = new Clock();

        var (composite, ear) = Composite([ble, hds], clock);
        using var _ = composite;

        ble.Send(60);
        hds.Send(120);

        Assert.Equal([60], ear.Beats);
        Assert.Equal(HeartRateConnection.BleGatt, composite.Connection);
    }

    /// <summary>設定を触らずにベルトから時計へ移れるのがこの層の目的。</summary>
    [Fact]
    public void WhenTheHolderGoesQuietTheOtherTakesOver()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var hds = new FakeInput(TimeSpan.FromSeconds(60), "時計") { Connection = HeartRateConnection.Hds };
        var clock = new Clock();

        var (composite, ear) = Composite([ble, hds], clock);
        using var _ = composite;

        ble.Send(60);
        clock.Now = clock.Now.AddSeconds(11);
        hds.Send(120);

        Assert.Equal([60, 120], ear.Beats);
        Assert.Equal(HeartRateConnection.Hds, composite.Connection);
        Assert.Equal("時計", composite.Connected?.Name);

        // 受け手が見た順: 繋がった → 1 拍 → 乗り換え（切れた → 繋がった）→ 1 拍。繋がった・切れたは対で、1 拍はその間だけ。
        Assert.Equal(["connected ベルト BleGatt", "beat 60", "disconnected ベルト Replaced", "connected 時計 Hds", "beat 120"], ear.Seen);
    }

    /// <summary>持ち主ごとに黙ってよい長さが違うので、BLE の 10 秒で判断すると、手首を下ろしただけで持ち主が入れ替わる。</summary>
    [Fact]
    public void TheSilenceBudgetBelongsToTheHolder()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var hds = new FakeInput(TimeSpan.FromSeconds(60), "時計") { Connection = HeartRateConnection.Hds };
        var clock = new Clock();

        var (composite, ear) = Composite([ble, hds], clock);
        using var _ = composite;

        hds.Send(120);
        Assert.Equal(TimeSpan.FromSeconds(60), composite.SilenceAfter);

        // BLE なら黙りすぎだが、時計にとってはまだ黙っていない。
        clock.Now = clock.Now.AddSeconds(30);
        ble.Send(60);

        Assert.Equal([120], ear.Beats);
    }

    /// <summary>GATT が張れた直後の「繋がったが心拍はまだ」を、繋がっていない扱いにしない。</summary>
    [Fact]
    public void WithNoHolderAnyConnectedSourceIsReported()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト")
        {
            Connected = new MonitorIdentity(1, "ベルト"),
        };

        var (composite, _) = Composite([ble], new Clock());
        using var _2 = composite;

        Assert.Equal("ベルト", composite.Connected?.Name);
        Assert.Equal(HeartRateConnection.BleGatt, composite.Connection);
    }

    /// <summary>束ねた側が null のままだと、パネルに出ない。</summary>
    [Fact]
    public void TheBatteryIsTheActiveSources()
    {
        var watch = new FakeInput(TimeSpan.FromSeconds(60), "時計");
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト")
        {
            Connected = new MonitorIdentity(1, "ベルト"),
            Battery = 64,
        };

        var (composite, _) = Composite([watch, ble], new Clock());
        using var _2 = composite;

        Assert.Equal(64, composite.Battery);
    }

    /// <summary>手放して初めて、別の入口が「繋がっている」を出せる。</summary>
    [Fact]
    public void AHolderThatStaysQuietIsReleased()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var clock = new Clock();

        var (composite, ear) = Composite([ble], clock);
        using var _ = composite;
        ble.Send(60);

        clock.Now = clock.Now.AddSeconds(11);
        ble.Drop();
        composite.Release();

        Assert.Null(composite.Connected);
        Assert.Equal(HeartRateConnection.None, composite.Connection);
        Assert.Equal(["connected ベルト BleGatt", "beat 60", "disconnected ベルト LinkLost"], ear.Seen);
    }

    /// <summary>入口が「繋がっていない」と言わなくても、受け手は切れたと聞く。同じ知らせが 2 回来ても、切れたは 1 回だけ。</summary>
    [Fact]
    public void AQuietHolderIsAnnouncedAsSilentOnce()
    {
        var hds = new FakeInput(TimeSpan.FromSeconds(60), "時計") { Connection = HeartRateConnection.Hds };
        var clock = new Clock();
        var (composite, ear) = Composite([hds], clock);
        using var _ = composite;
        hds.Send(80);

        // 入口はまだ「繋がっている」を出したまま黙っている（時計を外した）。手放すと、繋がっている入口として残る — 同じなので何も出ない。
        clock.Now = clock.Now.AddSeconds(61);
        hds.Connected = null;
        composite.Release();
        composite.Release();

        Assert.Equal(["connected 時計 Hds", "beat 80", "disconnected 時計 Silent"], ear.Seen);
    }

    /// <summary>GATT の線は上がっているが充電器の上、の形。次の 1 拍の前に「戻った」が来て、また黙ればまた「心拍なし」。その間も繋がったまま（切れたとは言わない）。</summary>
    [Fact]
    public void AHolderThatGoesQuietWhileStillConnectedIsSignalLostAndComesBackBeforeItsNextBeat()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var clock = new Clock();
        var (composite, ear) = Composite([ble], clock);
        using var _ = composite;

        ble.Send(60);
        Assert.True(composite.Receiving);

        clock.Now = clock.Now.AddSeconds(11);
        composite.Release();
        composite.Release();
        Assert.False(composite.Receiving);
        Assert.Equal("ベルト", composite.Connected?.Name);

        ble.Send(61);
        Assert.True(composite.Receiving);
        clock.Now = clock.Now.AddSeconds(11);
        composite.Release();

        Assert.Equal(
            ["connected ベルト BleGatt", "beat 60", "signal lost ベルト 10", "signal back ベルト", "beat 61", "signal lost ベルト 10"],
            ear.Seen);
    }

    /// <summary>状態を持つのは窓口（<see cref="HeartRateManager.Current"/>）。手で切った方は <see cref="HeartRateManager.HeldOff"/>。</summary>
    [Fact]
    public void TheHubKeepsTheStatusOfTheMonitorInUse()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト") { Battery = 80 };
        var clock = new Clock();
        var (composite, _) = Composite([ble], clock);
        using var owned = composite;
        Assert.Null(composite.Current);

        ble.Connected = new MonitorIdentity(1, "ベルト");
        ((IHeartRateDeviceSink)composite).LinkChanged(ble);
        Assert.Equal(
            new HeartRateDeviceState(new MonitorIdentity(1, "ベルト"), HeartRateConnection.BleGatt, HeartRateDeviceStatus.NoHeartRate, 80),
            composite.Current);

        ble.Send(60);
        Assert.Equal(HeartRateDeviceStatus.Connected, composite.Current?.Status);

        clock.Now = clock.Now.AddSeconds(11);
        composite.Release();
        Assert.Equal(HeartRateDeviceStatus.NoHeartRate, composite.Current?.Status);

        ble.Send(61);
        Assert.Equal(HeartRateDeviceStatus.Connected, composite.Current?.Status);

        composite.Disconnect();
        Assert.Null(composite.Current);
        Assert.Equal("ベルト", composite.HeldOff?.Name);
    }

    [Fact]
    public void BeforeAnyBeatTheMonitorConnectedFirstStaysInUse()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var hds = new FakeInput(TimeSpan.FromSeconds(60), "時計", address: 0) { Connection = HeartRateConnection.Hds };
        var (composite, ear) = Composite([ble, hds], new Clock());
        using var owned = composite;

        hds.Connected = new MonitorIdentity(0, "時計");
        ((IHeartRateDeviceSink)composite).LinkChanged(hds);
        ble.Connected = new MonitorIdentity(1, "ベルト");
        ((IHeartRateDeviceSink)composite).LinkChanged(ble);

        Assert.Equal(["connected 時計 Hds"], ear.Seen);
        Assert.Equal("時計", composite.Current?.Identity.Name);
    }

    /// <summary>戻っても、繋がったままの方から取り返さない（行ったり来たりしない）。まだ 1 拍も無い間の話で、1 拍が来ればその入口が持ち主（黙り切るまで）。</summary>
    [Fact]
    public void AMonitorWhoseLinkDropsQueuesBehindTheOneThatStayed()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var hds = new FakeInput(TimeSpan.FromSeconds(60), "時計", address: 0) { Connection = HeartRateConnection.Hds };
        var (composite, ear) = Composite([ble, hds], new Clock());
        using var owned = composite;

        ble.Connected = new MonitorIdentity(1, "ベルト");
        ((IHeartRateDeviceSink)composite).LinkChanged(ble);
        hds.Connected = new MonitorIdentity(0, "時計");
        ((IHeartRateDeviceSink)composite).LinkChanged(hds);

        ble.Drop();
        ble.Connected = new MonitorIdentity(1, "ベルト");
        ((IHeartRateDeviceSink)composite).LinkChanged(ble);

        Assert.Equal(["connected ベルト BleGatt", "disconnected ベルト LinkLost", "connected 時計 Hds"], ear.Seen);
        Assert.Equal("時計", composite.Current?.Identity.Name);
    }

    /// <summary>時計（HDS）は黙ると時刻で消えるだけで何も知らせてこない。見張りが並びを見直さないと、ベルトが去ったとき、時計より先から繋がっている心拍計を差し置いて時計が出る。</summary>
    [Fact]
    public void AWatchThatVanishesAndReturnsWhileTheBeltCarriesQueuesBehind()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var hds = new FakeInput(TimeSpan.FromSeconds(60), "時計", address: 0) { Connection = HeartRateConnection.Hds };
        var other = new FakeInput(TimeSpan.FromSeconds(10), "もう 1 本", address: 2);
        var clock = new Clock();
        var (composite, ear) = Composite([ble, hds, other], clock);
        using var owned = composite;

        ble.Send(60);
        hds.Connected = new MonitorIdentity(0, "時計");
        ((IHeartRateDeviceSink)composite).LinkChanged(hds);
        other.Connected = new MonitorIdentity(2, "もう 1 本");
        ((IHeartRateDeviceSink)composite).LinkChanged(other);

        // ベルトが運んでいる間に、時計が知らせずに消えて戻る（見張りはその間も回る）。
        hds.Connected = null;
        composite.Release();
        hds.Connected = new MonitorIdentity(0, "時計");
        composite.Release();

        // ベルトが外れて黙り切ると、先から繋がっている「もう 1 本」が出る。
        ble.Drop();
        clock.Now = clock.Now.AddSeconds(11);
        composite.Release();

        Assert.Equal(["connected ベルト BleGatt", "beat 60", "disconnected ベルト LinkLost", "connected もう 1 本 BleGatt"], ear.Seen);
    }

    /// <summary>心拍なしを閉じるのは「切れた」なので、「戻った」は出さない。繋ぎ直した 1 拍目は、ふつうの 1 拍目。</summary>
    [Fact]
    public void ASignalLostThatEndsInADisconnectIsNotFollowedByABack()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var clock = new Clock();
        var (composite, ear) = Composite([ble], clock);
        using var _ = composite;

        ble.Send(60);
        clock.Now = clock.Now.AddSeconds(11);
        composite.Release();
        ble.Drop();
        ble.Send(61);

        Assert.Equal(
            ["connected ベルト BleGatt", "beat 60", "signal lost ベルト 10", "disconnected ベルト LinkLost", "connected ベルト BleGatt", "beat 61"],
            ear.Seen);
        Assert.True(composite.Receiving);
    }

    /// <summary>ページは「接続済み・心拍なし」（線が戻ったが充電器の上、の形）。心拍なしは届いていたものが途絶えたときだけなので、いくら待っても出さない。</summary>
    [Fact]
    public void AConnectionWithoutAnyBeatIsNotReceivingAndIsNeverSignalLost()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var clock = new Clock();
        var (composite, ear) = Composite([ble], clock);
        using var _ = composite;

        ble.Connected = new MonitorIdentity(1, "ベルト");
        ((IHeartRateDeviceSink)composite).LinkChanged(ble);
        clock.Now = clock.Now.AddSeconds(60);
        composite.Release();

        Assert.Equal(["connected ベルト BleGatt"], ear.Seen);
        Assert.Equal("ベルト", composite.Connected?.Name);
        Assert.False(composite.Receiving);
    }

    /// <summary>GATT から広告へ切り替えた直後など、1 拍も来ていない繋がりでは心拍なしを出さない。ページは新しい繋がりで 1 拍届くまで「接続済み・心拍なし」。</summary>
    [Fact]
    public void ANewWayOfConnectingWithoutABeatYetIsNotSignalLost()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var clock = new Clock();
        var (composite, ear) = Composite([ble], clock);
        using var _ = composite;

        ble.Send(60);
        ble.Connection = HeartRateConnection.BleBroadcastPolar;
        ((IHeartRateDeviceSink)composite).LinkChanged(ble);
        Assert.False(composite.Receiving);

        clock.Now = clock.Now.AddSeconds(11);
        composite.Release();

        Assert.Equal(
            ["connected ベルト BleGatt", "beat 60", "disconnected ベルト LinkLost", "connected ベルト BleBroadcastPolar"],
            ear.Seen);
    }

    /// <summary>乗り換えにすると、モジュールがアバターへ「繋がっていない」を送らず、時計の最後の心拍を出したまま残る。乗り換えは、1 拍で移ったときだけ。</summary>
    [Fact]
    public void AWatchThatGoesQuietWithOnlyAnIdleBeltLeftIsSilentNotReplaced()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var hds = new FakeInput(TimeSpan.FromSeconds(60), "時計") { Connection = HeartRateConnection.Hds };
        var clock = new Clock();
        var (composite, ear) = Composite([ble, hds], clock);
        using var _ = composite;

        ble.Connected = new MonitorIdentity(1, "ベルト");
        ((IHeartRateDeviceSink)composite).LinkChanged(ble);
        hds.Send(80);

        clock.Now = clock.Now.AddSeconds(61);
        hds.Connected = null;
        composite.Release();

        Assert.Equal(
            ["connected ベルト BleGatt", "disconnected ベルト Replaced", "connected 時計 Hds", "beat 80", "disconnected 時計 Silent", "connected ベルト BleGatt"],
            ear.Seen);
        Assert.False(composite.Receiving);
    }

    /// <summary>ベルトが線を落として手放されたあと、拍を捨てられていた時計へ移り、その時計も 1 拍も出さずに黙った形。見張りが見直さないと「繋がっている」と知らせたまま残る。</summary>
    [Fact]
    public void AnInputThatTimesOutIsDisconnectedEvenWithoutAHolder()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var hds = new FakeInput(TimeSpan.FromSeconds(60), "時計") { Connection = HeartRateConnection.Hds };
        var clock = new Clock();
        var (composite, ear) = Composite([ble, hds], clock);
        using var _ = composite;

        ble.Send(60);
        hds.Send(120);
        ble.Drop();
        clock.Now = clock.Now.AddSeconds(11);
        composite.Release();

        hds.Connected = null;
        composite.Release();

        Assert.Equal(
            ["connected ベルト BleGatt", "beat 60", "disconnected ベルト LinkLost", "connected 時計 Hds", "disconnected 時計 Silent"],
            ear.Seen);
    }

    /// <summary>入口が線を放すのが遅くても、その 1 台の 1 拍・「変わった」・見張りでは戻らない。「接続」で、入口がまだ放していなければその場で繋がる。入口へは切る・繋ぐをこの順で伝える。</summary>
    [Fact]
    public void AMonitorDisconnectedByHandIsAnnouncedOnceAndStaysOffUntilConnect()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var clock = new Clock();
        var (composite, ear) = Composite([ble], clock);
        using var _ = composite;
        ble.Send(60);

        Assert.Equal("ベルト", composite.Disconnect()?.Name);
        ble.Send(61);
        ((IHeartRateDeviceSink)composite).LinkChanged(ble);
        clock.Now = clock.Now.AddSeconds(11);
        composite.Release();

        Assert.Null(composite.Connected);
        Assert.Equal("ベルト", composite.HeldOff?.Name);
        Assert.Equal(["connected ベルト BleGatt", "beat 60", "disconnected ベルト ByUser"], ear.Seen);

        Assert.True(composite.Reconnect());
        Assert.Equal(["connected ベルト BleGatt", "beat 60", "disconnected ベルト ByUser", "connected ベルト BleGatt"], ear.Seen);
        ble.Send(62);

        Assert.Null(composite.HeldOff);
        Assert.Equal(["connected ベルト BleGatt", "beat 60", "disconnected ベルト ByUser", "connected ベルト BleGatt", "beat 62"], ear.Seen);
        Assert.Equal(["disconnect ベルト", "connect ベルト"], ble.Calls);
    }

    /// <summary>切った 1 台から遅れて届いた 1 拍は持ち主も取らない（取ると、その 1 台が黙り切るまで時計の拍を捨てる）。</summary>
    [Fact]
    public void WhileOneMonitorIsDisconnectedByHandAnotherStillConnects()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var hds = new FakeInput(TimeSpan.FromSeconds(60), "時計", address: 0) { Connection = HeartRateConnection.Hds };
        var (composite, ear) = Composite([ble, hds], new Clock());
        using var _ = composite;
        ble.Send(60);

        composite.Disconnect();
        ble.Send(61);
        hds.Send(120);

        Assert.Equal(
            ["connected ベルト BleGatt", "beat 60", "disconnected ベルト ByUser", "connected 時計 Hds", "beat 120"],
            ear.Seen);
        Assert.Equal("ベルト", composite.HeldOff?.Name);
    }

    /// <summary>乗り換えではない（モジュールがアバターを倒す）。ベルトは 1 拍も無いので「受信中」ではない。</summary>
    [Fact]
    public void DisconnectingTheCarrierWithOnlyAnIdleMonitorLeftMovesToItWithoutABeat()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var hds = new FakeInput(TimeSpan.FromSeconds(60), "時計", address: 0) { Connection = HeartRateConnection.Hds };
        var (composite, ear) = Composite([ble, hds], new Clock());
        using var _ = composite;
        ble.Connected = new MonitorIdentity(1, "ベルト");
        ((IHeartRateDeviceSink)composite).LinkChanged(ble);
        hds.Send(80);

        Assert.Equal("時計", composite.Disconnect()?.Name);

        Assert.Equal(
            ["connected ベルト BleGatt", "disconnected ベルト Replaced", "connected 時計 Hds", "beat 80", "disconnected 時計 ByUser", "connected ベルト BleGatt"],
            ear.Seen);
        Assert.False(composite.Receiving);
    }

    /// <summary>切ったベルトが（入口がまだ放していなくて）繋がったままでも、時計が運んでいれば時計のまま。ベルトが入るのは、時計が黙り切るか切れたとき（混ぜない）。</summary>
    [Fact]
    public void ConnectingAgainDoesNotPushAsideTheMonitorConnectedNow()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var hds = new FakeInput(TimeSpan.FromSeconds(60), "時計", address: 0) { Connection = HeartRateConnection.Hds };
        var (composite, ear) = Composite([ble, hds], new Clock());
        using var _ = composite;
        ble.Send(60);
        composite.Disconnect();
        hds.Send(120);

        Assert.True(composite.Reconnect());
        ble.Send(61);

        Assert.Equal(["connected ベルト BleGatt", "beat 60", "disconnected ベルト ByUser", "connected 時計 Hds", "beat 120"], ear.Seen);
        Assert.Equal("時計", composite.Connected?.Name);
    }

    /// <summary>繋いだあとで名乗り直すので、手で切った印が外れないようアドレスで見る。アドレスの無い時計（HDS の送り主）は名前で見る。</summary>
    [Fact]
    public void TheSameMonitorIsKnownByItsAddressEvenAfterItRenamesItself()
    {
        Assert.True(new MonitorIdentity(5, "Polar").IsSame(new MonitorIdentity(5, "Polar H10 ABC123")));
        Assert.False(new MonitorIdentity(5, "Polar").IsSame(new MonitorIdentity(6, "Polar")));
        Assert.True(new MonitorIdentity(0, "192.168.1.20").IsSame(new MonitorIdentity(0, "192.168.1.20")));
        Assert.False(new MonitorIdentity(0, "192.168.1.20").IsSame(new MonitorIdentity(0, "192.168.1.21")));
        Assert.False(new MonitorIdentity(0, "Polar").IsSame(new MonitorIdentity(5, "Polar")));
    }

    /// <summary>デスクトップと VR の窓の両方から押せる。終わったとき、窓口が切っていなければ入口へ最後に伝えたのは「接続」、切っていれば「切断」。</summary>
    [Fact]
    public void DisconnectAndConnectFromTwoThreadsLeaveTheHubAndTheInputAgreeing()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var (composite, _) = Composite([ble], new Clock());
        using var owned = composite;
        ble.Send(60);
        using var start = new Barrier(2);

        var cutting = new Thread(() =>
        {
            start.SignalAndWait();
            for (var n = 0; n < 2000; n++) composite.Disconnect();
        });
        var joining = new Thread(() =>
        {
            start.SignalAndWait();
            for (var n = 0; n < 2000; n++) composite.Reconnect();
        });
        cutting.Start();
        joining.Start();
        cutting.Join();
        joining.Join();

        string last;
        lock (ble.Calls) last = ble.Calls.LastOrDefault() ?? "connect ベルト";
        Assert.Equal(composite.HeldOff is null ? "connect ベルト" : "disconnect ベルト", last);
    }

    /// <summary>入口が「切断」を受けている最中に「接続」が割り込むと、入口へは「接続」→「切断」の順に届き、窓口は切っていないのに入口だけ切ったまま残る。</summary>
    [Fact]
    public void ConnectWaitsUntilTheInputHasBeenToldToDisconnect()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var (composite, _) = Composite([ble], new Clock());
        using var owned = composite;
        ble.Send(60);
        using var hold = new ManualResetEventSlim();
        ble.HoldDisconnect = hold;

        var cutting = new Thread(() => composite.Disconnect());
        cutting.Start();
        Assert.True(ble.DisconnectEntered.Wait(TimeSpan.FromSeconds(10)));

        var joining = new Thread(() => composite.Reconnect());
        joining.Start();
        var joinedWhileHeld = joining.Join(TimeSpan.FromMilliseconds(300));

        hold.Set();
        Assert.True(cutting.Join(TimeSpan.FromSeconds(10)));
        Assert.True(joining.Join(TimeSpan.FromSeconds(10)));

        Assert.False(joinedWhileHeld);
        string[] calls;
        lock (ble.Calls) calls = [.. ble.Calls];
        Assert.Equal(["disconnect ベルト", "connect ベルト"], calls);
        Assert.Null(composite.HeldOff);
    }

    /// <summary>見えているのが切った 1 台だけなら「見当たらない」とは書かない。周回そのもの（放す・起こす）は実機でしか走らない。</summary>
    [Fact]
    public void TheBleScanSkipsAMonitorDisconnectedByHand()
    {
        var belt = new MonitorIdentity(0xA1, "Belt");
        var other = new MonitorIdentity(0xB2, "Other");

        Assert.Equal(other, WinRtHeartRateSource.PickTarget([belt, other], DeviceSelector.Any, [0xA1UL]));
        Assert.Equal(belt, WinRtHeartRateSource.PickTarget([belt, other], DeviceSelector.Any, []));
        Assert.Null(WinRtHeartRateSource.PickTarget([belt], new DeviceSelector(0xA1, null), [0xA1UL]));

        Assert.True(WinRtHeartRateSource.IsHeldInSight([belt], new DeviceSelector(0xA1, null), [0xA1UL]));
        Assert.False(WinRtHeartRateSource.IsHeldInSight([other], new DeviceSelector(0xA1, null), [0xA1UL]));
        Assert.False(WinRtHeartRateSource.IsHeldInSight([belt], new DeviceSelector(0xA1, null), []));
    }

    [Fact]
    public void WithNothingConnectedThereIsNothingToDisconnect()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var (composite, ear) = Composite([ble], new Clock());
        using var _ = composite;

        Assert.Null(composite.Disconnect());
        Assert.False(composite.Reconnect());
        Assert.Empty(ear.Seen);
        Assert.Empty(ble.Calls);
    }

    [Fact]
    public async Task AWatchDisconnectedByHandIsIgnoredUntilConnect()
    {
        var clock = new SharedClock();
        var config = new HrsConfig { Hds = { Enabled = true, Port = FreePort() } };
        var hds = new HdsHeartRateSource(config, NullLogger.Instance, clock, IPAddress.Loopback);
        var (composite, ear) = Composite([hds], clock);
        using var _ = composite;
        using var stop = new CancellationTokenSource();
        var running = composite.RunAsync(DeviceSelector.Any, stop.Token);

        await PutAsync(config.Hds.Port, """{"data":"heartRate:81"}""");
        await Until(() =>
        {
            lock (ear.Seen) return ear.Beats.Count == 1;
        });

        Assert.Equal("127.0.0.1", composite.Disconnect()?.Name);
        Assert.Null(hds.Connected);

        // 応答は本文を読んだところで返る（心拍を渡すのはそのあと）ので、捨てたのを数えて待つ。
        await PutAsync(config.Hds.Port, """{"data":"heartRate:82"}""");
        await Until(() => hds.IgnoredWhileDisconnected == 1);
        Assert.Null(hds.Connected);
        lock (ear.Seen) Assert.Equal([81], ear.Beats);

        Assert.True(composite.Reconnect());
        await PutAsync(config.Hds.Port, """{"data":"heartRate:83"}""");
        await Until(() =>
        {
            lock (ear.Seen) return ear.Beats.Count == 2;
        });

        lock (ear.Seen)
        {
            Assert.Equal(
                ["connected 127.0.0.1 Hds", "beat 81", "disconnected 127.0.0.1 ByUser", "connected 127.0.0.1 Hds", "beat 83"],
                ear.Seen);
        }

        await stop.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>覚えていないと、黙って持ち主を手放したときに「まだ繋がっている」と分からず、心拍なしではなく「切れた（黙った）」になる。</summary>
    [Fact]
    public void AMonitorThatWasNeverFoundIsKnownFromItsFirstBeat()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト") { AnnouncesItself = false };
        var clock = new Clock();
        var (composite, ear) = Composite([ble], clock);
        using var _ = composite;

        ble.Send(60);
        clock.Now = clock.Now.AddSeconds(11);
        composite.Release();

        Assert.Equal(["connected ベルト BleGatt", "beat 60", "signal lost ベルト 10"], ear.Seen);
        Assert.Equal(HeartRateDeviceStatus.NoHeartRate, composite.Current?.Status);
    }

    /// <summary>覚えないと、繋がったのが窓口から見えない。</summary>
    [Fact]
    public void AMonitorThatWasNeverFoundIsKnownFromItsFirstLinkChange()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト") { AnnouncesItself = false };
        var (composite, ear) = Composite([ble], new Clock());
        using var _ = composite;

        ble.Connected = new MonitorIdentity(1, "ベルト");
        ((IHeartRateDeviceSink)composite).LinkChanged(ble);

        Assert.Equal(["connected ベルト BleGatt"], ear.Seen);
        Assert.Equal("ベルト", composite.Connected?.Name);
    }

    /// <summary>入口は喋っている。0 は「計れていない」で、表示はモジュールが「—」にする。</summary>
    [Fact]
    public void AZeroBeatStillCountsAsReceiving()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var (composite, ear) = Composite([ble], new Clock());
        using var _ = composite;

        ble.Send(0);

        Assert.True(composite.Receiving);
        Assert.Equal(["connected ベルト BleGatt", "beat 0"], ear.Seen);
    }

    /// <summary>時計は黙ると繋がりごと消えるので心拍なしは出さない（混ぜない）。黙り明けの 1 拍目も捨てない（HDS は繋がった印を 1 拍より先に立てる）。ループバックへ PUT を投げ、窓口の時計を進める。</summary>
    [Fact]
    public async Task AQuietWatchIsDisconnectedAsSilentAndNeverSignalLost()
    {
        var clock = new SharedClock();
        var config = new HrsConfig { Hds = { Enabled = true, Port = FreePort() } };
        var hds = new HdsHeartRateSource(config, NullLogger.Instance, clock, IPAddress.Loopback);
        var (composite, ear) = Composite([hds], clock);
        using var _ = composite;
        using var stop = new CancellationTokenSource();
        var running = composite.RunAsync(DeviceSelector.Any, stop.Token);

        await PutAsync(config.Hds.Port, """{"data":"heartRate:81"}""");
        await Until(() =>
        {
            lock (ear.Seen) return ear.Beats.Count == 1;
        });
        Assert.True(composite.Receiving);

        clock.Advance(TimeSpan.FromSeconds(61));
        composite.Release();

        string[] seen;
        lock (ear.Seen) seen = [.. ear.Seen];
        Assert.Equal(["connected 127.0.0.1 Hds", "beat 81", "disconnected 127.0.0.1 Silent"], seen);
        Assert.False(composite.Receiving);

        // 黙り明けの 1 拍目から、また繋がる。
        await PutAsync(config.Hds.Port, """{"data":"heartRate:82"}""");
        await Until(() =>
        {
            lock (ear.Seen) return ear.Beats.Count == 2;
        });
        lock (ear.Seen) Assert.Equal(["connected 127.0.0.1 Hds", "beat 82"], ear.Seen[3..]);

        await stop.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>入口がまだ繋がった印を立てていない形。</summary>
    [Fact]
    public void ABeatBeforeAnyConnectionIsNotHandedOut()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var (composite, ear) = Composite([ble], new Clock());
        using var _ = composite;

        ((IHeartRateDeviceSink)composite).Beat(ble, 70);

        Assert.Empty(ear.Seen);
    }

    /// <summary>切れた直後に遅れて届いた 1 拍を持ち主が取ると、配らない拍のせいで、その入口が黙り切るまで、繋がっている時計の拍まで捨ててしまう。</summary>
    [Fact]
    public void ABeatFromAnInputThatIsNotConnectedDoesNotHoldTheOthersBack()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var hds = new FakeInput(TimeSpan.FromSeconds(60), "時計") { Connection = HeartRateConnection.Hds };
        var (composite, ear) = Composite([ble, hds], new Clock());
        using var _ = composite;

        ((IHeartRateDeviceSink)composite).Beat(ble, 70);
        hds.Send(120);

        Assert.Equal(["connected 時計 Hds", "beat 120"], ear.Seen);
        Assert.Equal("時計", composite.Connected?.Name);
    }

    /// <summary>入口は lock の外で印を倒すので、窓口が見てから知らせるまでの間に切れることがある。</summary>
    [Fact]
    public void ABeatWhoseLinkDropsAsItArrivesIsNotHandedOut()
    {
        var ble = new DroppingInput();
        var (composite, ear) = Composite([ble], new Clock());
        using var _ = composite;

        ((IHeartRateDeviceSink)composite).Beat(ble, 70);

        Assert.Empty(ear.Seen);
    }

    /// <summary>呼び返しても詰まらず、順も崩れない。呼び返しの中で配り直すと、あとに足した受け手には 1 拍と切れたが繋がったより先に届いてしまう（受け手が 1 人だけでは、配り直しても順が同じに見える）。</summary>
    [Fact]
    public void AChangeMadeWhileAListenerIsBeingToldArrivesAfterwards()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var (composite, ear) = Composite([ble], new Clock());
        using var _ = composite;
        ear.WhenConnected = ble.Drop;
        var later = new Ear();
        composite.Listeners.AddListener<IHeartRateEventListener>(HostOwner.Instance, later);

        ble.Send(60);

        Assert.Equal(["connected ベルト BleGatt", "beat 60", "disconnected ベルト LinkLost"], ear.Seen);
        Assert.Equal(["connected ベルト BleGatt", "beat 60", "disconnected ベルト LinkLost"], later.Seen);
    }

    /// <summary>4 つのスレッドが 1 つの入口から、揃えて一斉に 1 拍と「切れた」を送る（BLE の通知と線の見張りは別のスレッドから来る）。もう 1 つは時計を進めて持ち主を手放し、もう 1 つはパネルの「切断」「接続」を繰り返す（最後は「接続」）。終わったあとの最後の知らせは、入口のいまの繋がりと合う。</summary>
    [Fact]
    public void ConcurrentNoticesStillPairUpAndBeatsStayInside()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var clock = new SharedClock();
        var (composite, ear) = Composite([ble], clock);
        using var _ = composite;
        using var start = new Barrier(6);

        var threads = Enumerable.Range(0, 4).Select(i => new Thread(() =>
        {
            start.SignalAndWait();
            for (var n = 0; n < 500; n++)
            {
                if ((n + i) % 7 == 0) ble.Drop();
                else ble.Send(60 + i);
            }
        })).Append(new Thread(() =>
        {
            start.SignalAndWait();
            for (var n = 0; n < 500; n++)
            {
                clock.Advance(TimeSpan.FromSeconds(11));
                composite.Release();
            }
        })).Append(new Thread(() =>
        {
            start.SignalAndWait();
            for (var n = 0; n < 500; n++)
            {
                composite.Disconnect();
                composite.Reconnect();
            }
        })).ToArray();
        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join();

        var connected = false;
        var lost = false;
        string[] seen;
        lock (ear.Seen) seen = [.. ear.Seen];
        Assert.NotEmpty(seen);
        foreach (var line in seen)
        {
            if (line.StartsWith("connected", StringComparison.Ordinal))
            {
                Assert.False(connected, "connected twice in a row");
                connected = true;
                lost = false;
            }
            else if (line.StartsWith("disconnected", StringComparison.Ordinal))
            {
                Assert.True(connected, "disconnected without connected");
                connected = false;
                lost = false;
            }
            else if (line.StartsWith("signal lost", StringComparison.Ordinal))
            {
                Assert.True(connected, "signal lost outside a connection");
                Assert.False(lost, "signal lost twice in a row");
                lost = true;
            }
            else if (line.StartsWith("signal back", StringComparison.Ordinal))
            {
                Assert.True(lost, "signal back without signal lost");
                lost = false;
            }
            else
            {
                Assert.True(connected, "a beat outside a connection");
                Assert.False(lost, "a beat while the signal was lost");
            }
        }

        Assert.Equal(ble.Connected is not null, connected);
    }

    /// <summary>知らせた側から見ると同期。待たずに戻ると、戻ったあとに読んだ状態がまだ古い。</summary>
    [Fact]
    public void ANoticeIsDeliveredByTheTimeItsSenderReturnsEvenWhileAnotherIsBeingDelivered()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var (composite, ear) = Composite([ble], new Clock());
        using var _ = composite;
        using var inside = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        ear.WhenBeat = bpm =>
        {
            if (bpm != 1) return;
            inside.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        };

        var first = Task.Run(() => ble.Send(1));
        Assert.True(inside.Wait(TimeSpan.FromSeconds(10)));
        var second = Task.Run(() => ble.Send(2));
        Assert.False(second.Wait(300), "the second notice returned before it was delivered");

        release.Set();
        Assert.True(second.Wait(TimeSpan.FromSeconds(10)));
        lock (ear.Seen) Assert.Equal([1, 2], ear.Beats);
        Assert.True(first.Wait(TimeSpan.FromSeconds(10)));
    }

    /// <summary>口を閉じる前に届く。モジュールはこのあとで畳まれる。</summary>
    [Fact]
    public async Task StoppingAnnouncesTheDisconnectBeforeReturning()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var (composite, ear) = Composite([ble], new Clock());
        using var _ = composite;
        using var stop = new CancellationTokenSource();
        var running = composite.RunAsync(DeviceSelector.Any, stop.Token);
        ble.Send(60);

        await stop.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(["connected ベルト BleGatt", "beat 60", "disconnected ベルト Shutdown"], ear.Seen);
        Assert.False(composite.Receiving);
    }

    /// <summary>モジュールは設定を変えるたびに受け直すので、2 周目に線が切れたら「切れた（LinkLost）」で、「やめた（Shutdown）」ではない。</summary>
    [Fact]
    public async Task ASecondRunReportsALostLinkAsLostAndNotAsAStop()
    {
        var ble = new FakeInput(TimeSpan.FromSeconds(10), "ベルト");
        var (composite, ear) = Composite([ble], new Clock());
        using var _ = composite;

        using (var first = new CancellationTokenSource())
        {
            var running = composite.RunAsync(DeviceSelector.Any, first.Token);
            ble.Send(60);
            await first.CancelAsync();
            await running.WaitAsync(TimeSpan.FromSeconds(10));
        }

        using var second = new CancellationTokenSource();
        var again = composite.RunAsync(DeviceSelector.Any, second.Token);
        ble.Send(61);
        ble.Drop();

        Assert.Equal(
            ["connected ベルト BleGatt", "beat 60", "disconnected ベルト Shutdown", "connected ベルト BleGatt", "beat 61", "disconnected ベルト LinkLost"],
            ear.Seen);

        await second.CancelAsync();
        await again.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
