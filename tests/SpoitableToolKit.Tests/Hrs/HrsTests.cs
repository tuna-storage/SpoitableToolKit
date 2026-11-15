using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.Shortcuts;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Host.Config.Models;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Modules.Hrs;
using SpoitableToolKit.Modules.Hrs.Avatar;
using SpoitableToolKit.Modules.Hrs.Config;
using SpoitableToolKit.Modules.Hrs.Devices.Ble;
using SpoitableToolKit.Modules.Hrs.Devices.Hds;
using SpoitableToolKit.Modules.Hrs.Devices.Interfaces;
using SpoitableToolKit.Modules.Hrs.Devices.Models;
using SpoitableToolKit.Modules.Hrs.Events.Session;
using SpoitableToolKit.Modules.Hrs.Recording;
using SpoitableToolKit.Tests.Fakes;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Hrs;

public sealed class HrsTests
{
    /// <summary>flags の bit0 が「16 ビットか」。決め打ちにすると片方の機種で桁が壊れる。</summary>
    [Fact]
    public void HeartRateIsReadAsEitherEightOrSixteenBits()
    {
        // flags=0x00 → 8 ビット
        Assert.Equal(72, HrsMeasurement.Parse([0x00, 72]));

        // flags=0x01 → 16 ビットのリトルエンディアン (0x0104 = 260)
        Assert.Equal(260, HrsMeasurement.Parse([0x01, 0x04, 0x01]));
    }

    /// <summary>短いパケットは実機でも来る。例外にせず読めなかったことにする。</summary>
    [Fact]
    public void AShortPacketIsNotRead()
    {
        Assert.Null(HrsMeasurement.Parse([]));
        Assert.Null(HrsMeasurement.Parse([0x00]));

        // 16 ビットと言っているのに 1 バイトしかない。
        Assert.Null(HrsMeasurement.Parse([0x01, 0x04]));
    }

    [Fact]
    public void AnEmptySelectorConnectsToTheFirstMonitorFound()
    {
        Assert.True(DeviceSelector.TryParse("", out var selector, out _));
        Assert.True(selector.Matches(new MonitorIdentity(0x001122334455, "なんでも")));
    }

    [Fact]
    public void BracketsMeanTheSelectorIsAnAddress()
    {
        Assert.True(DeviceSelector.TryParse("Polar H10 [A1:B2:C3:D4:E5:F6]", out var selector, out _));

        Assert.True(selector.Matches(new MonitorIdentity(0xA1B2C3D4E5F6, "名前は違ってもよい")));
        Assert.False(selector.Matches(new MonitorIdentity(0x000000000001, "Polar H10")));
    }

    /// <summary>手で名前だけ書く人向け。候補から選べば角括弧が付くが、強制はしない。</summary>
    [Fact]
    public void WithoutBracketsTheSelectorMatchesTheName()
    {
        Assert.True(DeviceSelector.TryParse("polar", out var selector, out _));

        Assert.True(selector.Matches(new MonitorIdentity(1, "Polar H10")));
        Assert.False(selector.Matches(new MonitorIdentity(2, "COOSPO H6")));
    }

    // 空扱いに倒すと、打ち間違えたときに黙って別のデバイスへ繋がる。
    [Fact]
    public void AnUnreadableAddressReturnsAReason()
    {
        Assert.False(DeviceSelector.TryParse("Polar [ZZ:ZZ]", out _, out var error));

        // 返すのは読めなかった文字列だけ。画面に出す文はモジュール側が組む（翻訳のため）。
        Assert.Equal("ZZ:ZZ", error);
    }

    [Fact]
    public void ASuggestionIsOneLineWithTheAddress()
    {
        Assert.Equal("Polar H10 [A1:B2:C3:D4:E5:F6]",
            new MonitorIdentity(0xA1B2C3D4E5F6, "Polar H10").ToSuggestion());
    }

    private static readonly DateTimeOffset Noon = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

    // 常駐したまま何時間も動くので、切らないと 1 秒に 1 件ずつ増え続ける。
    [Fact]
    public void TheRecentBeatsDropTheOldestAtTheCap()
    {
        var log = new HeartRateLog(capacity: 3);

        for (var bpm = 60; bpm < 65; bpm++) log.Add(bpm, Noon.AddSeconds(bpm));

        // 古い順に並ぶ。グラフはそのまま左から右へ描ける。
        Assert.Equal([62, 63, 64], log.Recent.Select(s => s.Bpm));
    }

    // 前のデバイスの値を後ろに繋げると、1 本の線に見えるのに別人の心拍が混ざる。
    [Fact]
    public void ReconnectingClearsTheRecentBeats()
    {
        var log = new HeartRateLog();
        log.Add(60, Noon);

        log.Clear();

        Assert.Empty(log.Recent);
    }

    // 繋がるたびに呼ぶもので、同じ人が付け直しただけなので直近のグラフは残す（消すのは繋ぎ先を変えたときの HeartRateLog.Clear）。
    [Fact]
    public void RestartingTheStatsKeepsTheRecentBeats()
    {
        var log = new HeartRateLog();
        log.Add(60, Noon);
        log.Add(100, Noon.AddSeconds(1));

        log.RestartStats();

        Assert.Equal([60, 100], log.Recent.Select(s => s.Bpm));
        Assert.Equal(default, log.Overall);

        log.Add(70, Noon.AddSeconds(2));

        Assert.Equal(new HeartRateStats(70, 70, 70), log.Overall);
    }

    // 切れた所はグラフに 0 を残す。続けて切れても 1 点、次の 1 拍の前にもう 1 点 0（線は 0 のまま伸びてから上がる）。最小・平均・最大には数えない。まだ 1 点も無ければ置かない。
    [Fact]
    public void AGapLeavesZerosInTheChartButNotInTheStats()
    {
        var log = new HeartRateLog();
        log.Gap(Noon);
        Assert.Empty(log.Recent);

        log.Add(80, Noon.AddSeconds(1));
        log.Gap(Noon.AddSeconds(2));
        log.Gap(Noon.AddSeconds(3));
        log.Add(90, Noon.AddSeconds(10));
        log.Add(100, Noon.AddSeconds(11));

        Assert.Equal([80, 0, 0, 90, 100], log.Recent.Select(s => s.Bpm));
        Assert.Equal(new HeartRateStats(80, 100, 90), log.Overall);

        // 繋ぎ先を変えて畳んだら、切れていた印も忘れる（新しい心拍計の線は 0 から始めない）。
        log.Gap(Noon.AddSeconds(12));
        log.Clear();
        log.Add(70, Noon.AddSeconds(20));
        Assert.Equal([70], log.Recent.Select(s => s.Bpm));
    }

    [Fact]
    public async Task DisconnectingAndConnectingLeavesTheChartAtZeroInBetween()
    {
        var (module, host, source) = NewModule();
        try
        {
            await WaitFor(() => host.Osc.Last("/avatar/parameters/HR") is 72);

            await module.HandleUiCallAsync("disconnect", "{}");
            await module.HandleUiCallAsync("connect", "{}");
            source.Beat(75);

            using var status = JsonDocument.Parse(Status(module));
            int[] chart = [.. status.RootElement.GetProperty("samples").EnumerateArray().Select(s => s.GetProperty("bpm").GetInt32())];
            Assert.Equal([72, 0, 0, 75], chart);
        }
        finally
        {
            module.OnShutdown();
        }
    }

    // 記録と最小・平均・最大には入れない。
    [Fact]
    public async Task AZeroFromTheMonitorDropsTheChartButNotTheStats()
    {
        var (module, host, source) = NewModule();
        try
        {
            await WaitFor(() => host.Osc.Last("/avatar/parameters/HR") is 72);

            source.Beat(0);
            source.Beat(0);
            source.Beat(80);

            using var status = JsonDocument.Parse(Status(module));
            int[] chart = [.. status.RootElement.GetProperty("samples").EnumerateArray().Select(s => s.GetProperty("bpm").GetInt32())];
            Assert.Equal([72, 0, 0, 80], chart);
            Assert.Equal(72, status.RootElement.GetProperty("stats").GetProperty("min").GetInt32());
        }
        finally
        {
            module.OnShutdown();
        }
    }

    // 実機の BLE 無しで見るために、心拍の出どころを差し替える。

    /// <summary>session の「アバターへ止まったを送った」を配る口（試験では誰も聞かない。owner はどれでも通す）。</summary>
    private static IEventHub SessionEvents() =>
        new EventHubs(_ => true, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
            .CreateHub(HostOwner.Instance, [typeof(IHeartRateSessionEventListener)]);

    // 繋いだふりをして、畳まれるまで動き続ける出どころ。後片付けにわざと間を作るので、ホストが待っているかどうかが分かる。Watching は本物の watcher と同じく周回をまたいで共有される状態で、前の周回の後片付けが新しい周回を追い越すとここが false になる。
    private sealed class FakeSource : IHeartRateSource
    {
        /// <summary>本物が BLE の広告を見張っているかどうかに当たる。</summary>
        public bool Watching { get; private set; }

        public IReadOnlyList<MonitorIdentity> Discovered => [new(0xA1B2C3D4E5F6, "Fake")];

        public MonitorIdentity? Connected { get; private set; }

        public HeartRateConnection Connection =>
            Connected is null ? HeartRateConnection.None : Via;

        /// <summary>繋がっているときの入口。BLE と時計の乗り換えを試験から起こす。</summary>
        public HeartRateConnection Via { get; set; } = HeartRateConnection.BleGatt;

        public TimeSpan SilenceAfter => TimeSpan.FromSeconds(10);

        public int? Battery { get; set; }

        private IHeartRateSink? _sink;

        public void ReportTo(IHeartRateSink sink) => _sink = sink;

        /// <summary>同じ繋がりのまま「変わった」と知らせる（入口と調停の両方から同じ知らせが来る形）。</summary>
        public void Nudge() => _sink?.LinkChanged(this);

        /// <summary>後片付けが済んだか。畳んだあとに false なら、待たずに抜けている。</summary>
        public bool TornDown { get; private set; }

        public int Runs { get; private set; }

        /// <summary>立っていれば、後片付けの途中でこれが立つまで待つ（前の周回が畳み終わらない形を試験から作る）。</summary>
        public ManualResetEventSlim? HoldTeardown { get; set; }

        public void Drop()
        {
            Connected = null;
            _sink?.LinkChanged(this);
        }

        /// <summary>同じ心拍計に繋がり直した（付け直した・圏内へ戻った）。周回はそのまま — パネルの「切断」→「接続」で作り直すと、この偽物は畳むときに切れたと知らせない（本物は知らせる）。</summary>
        public void Reconnect()
        {
            Connected = Discovered[0];
            _sink?.LinkChanged(this);
        }

        public void Beat(int bpm) => _sink?.Beat(this, bpm);

        /// <summary>手で切られている相手（窓口から）。「接続」で外れる。</summary>
        public MonitorIdentity? HeldOff { get; private set; }

        /// <summary>手で切られた。本物と同じく線を放す（周回はそのまま）。</summary>
        public void Disconnect(MonitorIdentity monitor)
        {
            HeldOff = monitor;
            Connected = null;
            _sink?.LinkChanged(this);
        }

        /// <summary>また繋いでよい。本物は次の周回で繋ぎ直す — ここではその場で繋がる。</summary>
        public void Connect(MonitorIdentity monitor)
        {
            HeldOff = null;
            Connected = Discovered[0];
            _sink?.LinkChanged(this);
        }

        public async Task RunAsync(DeviceSelector selector, CancellationToken cancellationToken)
        {
            Runs++;
            Watching = true;
            Connected = Discovered[0];
            _sink?.LinkChanged(this);
            _sink?.Beat(this, 72);

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // GATT の解放も watcher の停止もすぐには終わらない。
                // 待っていなければ、次の周回がここを追い越す。
                await Task.Delay(50, CancellationToken.None).ConfigureAwait(false);
                HoldTeardown?.Wait(TimeSpan.FromSeconds(10));
                Connected = null;
                Watching = false;
                TornDown = true;
            }
        }
    }

    /// <param name="configure">OnInit の前に設定へ書く。ホストは保存済みの値を読み込んでから OnInit を呼ぶので、「設定ファイルにこう入っていた」状態はここで作る。</param>
    private static (HrsModule Module, FakeModuleHost Host, FakeSource Source) NewModule(
        Action<HrsConfig>? configure = null, Logs? logs = null)
    {
        var source = new FakeSource();
        var module = new HrsModule { CreateSources = () => [source] };
        var host = (logs is null ? new FakeModuleHost() : FakeModuleHost.LoggingTo(logs)).WithConfig("hrs", module.Config!);

        configure?.Invoke((HrsConfig)module.Config!);

        Assert.True(module.OnInit(host));
        return (module, host, source);
    }

    /// <summary>
    /// 設定画面の「適用」と同じ道で変える（本物の置き場: 読んで確かめる → 聞く → 断られなかった値を書いて保存 → 知らせる）。
    /// 断られた欄があれば、その欄の key と理由を返す（全部入れば空）。
    /// </summary>
    private static IReadOnlyList<ConfigNote> Apply(FakeModuleHost host, string json)
    {
        try
        {
            host.Configs!.Apply("hrs", (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(json)!, new FakeLocalizer());
            return [];
        }
        catch (ConfigRejectedException ex)
        {
            return ex.Notes;
        }
    }

    // 通算には入れず（入れると「最小 0」になる）、サイドバーは前の数字を残さず「計れていない」にする。心拍計は肌から離れると 0 を送る。グラフだけは 0 に落とす（切れたときと同じ）。
    [Fact]
    public async Task AZeroBeatIsNotRecordedAndShowsNoReading()
    {
        var (module, host, source) = NewModule();
        try
        {
            // 繋いだ直後に 72 が 1 拍来る（繋ぐ周回は別のスレッド）。
            await WaitFor(() => host.Ui.Status!.Text == "72 bpm");
            Assert.Equal("72 bpm", host.Ui.Status!.Text);

            source.Beat(0);

            Assert.Equal("— bpm", host.Ui.Status.Text);
            Assert.Equal(StatusState.Off, host.Ui.Status.State);

            using var status = JsonDocument.Parse(Status(module));
            var samples = status.RootElement.GetProperty("samples").EnumerateArray().Select(s => s.GetProperty("bpm").GetInt32());
            Assert.Equal([72, 0], samples);

            // 計れるようになったら元へ戻る。
            source.Beat(80);
            Assert.Equal("80 bpm", host.Ui.Status.Text);
            Assert.Equal(StatusState.Ok, host.Ui.Status.State);
        }
        finally
        {
            module.OnShutdown();
        }
    }

    // 待たずに抜けると GATT の解放が途中で終わり、次の起動で繋がりにくくなる。
    [Fact]
    public void ShutdownWaitsForTheCleanupToFinish()
    {
        var (module, _, source) = NewModule();

        module.OnShutdown();

        Assert.True(source.TornDown);
        Assert.Null(source.Connected);
    }

    // 間に合わないと、ホストが OSC を畳んだあとの送信になり、アバターは最後に受け取った心拍を出したままになる。
    [Fact]
    public void ShutdownTellsTheAvatarItIsNotConnected()
    {
        var (module, host, _) = NewModule();

        module.OnShutdown();

        Assert.Equal(false, host.Osc.Last("/avatar/parameters/isHRConnected"));
        Assert.Equal(0, host.Osc.Last("/avatar/parameters/HR"));
    }

    // 待たずに始めると、前の周回の後片付けが新しく張った走査を止めてしまい、デバイスを変えた途端に静かに動かなくなる。
    [Fact]
    public async Task ReconnectingKeepsTheScanRunning()
    {
        var (module, host, source) = NewModule();

        Assert.Empty(Apply(host, """{ "Device": "Polar H10 [A1:B2:C3:D4:E5:F6]" }"""));

        // 前の周回の後片付け（50ms）が済むだけの間を置いてから見る。
        await Task.Delay(400);

        Assert.Equal(2, source.Runs);
        Assert.True(source.Watching);

        module.OnShutdown();
        Assert.True(source.TornDown);
        Assert.False(source.Watching);
    }

    // 開き直さないと、設定で有効にしても次の起動まで口が開かない。ファイアウォールを見る係は差し替える（本物は netsh を起こすので、試験機の設定を覗きに行く）。
    [Fact]
    public async Task EnablingHdsReopensTheListener()
    {
        var source = new FakeSource();
        var checks = 0;
        var module = new HrsModule
        {
            CreateSources = () => [source],
            CheckFirewall = () =>
            {
                checks++;
                return HdsFirewall.State.Present;
            },
        };

        var host = new FakeModuleHost().WithConfig("hrs", module.Config!);
        Assert.True(module.OnInit(host));
        var beforeChange = checks;

        Assert.Empty(Apply(host, """{ "Hds.Enabled": true }"""));
        Assert.True(((HrsConfig)module.Config!).Hds.Enabled);

        // 前の周回の後片付け（50ms）と、裏で走る確認が済むだけの間を置く。
        await Task.Delay(400);

        Assert.Equal(2, source.Runs);
        Assert.Equal(beforeChange + 1, checks);

        module.OnShutdown();
    }

    // 「切断」は繋がっている心拍計だけを切り、「接続」で戻る。探すのと時計の待ち受けは続き（周回は作り直さない）、「接続」を押すまでその 1 台には繋ぎ直さない。他のアプリに GATT を掴ませてから繋ぎ直す、といった確かめ方もできる。
    // アバターへは HR 0 で、bool は全部 false（繋がっている・動いている・拍）。送らずに黙ると、最後の心拍を出したままになる。
    // 周回は生きていて探し続けるが、アバターに見せるのは「繋がっていない」。
    [Fact]
    public async Task TheMonitorCanBeDroppedAndRemadeByHand()
    {
        var (module, host, source) = NewModule();
        try
        {
            await WaitFor(() => host.Osc.Last("/avatar/parameters/HR") is 72);

            await module.HandleUiCallAsync("disconnect", "{}");
            await WaitFor(() => host.Osc.Last("/avatar/parameters/isHRActive") is false);

            Assert.False(source.TornDown);
            Assert.NotNull(source.HeldOff);
            Assert.Equal(false, host.Osc.Last("/avatar/parameters/isHRActive"));
            Assert.Equal(0, host.Osc.Last("/avatar/parameters/HR"));
            Assert.Equal(false, host.Osc.Last("/avatar/parameters/isHRConnected"));

            // 切ってある間に届いた 1 拍は受けない。
            source.Beat(90);

            using (var off = JsonDocument.Parse(Status(module)))
            {
                Assert.False(off.RootElement.GetProperty("connected").GetBoolean());
                Assert.True(off.RootElement.GetProperty("running").GetBoolean());
                Assert.Equal("Fake [A1:B2:C3:D4:E5:F6]", off.RootElement.GetProperty("heldOff").GetString());
                Assert.Equal(0, off.RootElement.GetProperty("heartRate").GetInt32());
            }

            await module.HandleUiCallAsync("connect", "{}");
            source.Beat(75);
            await WaitFor(() => host.Osc.Last("/avatar/parameters/HR") is 75);

            Assert.Equal(1, source.Runs);
            Assert.Null(source.HeldOff);
            using var on = JsonDocument.Parse(Status(module));
            Assert.True(on.RootElement.GetProperty("connected").GetBoolean());
            // null の欄は書かない（パネルの JSON の決まり）。
            Assert.False(on.RootElement.TryGetProperty("heldOff", out var held) && held.ValueKind != JsonValueKind.Null);
        }
        finally
        {
            module.OnShutdown();
        }
    }

    // 残すと、新しい心拍計が離れたときに前に切った 1 台が「切断してあります」と出て、探している最中の「切断」も出ない。
    [Fact]
    public async Task ChoosingAnotherMonitorForgetsTheOneDisconnectedByHand()
    {
        var (module, host, source) = NewModule();
        try
        {
            await WaitFor(() => host.Osc.Last("/avatar/parameters/HR") is 72);
            await module.HandleUiCallAsync("disconnect", "{}");
            Assert.NotNull(source.HeldOff);

            Assert.Empty(Apply(host, """{ "Device": "Polar H10 [A1:B2:C3:D4:E5:F6]" }"""));
            await WaitFor(() => source.Runs == 2);

            Assert.Null(source.HeldOff);
            using var status = JsonDocument.Parse(Status(module));
            Assert.False(status.RootElement.TryGetProperty("heldOff", out var held) && held.ValueKind != JsonValueKind.Null);
        }
        finally
        {
            module.OnShutdown();
        }
    }

    // 何も繋がっていないとき（探している最中）の「切断」は受けるのを止める（アバターへ「繋がっていない」）。「接続」で受け始め、手で切った印も外れる。
    [Fact]
    public async Task DisconnectingWhileNothingIsConnectedStopsCapturing()
    {
        var (module, host, source) = NewModule();
        await WaitFor(() => host.Osc.Last("/avatar/parameters/HR") is 72);

        await module.HandleUiCallAsync("disconnect", "{}");
        await module.HandleUiCallAsync("disconnect", "{}");
        await WaitFor(() => source.TornDown);

        Assert.True(source.TornDown);
        Assert.Equal(false, host.Osc.Last("/avatar/parameters/isHRConnected"));

        await module.HandleUiCallAsync("connect", "{}");
        await WaitFor(() => source.Runs == 2);

        Assert.Equal(2, source.Runs);
        Assert.True(source.Watching);
        Assert.Null(source.HeldOff);

        module.OnShutdown();
    }

    // 乗り換えではなく、アバターへ「動いていない」を送る。
    [Fact]
    public async Task DisconnectingTheCarrierWithOnlyAnIdleMonitorLeftTellsTheAvatarItStopped()
    {
        var source = new FakeSource();
        var module = new HrsModule { CreateSources = () => [new IdleSource(), source] };
        var host = new FakeModuleHost().WithConfig("hrs", module.Config!);
        Assert.True(module.OnInit(host));
        try
        {
            await WaitFor(() => host.Osc.Last("/avatar/parameters/HR") is 72);

            await module.HandleUiCallAsync("disconnect", "{}");
            await WaitFor(() => host.Osc.Last("/avatar/parameters/isHRActive") is false);

            Assert.Equal(false, host.Osc.Last("/avatar/parameters/isHRActive"));
            using var status = JsonDocument.Parse(Status(module));
            Assert.Equal("Idle", status.RootElement.GetProperty("device").GetString());
            Assert.False(status.RootElement.GetProperty("receiving").GetBoolean());
            Assert.Equal("Fake [A1:B2:C3:D4:E5:F6]", status.RootElement.GetProperty("heldOff").GetString());
        }
        finally
        {
            module.OnShutdown();
        }
    }

    /// <summary>繋がっているだけで、1 拍も送らない入口（充電器の上のベルト）。</summary>
    private sealed class IdleSource : IHeartRateSource
    {
        public IReadOnlyList<MonitorIdentity> Discovered => [];

        public MonitorIdentity? Connected { get; private set; }

        public HeartRateConnection Connection => Connected is null ? HeartRateConnection.None : HeartRateConnection.BleGatt;

        public TimeSpan SilenceAfter => TimeSpan.FromSeconds(10);

        public int? Battery => null;

        private IHeartRateSink? _sink;

        public void ReportTo(IHeartRateSink sink) => _sink = sink;

        public void Disconnect(MonitorIdentity monitor)
        {
            Connected = null;
            _sink?.LinkChanged(this);
        }

        public void Connect(MonitorIdentity monitor)
        {
            Connected = new MonitorIdentity(0x0A0B0C0D0E0F, "Idle");
            _sink?.LinkChanged(this);
        }

        public async Task RunAsync(DeviceSelector selector, CancellationToken cancellationToken)
        {
            Connected = new MonitorIdentity(0x0A0B0C0D0E0F, "Idle");
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

    // 心拍を運んでいた入口が黙り、残ったのが 1 拍も送らない入口だけなら、アバターへ「動いていない」を送る（ページは「接続済み・心拍なし」）。窓口がこれを「乗り換え」と言うと、モジュールがアバターを倒さず、黙った入口の最後の心拍を出し続ける。
    [Fact]
    public async Task WhenTheCarrierGoesQuietAndOnlyAnIdleMonitorIsLeftTheAvatarIsToldItStopped()
    {
        var clock = new Clock();
        var source = new FakeSource();
        var module = new HrsModule { CreateSources = () => [new IdleSource(), source], Time = clock };
        var host = new FakeModuleHost().WithConfig("hrs", module.Config!);
        Assert.True(module.OnInit(host));
        try
        {
            await WaitFor(() => host.Osc.Last("/avatar/parameters/HR") is 72);

            clock.Now = clock.Now.AddSeconds(11);
            await WaitFor(() => host.Osc.Last("/avatar/parameters/isHRActive") is false);

            Assert.Equal(false, host.Osc.Last("/avatar/parameters/isHRActive"));
            using var quiet = JsonDocument.Parse(Status(module));
            Assert.Equal("Idle", quiet.RootElement.GetProperty("device").GetString());
            Assert.False(quiet.RootElement.GetProperty("receiving").GetBoolean());
            Assert.Equal(0, quiet.RootElement.GetProperty("heartRate").GetInt32());
        }
        finally
        {
            module.OnShutdown();
        }
    }

    // ページは「接続済み・心拍なし」（「受信中」は心拍が届いている間だけ）。bpm は前の数字を残さず、アバターへは「動いていない」（繋がったまま）を、入口の黙ってよい長さで送る。次の 1 拍で「受信中」に戻る。窓口の時計を進めて起こす（本物の秒数は待たない — 見張りは 250ms おき）。
    [Fact]
    public async Task AQuietBeltShowsConnectedWithoutAHeartRateUntilItBeatsAgain()
    {
        var clock = new Clock();
        var source = new FakeSource();
        var module = new HrsModule { CreateSources = () => [source], Time = clock };
        var host = new FakeModuleHost().WithConfig("hrs", module.Config!);
        Assert.True(module.OnInit(host));
        try
        {
            await WaitFor(() => host.Osc.Last("/avatar/parameters/HR") is 72);
            using (var receiving = JsonDocument.Parse(Status(module)))
                Assert.True(receiving.RootElement.GetProperty("receiving").GetBoolean());

            int pushedBefore;
            lock (host.Panel.Pushed) pushedBefore = host.Panel.Pushed.Count;

            clock.Now = clock.Now.AddSeconds(11);
            await WaitFor(() => host.Osc.Last("/avatar/parameters/isHRActive") is false);

            // 心拍なしと知った瞬間にパネルへ送った中身で、もう前の数字を持っていない（session を倒してから送る）。
            string[] pushed;
            lock (host.Panel.Pushed) pushed = [.. host.Panel.Pushed.Skip(pushedBefore).Where(p => p.Channel == "status").Select(p => p.Payload)];
            using (var first = JsonDocument.Parse(pushed.First(p => !JsonDocument.Parse(p).RootElement.GetProperty("receiving").GetBoolean())))
                Assert.Equal(0, first.RootElement.GetProperty("heartRate").GetInt32());

            // 心拍計は繋がったままでも、アバターには「繋がっていない」（0 なら全部 false）。ページは「接続済み・心拍なし」。
            Assert.Equal(false, host.Osc.Last("/avatar/parameters/isHRActive"));
            Assert.Equal(false, host.Osc.Last("/avatar/parameters/isHRConnected"));
            using (var quiet = JsonDocument.Parse(Status(module)))
            {
                Assert.True(quiet.RootElement.GetProperty("connected").GetBoolean());
                Assert.False(quiet.RootElement.GetProperty("receiving").GetBoolean());
                Assert.Equal(0, quiet.RootElement.GetProperty("heartRate").GetInt32());
            }

            source.Beat(80);

            using var back = JsonDocument.Parse(Status(module));
            Assert.True(back.RootElement.GetProperty("receiving").GetBoolean());
            Assert.Equal(80, back.RootElement.GetProperty("heartRate").GetInt32());

        // グラフは黙っていた間を 0 にする。
            int[] chart = [.. back.RootElement.GetProperty("samples").EnumerateArray().Select(s => s.GetProperty("bpm").GetInt32())];
            Assert.Equal([72, 0, 0, 80], chart);
        }
        finally
        {
            module.OnShutdown();
        }
    }

    // 前の周回が詰まって、アプリが畳むのを待ち切れなかった形（畳んだ口へは足せない）。外で投げると周回が黙って落ちるので、周回の中で受け止めて 1 行残す。
    [Fact]
    public async Task ARoundThatStartsAfterTheModuleWasPutAwayLogsInsteadOfFailingSilently()
    {
        var logs = new Logs();
        var (module, host, source) = NewModule(logs: logs);
        try
        {
            await WaitFor(() => source.Runs == 1 && source.Watching);
            using var hold = new ManualResetEventSlim();
            source.HoldTeardown = hold;

            // 1 周目は後片付けの途中で止まっている。2 周目がそれを待つ間に、アプリがモジュールを畳んだ（口を閉じた）。
            await module.HandleUiCallAsync("connect", "{}");
            host.Hubs.Forget(module);
            hold.Set();
            await WaitFor(() => logs.All().Contains("Heart rate capture stopped"));

            Assert.Contains("Heart rate capture stopped", logs.All());
            Assert.Equal(1, source.Runs);
        }
        finally
        {
            module.OnShutdown();
        }
    }

    // 通れなさそうならパネルへ伝える（ボタンを出すのはパネル側）。アプリからは勝手に足さない（設定を保存しただけで UAC が出るのは筋が悪い）。
    // ブロックも同じ扱い: Windows の確認でキャンセルすると作られ、ブロックは許可より強いので、放っておくと二度と届かない。
    [Theory]
    [InlineData("missing")]
    [InlineData("blocked")]
    public async Task ABlockedFirewallIsReportedToThePanel(string expected)
    {
        // 引数で enum を受けると、internal な型が public な試験メソッドに出てしまう。
        var state = expected == "blocked" ? HdsFirewall.State.Blocked : HdsFirewall.State.Missing;
        var added = 0;
        var seen = state;
        var module = new HrsModule
        {
            CreateSources = () => [new FakeSource()],
            CheckFirewall = () => seen,
            AddFirewall = () =>
            {
                added++;

                // 直したあとに見直すので、ここで答えを変える（本物も同じ道を通る）。
                seen = HdsFirewall.State.Present;
                return true;
            },
        };

        ((HrsConfig)module.Config!).Hds.Enabled = true;
        Assert.True(module.OnInit(new FakeModuleHost()));

        await WaitFor(() => Status(module).Contains($"\"firewall\":\"{expected}\""));
        Assert.Contains($"\"firewall\":\"{expected}\"", Status(module));

        // 押されたときだけ直しに行く。直ったらパネルの表示も消える。
        await module.HandleUiCallAsync("addFirewallRule", "{}");
        await WaitFor(() => Status(module).Contains("\"firewall\":\"present\""));

        Assert.Equal(1, added);
        Assert.Contains("\"firewall\":\"present\"", Status(module));

        module.OnShutdown();
    }

    private static string Status(HrsModule module) =>
        module.HandleUiCallAsync("getStatus", "{}").GetAwaiter().GetResult();

    // 手首のタイルに描く心拍。押すとパネルを開き、ページは同梱の web/tile.html。getHeartRate は記録を数えない軽い口で、push（status）と同じ名前で今の心拍と線の分を返す。
    [Fact]
    public async Task TheLiveTileOpensThePanelAndReadsTheBeatWithoutCountingTheRecords()
    {
        var (module, host, _) = NewModule();
        try
        {
            var live = Assert.Single(host.Shortcuts.Registered);
            Assert.Equal("live", live.Id);
            Assert.Equal("tile.html", live.Page);
            Assert.True(File.Exists(Path.Combine(RepoRoot(), "src", "SpoitableToolKit.Modules", "Hrs", "web", "tile.html")));

            // 押すと、心拍のパネルを空間の窓で開く（場所は付けない）。偽物の host.Ui は名前を "test" で頼む。
            // 開いている間の枠は、開くのと同じ画面を名乗る（偽物の host.Shortcuts も "test"）。
            await Assert.IsAssignableFrom<IClickableWristShortcut>(live.Handler).OnTriggerAsync(CancellationToken.None);
            Assert.Equal(["test"], host.Panel.Toggled);
            Assert.Equal("test", live.Marked);

            await WaitFor(() => host.Ui.Status!.Text == "72 bpm");

            using var beat = JsonDocument.Parse(await module.HandleUiCallAsync("getHeartRate", ""));
            Assert.True(beat.RootElement.GetProperty("connected").GetBoolean());
            Assert.Equal(72, beat.RootElement.GetProperty("heartRate").GetInt32());
            Assert.Equal([72], beat.RootElement.GetProperty("samples").EnumerateArray().Select(s => s.GetProperty("bpm").GetInt32()));
            Assert.False(beat.RootElement.TryGetProperty("recordCount", out _));
        }
        finally
        {
            module.OnShutdown();
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpoitableToolKit.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repo の根が見つからない");
    }

    // 待つと、外れたベルトの最後の心拍をアバターが 10 秒（時計なら 60 秒）出したままになる。
    [Fact]
    public async Task ALostLinkIsReportedWithoutWaiting()
    {
        var (module, host, source) = NewModule();

        // 心拍が流れ始めてから切る。先に切ると、そのあとに来る 1 拍目で元に戻ってしまう。
        await WaitFor(() => host.Osc.Last("/avatar/parameters/HR") is 72);

        source.Drop();

        // ループが次の 1 手を打つまで（心拍 72 なら 1 秒弱）。
        await WaitFor(() => host.Osc.Last("/avatar/parameters/isHRActive") is false);

        Assert.Equal(false, host.Osc.Last("/avatar/parameters/isHRActive"));
        Assert.Equal(0, host.Osc.Last("/avatar/parameters/HR"));

        module.OnShutdown();
    }

    // 開いたままのパネルにも届くよう、切れた時点で流す（次の 1 拍は来ないので、待っていると画面は受信中のまま残る）。同じ心拍計が戻ったら、また出る。
    [Fact]
    public async Task ALostLinkShowsAsNotConnectedOnThePanelUntilItComesBack()
    {
        var (module, host, source) = NewModule();
        try
        {
            await WaitFor(() => host.Ui.Status!.Text == "72 bpm");

            using (var before = JsonDocument.Parse(Status(module)))
            {
                Assert.True(before.RootElement.GetProperty("connected").GetBoolean());
                Assert.Equal("Fake", before.RootElement.GetProperty("device").GetString());
            }

            lock (host.Panel.Pushed) host.Panel.Pushed.Clear();
            source.Drop();

            string[] pushed;
            lock (host.Panel.Pushed)
                pushed = [.. host.Panel.Pushed.Where(p => p.Channel == "status").Select(p => p.Payload)];

            // 切れたあとに組んだ 1 通を見る（別のスレッドが切れる前に組んだ 1 通を、あとから流すことがある）。
            Assert.Contains(pushed, p => p.Contains("\"connected\":false"));
            using (var lost = JsonDocument.Parse(pushed.First(p => p.Contains("\"connected\":false"))))
            {
                Assert.Equal("none", lost.RootElement.GetProperty("connection").GetString());
                Assert.Equal(0, lost.RootElement.GetProperty("heartRate").GetInt32());
                Assert.False(lost.RootElement.TryGetProperty("device", out _));
            }

            source.Reconnect();
            source.Beat(80);

            using var back = JsonDocument.Parse(Status(module));
            Assert.True(back.RootElement.GetProperty("connected").GetBoolean());
            Assert.Equal("Fake", back.RootElement.GetProperty("device").GetString());
            Assert.Equal(80, back.RootElement.GetProperty("heartRate").GetInt32());
        }
        finally
        {
            module.OnShutdown();
        }
    }

    /// <summary>時間で決め打ちにせず、起きるまで待つ（遅い機械で落ちないように）。</summary>
    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 60 && !condition(); i++) await Task.Delay(50);
    }

    /// <summary><see cref="HeartRateSession.Step"/> は時間を進めないので、試験から何度でも叩ける。</summary>
    /// <summary>試験が進め、本物の見張り（窓口の 250ms）が別のスレッドから読む。値がちぎれないよう Interlocked で持つ。</summary>
    private sealed class Clock : TimeProvider
    {
        private long _ticks = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero).UtcTicks;

        public DateTimeOffset Now
        {
            get => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
            set => Interlocked.Exchange(ref _ticks, value.UtcTicks);
        }

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (HeartRateSession Session, FakeOscBus Osc, Clock Clock) NewSession(HrsConfig? config = null)
    {
        var osc = new FakeOscBus();
        var clock = new Clock();
        return (new HeartRateSession(osc, config ?? new HrsConfig(), SessionEvents(), time: clock), osc, clock);
    }

    // 拍は 1 周期（60 bpm なら 1000ms）ごとに立ち、100ms だけ立ったまま送る。心拍が来ている間は 50ms より長く空けない。拍の立ち上がりと立ち下がりの瞬間にも送るので、拍の長さは 50ms の刻みに丸められない。
    // 送るたびに 9 個とも全部送る。UDP は届いた保証が無いので、変わったものだけ送ると取りこぼしたまま固まる。
    [Fact]
    public void ABeatIsUpForAHundredMillisecondsEveryCycle()
    {
        var (session, osc, clock) = NewSession();
        session.Report(60);
        var start = clock.Now;
        List<double> ups = [], downs = [];
        var was = false;

        while (clock.Now - start < TimeSpan.FromMilliseconds(2050))
        {
            var before = osc.Sent.Count;
            var wait = session.Step();
            Assert.Equal(9, osc.Sent.Count - before);

            var beat = (bool)osc.Last("/avatar/parameters/isHRBeat")!;
            var at = (clock.Now - start).TotalMilliseconds;
            if (beat && !was) ups.Add(at);
            if (!beat && was) downs.Add(at);
            was = beat;

            Assert.InRange(wait, TimeSpan.FromTicks(1), TimeSpan.FromMilliseconds(50));
            clock.Now += wait;
        }

        Assert.Equal([0d, 1000d, 2000d], ups);
        Assert.Equal([100d, 1100d], downs);
    }

    [Fact]
    public void TheHeartRateIsSentSplitIntoDigits()
    {
        var (session, osc, _) = NewSession();
        session.Report(128);
        session.Step();

        Assert.Equal(8, osc.Last("/avatar/parameters/onesHR"));
        Assert.Equal(2, osc.Last("/avatar/parameters/tensHR"));
        Assert.Equal(1, osc.Last("/avatar/parameters/hundredsHR"));
        Assert.Equal(128, osc.Last("/avatar/parameters/HR"));
        Assert.Equal(128 / 255f, osc.Last("/avatar/parameters/HRPercent"));
    }

    /// <summary>心拍が来ていないうちは何も送らない。繋いだ直後に 0 を流さないため。</summary>
    [Fact]
    public void BeforeAnyBeatNotActiveIsSentExactlyOnce()
    {
        var (session, osc, _) = NewSession();

        Assert.Equal(TimeSpan.FromMilliseconds(500), session.Step());
        Assert.Equal(9, osc.Sent.Count);
        Assert.Equal(false, osc.Last("/avatar/parameters/isHRActive"));

        // 2 回目は送らない。止まっている間ずっと UDP を出し続けない。
        session.Step();
        Assert.Equal(9, osc.Sent.Count);
    }

    // 倒さないと、外れたベルトの最後の値をアバターが出したままになる。パネルも前の数字を残さない（HeartRateSession.HeartRate が 0）。黙ったのを数えるのは窓口だけ（session は数えない。HrsSourceTests が見る）。
    [Fact]
    public void AStretchOfSilenceFlipsToNotActive()
    {
        var (session, osc, _) = NewSession();
        session.Report(60);
        session.Step();
        osc.Sent.Clear();

        session.SignalLost(TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromMilliseconds(500), session.Step());
        Assert.Equal(false, osc.Last("/avatar/parameters/isHRActive"));
        Assert.Equal(false, osc.Last("/avatar/parameters/isHRConnected"));
        Assert.Equal(0, session.HeartRate);

        // 心拍が戻れば、また送り始める。
        session.Report(70);
        session.Step();
        Assert.Equal(true, osc.Last("/avatar/parameters/isHRActive"));
        Assert.Equal(70, osc.Last("/avatar/parameters/HR"));
    }

    // 「動いていない」は 1 回だけ送る作りなので、心拍が戻ったときにラッチを外していないと、2 回目にベルトが外れたときだけ黙り、アバターは最後の心拍を出したまま止まる。
    [Fact]
    public void SilenceAfterBeatsResumeAlsoFlipsToNotActive()
    {
        var (session, osc, _) = NewSession();

        session.Report(60);
        session.Step();
        session.SignalLost(TimeSpan.FromSeconds(10));
        session.Step();

        // 心拍が戻って、また止まる。
        session.Report(70);
        session.Step();
        osc.Sent.Clear();
        session.SignalLost(TimeSpan.FromSeconds(10));
        session.Step();

        Assert.Equal(false, osc.Last("/avatar/parameters/isHRActive"));
    }

    // 心拍計は喋っていても、アバターには「繋がっていない」と見せる（bool は全部 false: 繋がっている・動いている・拍）。拍を立てていた最中でも倒す。
    [Fact]
    public void AZeroHeartRateTurnsEveryFlagOff()
    {
        var (session, osc, _) = NewSession();
        session.Report(60);
        session.Step();
        Assert.Equal(true, osc.Last("/avatar/parameters/isHRBeat"));
        osc.Sent.Clear();

        session.Report(0);
        session.Step();

        Assert.Equal(0, osc.Last("/avatar/parameters/HR"));
        Assert.Equal(false, osc.Last("/avatar/parameters/isHRConnected"));
        Assert.Equal(false, osc.Last("/avatar/parameters/isHRActive"));
        Assert.Equal(false, osc.Last("/avatar/parameters/isHRBeat"));
    }

    // 100ms 立てたままにすると次の拍とつながって立ちっぱなしになり、拍が見えなくなる。
    [Fact]
    public void AFastHeartRateStillDropsTheBeatBetweenBeats()
    {
        var (session, osc, clock) = NewSession();
        session.Report(600);
        List<bool> seen = [];

        for (var i = 0; i < 8; i++)
        {
            var wait = session.Step();
            seen.Add((bool)osc.Last("/avatar/parameters/isHRBeat")!);
            Assert.True(wait > TimeSpan.Zero);
            clock.Now += wait;
        }

        Assert.Equal([true, false, true, false, true, false, true, false], seen);
    }

    // 送らずに消えると、アバターは最後に受け取った心拍を出したままになる。
    [Fact]
    public async Task ShutdownSendsNotConnected()
    {
        var (session, osc, _) = NewSession();
        session.Report(60);

        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();
        await session.RunAsync(stop.Token);

        Assert.Equal(false, osc.Last("/avatar/parameters/isHRConnected"));
        Assert.Equal(0, osc.Last("/avatar/parameters/HR"));
    }

    // アドレスは UTF-8 で書くので日本語も通る。名前を変えても送る数は 9 個のまま。
    [Fact]
    public void ParameterNamesComeFromTheSettings()
    {
        var (session, osc, _) = NewSession(new HrsConfig { Parameters = { HeartRate = "心拍", IsBeat = "beat" } });
        session.Report(72);

        session.Step();

        Assert.Equal(72, osc.Last("/avatar/parameters/心拍"));
        Assert.Equal(true, osc.Last("/avatar/parameters/beat"));
        Assert.Equal(9, osc.Sent.Count);
    }

    // 「動いていない」は 1 回だけ送るようにしてあるので、ラッチを外さないと心拍が戻るまでアバターは何も受け取れない。
    [Fact]
    public void RenamingWhileIdleResendsTheState()
    {
        var config = new HrsConfig();
        var (session, osc, _) = NewSession(config);

        session.Step();
        Assert.Equal(false, osc.Last("/avatar/parameters/isHRActive"));

        config.Parameters.IsActive = "myActive";
        session.Step();

        Assert.Equal(false, osc.Last("/avatar/parameters/myActive"));
    }

    // 空だと宛先が /avatar/parameters/ で終わってしまい、頭か終わりの / と // は空の段ができて別のアドレスになる（途中の / は通す。HrsOscPresetTests）。
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/HR")]
    [InlineData("HR//2")]
    public void AnUnreadableParameterNameIsRejected(string name)
    {
        var (module, host, _) = NewModule();

        var notes = Apply(host, $$"""{ "Parameters.HeartRate": {{System.Text.Json.JsonSerializer.Serialize(name)}}, "NoSignalSeconds": 20 }""");

        // その名前だけ断る（ほかの値は入る）。偽物の文言はキーをそのまま返すので、どの項目が理由になったかが読み取れる。
        var note = Assert.Single(notes);
        Assert.Equal("Parameters.HeartRate", note.Key);
        Assert.Contains("hrs.settings.parameters.heartRate.label", note.Message);
        Assert.Equal("HR", ((HrsConfig)module.Config!).Parameters.HeartRate);
        Assert.Equal(20, ((HrsConfig)module.Config!).NoSignalSeconds);

        module.OnShutdown();
    }

    // 確かめるのは新しい値（写し）。自分の設定（まだ前の値）を読むと、何も断れない。
    [Fact]
    public void AnUnreadableDeviceIsRejectedAloneAndTheOtherValuesGoIn()
    {
        var (module, host, source) = NewModule();
        var config = (HrsConfig)module.Config!;

        var notes = Apply(host, """{ "Device": "Polar [ZZ:ZZ]", "ReconnectSeconds": 9 }""");

        var note = Assert.Single(notes);
        Assert.Equal("Device", note.Key);
        Assert.Contains("hrs.error.badAddress", note.Message);
        Assert.Equal("", config.Device);
        Assert.Equal(9, config.ReconnectSeconds);
        Assert.Equal(1, source.Runs);

        module.OnShutdown();
    }

    // 崩れると上の帯へ二度と入れない（moderate ≧ hard なら、hard の条件を満たす心拍が必ず先に moderate で拾われる）。手元で分かる条件なので、繋ぎに行かずに答える。
    [Fact]
    public void OutOfOrderZoneThresholdsAreRejected()
    {
        var (module, host, _) = NewModule();
        var config = (HrsConfig)module.Config!;

        // 2 つ変えて崩れた → 変えた 2 つとも断る（1 つだけ断ると、もう片方が入って崩れる）。
        var both = Apply(host, """{ "Zones.ModerateBpm": 150, "Zones.HardBpm": 130 }""");

        Assert.Equal(["Zones.ModerateBpm", "Zones.HardBpm"], both.Select(n => n.Key));
        Assert.All(both, n => Assert.Contains("hrs.error.zoneOrder", n.Message));
        Assert.Equal((100, 120, 140), (config.Zones.LightBpm, config.Zones.ModerateBpm, config.Zones.HardBpm));

        // 1 つだけ変えて、変えていない値と崩れた → その 1 つを断る（組み合わせは写しで、変わっていない値も今の値で見る）。
        var one = Apply(host, """{ "Zones.LightBpm": 130 }""");

        Assert.Equal(["Zones.LightBpm"], one.Select(n => n.Key));
        Assert.Equal(100, config.Zones.LightBpm);

        module.OnShutdown();
    }

    // 切ったのに書き続けると、切ったつもりの人のディスクが静かに埋まる（1 秒に 1 件で 1 日 8 万件を超える）。
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NothingIsStoredWhileRecordingIsOff(bool recording)
    {
        var directory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "stk-hrs-gate", Guid.NewGuid().ToString("N"))).FullName;

        var records = new HeartRateRecords(
            Path.Combine(directory, "records.db"), NullLogger.Instance);

        var module = new HrsModule
        {
            CreateSources = () => [new FakeSource()],
            CreateRecords = () => records,
        };

        ((HrsConfig)module.Config!).RecordingEnabled = recording;
        Assert.True(module.OnInit(new FakeModuleHost()));

        // 出どころは繋がった直後に 1 拍流す。
        await WaitFor(() => Status(module).Contains("\"heartRate\":72"));

        await records.FlushAsync();
        var summary = await records.SummaryAsync();

        Assert.Equal(recording ? 1 : 0, summary.Count);

        module.OnShutdown();
    }

    // 数え直すのは開いた時と書き出した後だけなので、1 件目のときに数え直さないと「まだ 1 件も記録されていません」のまま残る。getStatus は呼ぶたびに数えるので、ここでは push だけを見る。
    [Fact]
    public async Task TheFirstRecordReachesAnOpenPanelWithoutReopeningIt()
    {
        var directory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "stk-hrs-first", Guid.NewGuid().ToString("N"))).FullName;
        var records = new HeartRateRecords(Path.Combine(directory, "records.db"), NullLogger.Instance);
        var source = new FakeSource();
        var host = new FakeModuleHost();
        var module = new HrsModule { CreateSources = () => [source], CreateRecords = () => records };
        var config = (HrsConfig)module.Config!;

        // 繋がった直後の 1 拍（72）は記録しない — 開いたときの数え直し（0 件）と混ざらないように。
        config.RecordingEnabled = false;
        Assert.True(module.OnInit(host));

        try
        {
            await WaitFor(() => Status(module).Contains("\"heartRate\":72"));
            config.RecordingEnabled = true;
            var before = StatusPushes(host).Length;

            // 心拍は 1 秒に 1 回来る。数え直しが書き込みより先に終わると 0 件のままなので、届くまで送り続ける。
            for (var i = 0; i < 40 && !StatusPushes(host).Skip(before).Any(p => p.GetProperty("recordCount").GetInt64() >= 1); i++)
            {
                source.Beat(80);
                await Task.Delay(50);
            }

            Assert.Contains(StatusPushes(host).Skip(before), p => p.GetProperty("recordCount").GetInt64() >= 1);
        }
        finally
        {
            module.OnShutdown();
        }
    }

    /// <summary>パネルへ送った状態（<c>status</c>）を、送った順に。</summary>
    private static JsonElement[] StatusPushes(FakeModuleHost host)
    {
        lock (host.Panel.Pushed)
            return [.. host.Panel.Pushed.Where(p => p.Channel == "status").Select(p => JsonDocument.Parse(p.Payload).RootElement.Clone())];
    }

    // パスが分からないと、押した人は探しに行けない。出す先はモジュールの作業領域の下（exports/）。押した瞬間には終わっていないので、結果は push で追いつく。
    [Fact]
    public async Task TheExportedPathIsReturnedToThePanel()
    {
        var host = new FakeModuleHost();
        var source = new FakeSource();
        var module = new HrsModule { CreateSources = () => [source] };

        Assert.True(module.OnInit(host));

        try
        {
            // 1 拍記録されるのを待ってから出す（0 件だと出す意味が無い）。
            await WaitFor(() => Status(module).Contains("\"heartRate\":72"));

            await module.HandleUiCallAsync("exportCsv", "{\"hours\":0}");
            await WaitFor(() => Status(module).Contains("lastExport"));

            var status = Status(module);
            Assert.Contains("lastExport", status);

            var exports = Path.Combine(host.DataDirectory, "exports");
            var written = Directory.GetFiles(exports, "*.csv");

            Assert.Single(written);
            Assert.Equal("timestamp,bpm", (await File.ReadAllLinesAsync(written[0]))[0]);
        }
        finally
        {
            module.OnShutdown();
        }
    }

    /// <summary>記録を直に持たせたモジュール。過去の日に書くので、繋がった直後の 1 拍（いま）とは混ざらない。</summary>
    private static (HrsModule Module, FakeModuleHost Host, HeartRateRecords Records) WithRecords()
    {
        var directory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "stk-hrs-read", Guid.NewGuid().ToString("N"))).FullName;

        var records = new HeartRateRecords(Path.Combine(directory, "records.db"), NullLogger.Instance);
        var host = new FakeModuleHost();
        var module = new HrsModule { CreateSources = () => [new FakeSource()], CreateRecords = () => records };

        Assert.True(module.OnInit(host));
        return (module, host, records);
    }

    private static readonly DateTimeOffset PastNoon = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private static string Range(DateTimeOffset from, DateTimeOffset to) =>
        $$"""{"from":{{from.ToUnixTimeMilliseconds()}},"to":{{to.ToUnixTimeMilliseconds()}}}""";

    // 名前の無い 2 つ組にしてあるのは、1 日 8.6 万件だと {t,bpm} の組の半分ほどで済むから。範囲の外は来ない。
    [Fact]
    public async Task TheStatsTabReadsARangeAsPairs()
    {
        var (module, _, records) = WithRecords();

        try
        {
            records.Write(60, PastNoon.AddHours(-3));
            records.Write(80, PastNoon);
            records.Write(82, PastNoon.AddSeconds(1));
            records.Write(100, PastNoon.AddHours(3));

            var json = await module.HandleUiCallAsync("readRecords", Range(PastNoon.AddHours(-1), PastNoon.AddHours(1)));
            var at = PastNoon.ToUnixTimeMilliseconds();

            Assert.Equal($$"""{"points":[[{{at}},80],[{{at + 1000}},82]]}""", json);
        }
        finally
        {
            module.OnShutdown();
        }
    }

    // 投げるとパネルの統計タブが開かない。空なら「データなし」が出る。
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"from":1000}""")]
    [InlineData("""{"from":2000,"to":1000}""")]
    [InlineData("""{"from":"a","to":"b"}""")]
    [InlineData("not json")]
    public async Task AnUnreadableRangeReadsNothing(string payload)
    {
        var (module, _, records) = WithRecords();

        try
        {
            records.Write(80, DateTimeOffset.FromUnixTimeMilliseconds(1500));

            Assert.Equal("""{"points":[]}""", await module.HandleUiCallAsync("readRecords", payload));
        }
        finally
        {
            module.OnShutdown();
        }
    }

    // {"from","to"} があれば、その範囲だけが CSV に入る（hours より優先）。ファイル名は出した時刻のまま（過去の同じ日を 2 回出してもぶつからない）。
    [Fact]
    public async Task TheVisibleRangeCanBeExported()
    {
        var (module, host, records) = WithRecords();

        try
        {
            records.Write(60, PastNoon.AddHours(-3));
            records.Write(80, PastNoon);
            records.Write(100, PastNoon.AddHours(3));

            var payload = Range(PastNoon.AddHours(-1), PastNoon.AddHours(1)).Replace("{", """{"hours":0,""");
            await module.HandleUiCallAsync("exportCsv", payload);
            await WaitFor(() => Status(module).Contains("lastExport"));

            var written = Assert.Single(Directory.GetFiles(Path.Combine(host.DataDirectory, "exports"), "*.csv"));
            var lines = await File.ReadAllLinesAsync(written);

            Assert.Equal(2, lines.Length);
            Assert.EndsWith(",80", lines[1]);
            Assert.StartsWith("hr_" + DateTimeOffset.Now.ToString("yyyyMMdd"), Path.GetFileName(written));
        }
        finally
        {
            module.OnShutdown();
        }
    }

    // 戻す先が無い値には既定を当てる（範囲外の数値を読み込みのときだけ丸めるのと同じ扱い）。
    [Fact]
    public void AnUnreadableNameInTheFileFallsBackToTheDefault()
    {
        var (module, _, _) = NewModule(c =>
        {
            c.Parameters.Ones = "";
            c.Parameters.Tens = "a//b";
        });

        var config = (HrsConfig)module.Config!;
        Assert.Equal("onesHR", config.Parameters.Ones);
        Assert.Equal("tensHR", config.Parameters.Tens);

        module.OnShutdown();
    }

    // 名前はループが読み直すので繋ぎ直す必要が無く、繋ぎ直すと 9 個を 1 つずつ直す間ずっと心拍が切れる。
    [Fact]
    public async Task RenamingDoesNotReconnect()
    {
        var (module, host, source) = NewModule();

        var notes = Apply(host, """{ "Parameters.HeartRate": "HR2" }""");
        await Task.Delay(200);

        Assert.Empty(notes);
        Assert.Equal("HR2", ((HrsConfig)module.Config!).Parameters.HeartRate);
        Assert.Equal(1, source.Runs);
        Assert.True(source.Watching);

        module.OnShutdown();
    }

    // 前の VRCHR の名前には「止まった」が残る。設定画面の「適用」と同じ道で替える（選ぶ欄の値は名前の文字列で入る。ほかの試験は config を直に書き換えていて、この道を通らない）。
    [Fact]
    public async Task ChoosingAPresetSwitchesTheNamesWithoutReconnecting()
    {
        var (module, host, source) = NewModule();
        try
        {
            await WaitFor(() => host.Osc.Last("/avatar/parameters/HR") is 72);

            var notes = Apply(host, """{ "OscPreset": "VRCOSC", "SmoothValues": false }""");
            await WaitFor(() => host.Osc.Last("/avatar/parameters/VRCOSC/Heartrate/Value") is 72);

            Assert.Empty(notes);
            var config = (HrsConfig)module.Config!;
            Assert.Equal(HrsOscPreset.VRCOSC, config.OscPreset);
            Assert.False(config.SmoothValues);
            Assert.Equal(72, host.Osc.Last("/avatar/parameters/VRCOSC/Heartrate/Value"));
            Assert.Equal(0, host.Osc.Last("/avatar/parameters/HR"));
            Assert.Equal(1, source.Runs);
            Assert.True(source.Watching);
        }
        finally
        {
            module.OnShutdown();
        }
    }
    // 同じ繋がりの知らせが 2 回来ても 1 回しか書かない（入口と調停の両方から同じ知らせが来る）。
    [Fact]
    public async Task ConnectingAndLosingTheSourceAreLoggedOnce()
    {
        var logs = new Logs();
        var (module, host, source) = NewModule(logs: logs);
        try
        {
            await WaitFor(() => host.Ui.Status!.Text == "72 bpm");

            source.Nudge();

            Assert.Single(logs.Infos, line => line == "Heart rate source: Fake [A1:B2:C3:D4:E5:F6] over BleGatt");

            source.Drop();
            source.Nudge();

            Assert.Single(logs.Infos, line => line == "Heart rate source: lost Fake [A1:B2:C3:D4:E5:F6] (was over BleGatt)");
        }
        finally
        {
            module.OnShutdown();
        }
    }

    [Fact]
    public async Task StartingTheCaptureSaysWhatItLooksFor()
    {
        var logs = new Logs();
        var (module, host, _) = NewModule(c => c.Hds.Enabled = false, logs);
        try
        {
            await WaitFor(() => host.Ui.Status!.Text == "72 bpm");

            Assert.Contains("Heart rate capture started (looking for any heart rate monitor; HDS off)", logs.Infos);
        }
        finally
        {
            module.OnShutdown();
        }
    }

    // 0 は毎秒来るので 1 回だけ書く。計れるようになったら、それも 1 回。
    [Fact]
    public async Task AZeroReadingIsLoggedOnceAndSoIsTheRecovery()
    {
        var logs = new Logs();
        var (module, host, source) = NewModule(logs: logs);
        try
        {
            await WaitFor(() => host.Ui.Status!.Text == "72 bpm");

            source.Beat(0);
            source.Beat(0);
            source.Beat(81);
            source.Beat(82);

            Assert.Single(logs.Infos, line => line.Contains("reports 0 bpm", StringComparison.Ordinal));
            Assert.Single(logs.Infos, line => line == "Heart rate source: reading again (81 bpm)");
        }
        finally
        {
            module.OnShutdown();
        }
    }

    [Fact]
    public void ChangedSettingsAreLogged()
    {
        var logs = new Logs();
        var (module, host, _) = NewModule(logs: logs);
        try
        {
            Assert.Empty(Apply(host, """{ "RecordingEnabled": false, "NoSignalSeconds": 20 }"""));

            Assert.Contains("Heart rate settings changed: RecordingEnabled, NoSignalSeconds", logs.Infos);
        }
        finally
        {
            module.OnShutdown();
        }
    }

    // 切れても数字は残す（次に繋がるまで前の繋がりの数字を見せる）。計れていない（0）では切らない。数え直しで直近のグラフは消さない（計れていない・切れていた所は 0 で残る）。
    // パネルとダッシュボードの札、配信のオーバーレイは同じ HeartRateLog.Overall を読む。
    [Fact]
    public async Task TheStatsStartAgainOnEveryConnection()
    {
        var (module, host, source) = NewModule();
        try
        {
            await WaitFor(() => host.Ui.Status!.Text == "72 bpm");

            source.Beat(100);
            source.Beat(0);

            Assert.Equal("72 86 100", Stats(module));
            Assert.Equal("hrs.status.note(72, 100)", host.Ui.Status!.Note);

            source.Drop();

            Assert.Equal("72 86 100", Stats(module));
            Assert.Equal("hrs.status.note(72, 100)", host.Ui.Status.Note);

            // 繋がった時点で（まだ 1 拍も無くても）前の数字は消える。札も次の 1 拍を待たない。
            source.Reconnect();

            Assert.Null(Stats(module));
            Assert.Null(host.Ui.Status.Note);

            source.Beat(65);

            Assert.Equal("65 65 65", Stats(module));
            Assert.Equal("hrs.status.note(65, 65)", host.Ui.Status.Note);
            Assert.Equal([72, 100, 0, 0, 65], Samples(module));

            // 同じ繋がりの知らせがもう一度来ても数え直さない（入口と調停の両方から同じ知らせが来る）。
            source.Beat(85);
            source.Nudge();

            Assert.Equal("65 75 85", Stats(module));

            // 名前が同じままでも、入口が乗り換わったら（BLE → 時計）数え直す。
            source.Via = HeartRateConnection.Hds;
            source.Nudge();

            Assert.Null(Stats(module));
        }
        finally
        {
            module.OnShutdown();
        }
    }

    private static string? Stats(HrsModule module)
    {
        using var status = JsonDocument.Parse(Status(module));
        if (!status.RootElement.TryGetProperty("stats", out var stats)) return null;

        return $"{stats.GetProperty("min").GetInt32()} {stats.GetProperty("avg").GetInt32()} {stats.GetProperty("max").GetInt32()}";
    }

    private static int[] Samples(HrsModule module)
    {
        using var status = JsonDocument.Parse(Status(module));
        return [.. status.RootElement.GetProperty("samples").EnumerateArray().Select(s => s.GetProperty("bpm").GetInt32())];
    }

    /// <summary>電池はパネルへ流す。分からない（GATT でない）ときは出さない。</summary>
    [Fact]
    public async Task TheBatteryGoesToThePanel()
    {
        var (module, host, source) = NewModule();
        try
        {
            await WaitFor(() => host.Ui.Status!.Text == "72 bpm");

            using (var none = JsonDocument.Parse(Status(module)))
                Assert.False(none.RootElement.TryGetProperty("battery", out _));

            source.Battery = 64;

            using var some = JsonDocument.Parse(Status(module));
            Assert.Equal(64, some.RootElement.GetProperty("battery").GetInt32());
        }
        finally
        {
            module.OnShutdown();
        }
    }

    // 秒数は窓口が渡す（その入口の黙ってよい長さ）。0 が来ていた（計れていない）ときはモジュールが書くので、ここでは書かない。
    [Fact]
    public void SilenceIsLoggedWithHowLongItWas()
    {
        var logs = new Logs();
        var osc = new FakeOscBus();
        var session = new HeartRateSession(osc, new HrsConfig(), SessionEvents(), time: new Clock(), logger: logs);

        session.Report(70);
        session.Step();
        session.SignalLost(TimeSpan.FromSeconds(10));
        session.Step();
        session.Step();

        Assert.Equal(["No heart rate for 10s; telling the avatar it stopped"], logs.Infos);

        var zero = new Logs();
        var quiet = new HeartRateSession(new FakeOscBus(), new HrsConfig(), SessionEvents(), time: new Clock(), logger: zero);
        quiet.Report(0);
        quiet.Step();
        quiet.SignalLost(TimeSpan.FromSeconds(10));
        quiet.Step();

        Assert.Empty(zero.Infos);
    }
}
