using System.Text.Json;
using SpoitableToolKit.Modules.Hrs;
using SpoitableToolKit.Modules.Hrs.Devices.Interfaces;
using SpoitableToolKit.Modules.Hrs.Devices.Models;
using SpoitableToolKit.Tests.Fakes;
using Xunit;

namespace SpoitableToolKit.Tests.Hrs.Panel;

/// <summary>パネルの「心拍計」タブの一覧（push の <c>status</c> の <c>discovered</c> と <c>getMonitors</c>）。実機の BLE 無しで、入口を差し替えて見る。</summary>
public sealed class HrsMonitorListTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 16, 3, 0, 0, TimeSpan.Zero);

    private static readonly MonitorIdentity Belt = new(0xA09E1A000001, "Polar");
    private static readonly MonitorIdentity LongerName = new(0xA09E1A000002, "Polar H10 1234");
    private static readonly MonitorIdentity SameName = new(0xA09E1A000003, "Polar");

    private record Row(string Name, string? Address, int? Signal, string? LastSeen, bool Connected);

    private static Row[] Rows(string json)
    {
        using var document = JsonDocument.Parse(json);

        return
        [
            .. document.RootElement.GetProperty("discovered").EnumerateArray().Select(row => new Row(
                row.GetProperty("name").GetString()!,
                row.TryGetProperty("address", out var address) ? address.GetString() : null,
                row.TryGetProperty("signal", out var signal) ? signal.GetInt32() : null,
                row.TryGetProperty("lastSeen", out var seen) ? seen.GetString() : null,
                row.GetProperty("connected").GetBoolean())),
        ];
    }

    /// <summary>
    /// 「接続中」はアドレスで見る。名前の前方一致で見ると「Polar」に繋がっているときに「Polar H10 …」にも、名前で見ると同じ名前の別の 1 台にも印が付く。
    /// 電波の強さと最後に見た時刻は、入口が持っている値のまま出す（「〜前」はパネルが数える。繋がっている 1 台も、広告が新しければそのまま）。
    /// </summary>
    [Fact]
    public async Task TheStatusListsEachMonitorWithItsSignalAndMarksTheConnectedOneByAddress()
    {
        var module = Start(new SeenSource(Belt,
            new MonitorSighting(Belt, -61, At),
            new MonitorSighting(LongerName, -75, At.AddSeconds(-30)),
            new MonitorSighting(SameName, -90, At.AddMinutes(-2))));
        try
        {
            await WaitFor(() => Rows(Status(module)).Any(row => row.Connected));

            Row[] rows = Rows(Status(module));
            Assert.Equal(
            [
                new Row("Polar", "A0:9E:1A:00:00:01", -61, At.ToString("O"), true),
                new Row("Polar H10 1234", "A0:9E:1A:00:00:02", -75, At.AddSeconds(-30).ToString("O"), false),
                new Row("Polar", "A0:9E:1A:00:00:03", -90, At.AddMinutes(-2).ToString("O"), false),
            ], rows);
        }
        finally
        {
            module.OnShutdown();
        }
    }

    /// <summary>
    /// タブを開いている間は数秒おきに読み直すので、<c>getMonitors</c> は一覧だけを返し、記録は数えない（<c>getStatus</c> は数える）。並びは <c>getStatus</c> と同じ。
    /// </summary>
    [Fact]
    public async Task TheMonitorListIsReadWithoutCountingTheRecords()
    {
        var module = Start(new SeenSource(Belt, new MonitorSighting(Belt, -61, At), new MonitorSighting(LongerName, -75, At)));
        try
        {
            await WaitFor(() => Rows(Status(module)).Any(row => row.Connected));

            var monitors = await module.HandleUiCallAsync("getMonitors", "");

            using (var document = JsonDocument.Parse(monitors))
                Assert.Equal(["discovered"], document.RootElement.EnumerateObject().Select(property => property.Name));

            Row[] listed = Rows(monitors);
            Assert.Equal(Rows(Status(module)), listed);
            Assert.Equal(2, listed.Length);
        }
        finally
        {
            module.OnShutdown();
        }
    }

    /// <summary>
    /// GATT で繋ぐと広告を止める機種がある。GATT で繋がっている 1 台の最後の広告が古ければ、その時刻と電波の強さは出さずに「いま」とする
    /// （残っているのは繋いだときの広告で、そのままだと「接続中・2 分前」になる）。繋がっていない 1 台は広告のまま。
    /// </summary>
    [Fact]
    public async Task AConnectedMonitorThatStoppedAdvertisingIsSeenNowWithoutAStaleSignal()
    {
        var module = Start(new SeenSource(Belt,
            new MonitorSighting(Belt, -61, At.AddMinutes(-2)),
            new MonitorSighting(LongerName, -75, At.AddMinutes(-2))));
        try
        {
            await WaitFor(() => Rows(Status(module)).Any(row => row.Connected));

            Row[] rows = Rows(await module.HandleUiCallAsync("getMonitors", ""));
            Assert.Equal(
            [
                new Row("Polar", "A0:9E:1A:00:00:01", null, At.ToString("O"), true),
                new Row("Polar H10 1234", "A0:9E:1A:00:00:02", -75, At.AddMinutes(-2).ToString("O"), false),
            ], rows);
        }
        finally
        {
            module.OnShutdown();
        }
    }

    /// <summary>
    /// 広告から心拍を読んでいる 1 台（GATT が張れずに広告へ落ちた Polar）は、広告しか届かない。広告が古いのは本当に黙ったということなので、
    /// 「いま」にせず、ほかの行と同じく最後に見た時刻と電波の強さをそのまま出す（入口が手放すまで「接続中・たった今」と言い続けない）。
    /// </summary>
    [Fact]
    public async Task AMonitorReadFromItsAdvertisementsKeepsItsRealAgeAndSignal()
    {
        var module = Start(new SeenSource(Belt, new MonitorSighting(Belt, -61, At.AddMinutes(-2))) { Via = HeartRateConnection.BleBroadcastPolar });
        try
        {
            await WaitFor(() => Rows(Status(module)).Any(row => row.Connected));

            Row[] rows = Rows(await module.HandleUiCallAsync("getMonitors", ""));
            Assert.Equal([new Row("Polar", "A0:9E:1A:00:00:01", -61, At.AddMinutes(-2).ToString("O"), true)], rows);
        }
        finally
        {
            module.OnShutdown();
        }
    }

    /// <summary>
    /// 電波の強さと時刻を持たない入口（既定の <see cref="IHeartRateSource.Sightings"/>）は、その 2 つを書かない（null は書かない）。
    /// 繋がっている 1 台は居るのが分かっているので、時刻だけ「いま」。
    /// </summary>
    [Fact]
    public async Task AMonitorWithoutASignalIsListedWithoutOne()
    {
        var module = Start(new PlainSource(Belt, Belt, LongerName));
        try
        {
            await WaitFor(() => Rows(Status(module)).Any(row => row.Connected));

            Row[] rows = Rows(await module.HandleUiCallAsync("getMonitors", ""));
            Assert.Equal(
            [
                new Row("Polar", "A0:9E:1A:00:00:01", null, At.ToString("O"), true),
                new Row("Polar H10 1234", "A0:9E:1A:00:00:02", null, null, false),
            ], rows);
        }
        finally
        {
            module.OnShutdown();
        }
    }

    /// <summary>窓口とパネルの「いま」を <see cref="At"/> に止めて動かす。</summary>
    private static HrsModule Start(IHeartRateSource source)
    {
        var module = new HrsModule { CreateSources = () => [source], Time = new Clock(At) };
        Assert.True(module.OnInit(new FakeModuleHost().WithConfig("hrs", module.Config!)));
        return module;
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static string Status(HrsModule module) =>
        module.HandleUiCallAsync("getStatus", "{}").GetAwaiter().GetResult();

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 60 && !condition(); i++) await Task.Delay(50);
    }

    /// <summary>見えている心拍計を持ち、<paramref name="connectTo"/> に繋がったふりをする入口。電波の強さと時刻は持たない（既定の <see cref="IHeartRateSource.Sightings"/>）。</summary>
    private class PlainSource(MonitorIdentity connectTo, params MonitorIdentity[] discovered) : IHeartRateSource
    {
        public IReadOnlyList<MonitorIdentity> Discovered => discovered;

        public MonitorIdentity? Connected { get; private set; }

        public HeartRateConnection Connection => Connected is null ? HeartRateConnection.None : Via;

        /// <summary>繋がっているときの繋がり方。既定は GATT。</summary>
        public HeartRateConnection Via { get; init; } = HeartRateConnection.BleGatt;

        public TimeSpan SilenceAfter => TimeSpan.FromSeconds(10);

        private IHeartRateSink? _sink;

        public void ReportTo(IHeartRateSink sink) => _sink = sink;

        public void Disconnect(MonitorIdentity monitor)
        {
            Connected = null;
            _sink?.LinkChanged(this);
        }

        public void Connect(MonitorIdentity monitor)
        {
            Connected = connectTo;
            _sink?.LinkChanged(this);
        }

        public async Task RunAsync(DeviceSelector selector, CancellationToken cancellationToken)
        {
            Connected = connectTo;
            _sink?.LinkChanged(this);

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Connected = null;
            }
        }
    }

    /// <summary>BLE の入口と同じく、電波の強さと最後に見た時刻も持つ。</summary>
    private sealed class SeenSource(MonitorIdentity connectTo, params MonitorSighting[] seen)
        : PlainSource(connectTo, [.. seen.Select(s => s.Monitor)]), IHeartRateSource
    {
        public IReadOnlyList<MonitorSighting> Sightings => seen;
    }
}
