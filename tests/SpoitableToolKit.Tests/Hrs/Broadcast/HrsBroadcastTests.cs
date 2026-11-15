using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Modules.Hrs;
using SpoitableToolKit.Modules.Hrs.Broadcast;
using SpoitableToolKit.Modules.Hrs.Config;
using SpoitableToolKit.Modules.Hrs.Devices.Interfaces;
using SpoitableToolKit.Modules.Hrs.Devices.Models;
using SpoitableToolKit.Modules.Hrs.Events.Broadcast;
using SpoitableToolKit.Tests.Fakes;
using Xunit;

namespace SpoitableToolKit.Tests.Hrs.Broadcast;

/// <summary>配信者向けの WebSocket 配信と OBS のオーバーレイ。待ち受けはループバックだけなので、この試験で Windows のファイアウォールの確認は出ない（0.0.0.0 で待つと出る）。</summary>
public sealed class HrsBroadcastTests
{
    /// <summary>空いているポートを 1 つ。<c>HttpListener</c> はポート 0 を受け付けない（プレフィックスに具体的なポートが要る）ので、いったん借りて番号だけ貰う。</summary>
    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();

        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(50);
    }

    /// <summary>待ち受けを始めた配信を渡す。畳むのは呼び出し側（using）。</summary>
    private static async Task<(HrsBroadcastServer Server, HrsConfig Config, CancellationTokenSource Stop, Task Loop)>
        Listening(Func<IReadOnlyList<int>>? history = null)
    {
        var config = new HrsConfig { Broadcast = { Enabled = true, Port = FreePort() } };
        var server = new HrsBroadcastServer(config, NullLogger.Instance, BroadcastEvents(), history);
        var stop = new CancellationTokenSource();
        var loop = server.RunAsync(stop.Token);

        await WaitFor(() => server.Status == HrsBroadcastStatus.Listening);
        Assert.Equal(HrsBroadcastStatus.Listening, server.Status);

        return (server, config, stop, loop);
    }

    private static async Task Close(CancellationTokenSource stop, Task loop)
    {
        await stop.CancelAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Dispose();
    }

    /// <summary>オーバーレイを配る。dll に埋め込んである（モジュールはストリームから読み込まれるので自分の置き場所を辿れない）ため、埋め込みが漏れると 404 になる。</summary>
    [Theory]
    [InlineData("pill")]
    [InlineData("glass")]
    [InlineData("neon")]
    [InlineData("widget")]
    public async Task OverlaysAreServed(string name)
    {
        var (_, config, stop, loop) = await Listening();

        try
        {
            using var http = new HttpClient();
            var page = await http.GetStringAsync($"http://localhost:{config.Broadcast.Port}/overlay/{name}");

            Assert.StartsWith("<!DOCTYPE html>", page);

            // 配信に繋ぎに行く枝が入っていること（貼っただけで動くのが売り）。
            Assert.Contains("/ws", page);
        }
        finally
        {
            await Close(stop, loop);
        }
    }

    /// <summary>知らない名前は 404。名前をそのままファイル名にしないので、<c>../</c> を混ぜても dll の外は読めない。</summary>
    [Theory]
    [InlineData("nope")]
    [InlineData("../../secret")]
    public async Task AnUnknownOverlayIsNotServed(string name)
    {
        var (_, config, stop, loop) = await Listening();

        try
        {
            using var http = new HttpClient();
            var response = await http.GetAsync($"http://localhost:{config.Broadcast.Port}/overlay/{name}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await Close(stop, loop);
        }
    }

    /// <summary><c>localhost</c> でも <c>127.0.0.1</c> でも届く。<c>localhost</c> だけ登録すると、127.0.0.1 で叩いた OBS には 400 が返る（実測）。</summary>
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    public async Task EverySpellingOfLoopbackReaches(string host)
    {
        var (_, config, stop, loop) = await Listening();

        try
        {
            using var http = new HttpClient();
            var response = await http.GetAsync($"http://{host}:{config.Broadcast.Port}/health");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("\"status\":\"ok\"", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await Close(stop, loop);
        }
    }

    /// <summary>繋いだ直後にいまの状態と直近の心拍が届く。心拍は 1 秒に 1 回しか来ないので、渡さないと OBS を貼った直後だけ空になり、グラフも埋まらない。</summary>
    [Fact]
    public async Task TheCurrentStateArrivesRightAfterConnecting()
    {
        var (server, config, stop, loop) = await Listening(() => [70, 71, 72]);

        try
        {
            server.Publish(new HrsBroadcastState(75, "rest", true, 72, 70, 75, "Polar H10"));

            using var socket = await Connect(config.Broadcast.Port);
            var welcome = await Read(socket);

            Assert.Equal("welcome", welcome.GetProperty("type").GetString());
            Assert.Equal(75, welcome.GetProperty("bpm").GetInt32());
            Assert.True(welcome.GetProperty("connected").GetBoolean());
            Assert.Equal("Polar H10", welcome.GetProperty("device").GetString());
            Assert.Equal(3, welcome.GetProperty("history").GetArrayLength());
        }
        finally
        {
            await Close(stop, loop);
        }
    }

    /// <summary>流したものが届く。形を保つので、既存のオーバーレイや外部ツールは繋ぎ先を変えるだけで動く。</summary>
    [Fact]
    public async Task WhatIsBroadcastArrives()
    {
        var (server, config, stop, loop) = await Listening();

        try
        {
            using var socket = await Connect(config.Broadcast.Port);

            // 繋ぎ終えてもサーバーが受け手に数えるまで間がある。その間に流すと welcome で届く。
            await WaitFor(() => server.Clients == 1);
            server.Publish(new HrsBroadcastState(142, "hard", true, 120, 60, 142));
            var update = await Read(socket);

            Assert.Equal("hr_update", update.GetProperty("type").GetString());
            Assert.Equal(142, update.GetProperty("bpm").GetInt32());
            Assert.Equal("hard", update.GetProperty("zone").GetString());
            Assert.Equal(120, update.GetProperty("avg").GetInt32());
            Assert.Equal(60, update.GetProperty("min").GetInt32());
            Assert.Equal(142, update.GetProperty("max").GetInt32());
        }
        finally
        {
            await Close(stop, loop);
        }
    }

    /// <summary>無効なら口を開かない。既定は無効で、設定を触っていない人のポートを勝手に埋めない。</summary>
    [Fact]
    public async Task NothingListensWhileDisabled()
    {
        var config = new HrsConfig { Broadcast = { Enabled = false, Port = FreePort() } };
        var server = new HrsBroadcastServer(config, NullLogger.Instance, BroadcastEvents());
        var stop = new CancellationTokenSource();
        var loop = server.RunAsync(stop.Token);

        await Task.Delay(200);

        Assert.Equal(HrsBroadcastStatus.Disabled, server.Status);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        await Assert.ThrowsAnyAsync<Exception>(() => http.GetAsync($"http://localhost:{config.Broadcast.Port}/health"));

        await Close(stop, loop);
    }

    /// <summary>ポートが埋まっていても落ちずに待つ。ここで例外を投げると心拍まで止まる — 配信が始まらないのと心拍が止まるのは別の困り方で、混ぜてはいけない。</summary>
    [Fact]
    public async Task ATakenPortIsNotFatal()
    {
        var port = FreePort();

        var squatter = new HttpListener();
        squatter.Prefixes.Add($"http://localhost:{port}/");
        squatter.Start();

        try
        {
            var config = new HrsConfig { Broadcast = { Enabled = true, Port = port } };
            var server = new HrsBroadcastServer(config, NullLogger.Instance, BroadcastEvents());
            var stop = new CancellationTokenSource();
            var loop = server.RunAsync(stop.Token);

            await WaitFor(() => server.Status == HrsBroadcastStatus.Failed);

            Assert.Equal(HrsBroadcastStatus.Failed, server.Status);
            Assert.False(string.IsNullOrEmpty(server.Error));
            Assert.False(loop.IsFaulted);

            await Close(stop, loop);
        }
        finally
        {
            squatter.Close();
        }
    }

    /// <summary>切れたら「繋がっていない」が配信へ流れる。<c>bpm &gt; 0 &amp;&amp; connected</c> のときしか送らないと、ベルトが外れたときオーバーレイが最後の心拍を出したまま固まる。配信に「止まった」を伝えられるのはこの経路だけ。</summary>
    [Fact]
    public async Task LosingTheLinkBroadcastsNotConnected()
    {
        var source = new BeatingSource();
        var port = FreePort();
        var module = new HrsModule { CreateSources = () => [source] };

        var config = (HrsConfig)module.Config!;
        config.Broadcast.Enabled = true;
        config.Broadcast.Port = port;

        Assert.True(module.OnInit(new FakeModuleHost()));

        try
        {
            // 心拍が流れ始めてから繋ぐ。welcome で「繋がっている」を受け取れる状態にする。
            using var socket = await Connect(port);

            var welcome = await Read(socket);
            Assert.True(welcome.GetProperty("connected").GetBoolean());
            Assert.Equal(72, welcome.GetProperty("bpm").GetInt32());

            source.Drop();

            // 何通か流れたあとに来ることもあるので、繋がっていないが来るまで読む。
            var lost = await ReadUntil(socket, message => !message.GetProperty("connected").GetBoolean());

            Assert.False(lost.GetProperty("connected").GetBoolean());
            Assert.Equal(0, lost.GetProperty("bpm").GetInt32());
        }
        finally
        {
            module.OnShutdown();
        }
    }

    /// <summary>繋がったまま黙っただけでも配信へ流れる。GATT は張れているのに通知が来ない止まり方がある（ベルトが肌から浮く、時計の手首を下ろす）。このとき <see cref="IHeartRateSource.Connected"/> は <c>null</c> にならないので、切断とは別の経路で気付くしかない。知っているのはセッションだけで、その 1 回きりのラッチが配信まで届いているかを見る。</summary>
    [Fact]
    public async Task GoingQuietAlsoBroadcastsNotConnected()
    {
        // 無信号とみなすまでを短くした出どころ。繋がったままにするのが肝。
        var source = new BeatingSource(TimeSpan.FromMilliseconds(300));
        var port = FreePort();
        var module = new HrsModule { CreateSources = () => [source] };

        var config = (HrsConfig)module.Config!;
        config.Broadcast.Enabled = true;
        config.Broadcast.Port = port;

        Assert.True(module.OnInit(new FakeModuleHost()));

        try
        {
            using var socket = await Connect(port);

            var welcome = await Read(socket);
            Assert.True(welcome.GetProperty("connected").GetBoolean());

            // 1 拍しか流さないので、黙ったまま無信号の時間が過ぎる。
            var lost = await ReadUntil(socket, message => !message.GetProperty("connected").GetBoolean());

            Assert.Equal(0, lost.GetProperty("bpm").GetInt32());

            // 切断は起きていない（切断で倒れたのではないことを押さえる）。
            Assert.NotNull(source.Connected);
        }
        finally
        {
            module.OnShutdown();
        }
    }

    /// <summary>配信に誰かが繋いだ・離れたら、パネルの数が変わる（配信の様子の口 → パネル）。数が変わるのは配信のスレッドで、ほかに送るきっかけ（1 拍）が無くても届く。</summary>
    [Fact]
    public async Task TheNumberOfBroadcastViewersReachesThePanelWithoutABeat()
    {
        var source = new BeatingSource();
        var port = FreePort();
        var module = new HrsModule { CreateSources = () => [source] };

        var config = (HrsConfig)module.Config!;
        config.Broadcast.Enabled = true;
        config.Broadcast.Port = port;
        // 記録は切る — 最初の 1 拍で記録の数を裏で数え直してパネルへ送るので、それが繋いだあとに着くと、繋いだ数を運んでしまう。
        config.RecordingEnabled = false;

        var host = new FakeModuleHost();
        Assert.True(module.OnInit(host));

        try
        {
            // 1 拍と待ち受けがパネルへ着き、送りが止まってから繋ぐ（起動のときの記録の数え直しも裏で送る）。
            await WaitFor(() => StatusPushes(host).Any(p => p.GetProperty("heartRate").GetInt32() == 72
                                                             && p.GetProperty("wsStatus").GetString() == "listening"));
            await Settled(host);
            var before = StatusPushes(host).Length;

            var socket = await Connect(port);
            await Read(socket);
            await WaitFor(() => StatusPushes(host).Skip(before).Any(p => p.GetProperty("wsClients").GetInt32() == 1));
            Assert.Contains(StatusPushes(host).Skip(before), p => p.GetProperty("wsClients").GetInt32() == 1);

            var joined = StatusPushes(host).Length;
            // オーバーレイのタブを閉じた形（挨拶せずに切る）。
            socket.Abort();
            socket.Dispose();
            await WaitFor(() => StatusPushes(host).Skip(joined).Any(p => p.GetProperty("wsClients").GetInt32() == 0));
            Assert.Contains(StatusPushes(host).Skip(joined), p => p.GetProperty("wsClients").GetInt32() == 0);
        }
        finally
        {
            module.OnShutdown();
        }
    }

    /// <summary>パネルへの送りが 900ms 続けて増えなくなるまで待つ（長くて 5 秒）。</summary>
    private static async Task Settled(FakeModuleHost host)
    {
        var quiet = 0;
        var seen = StatusPushes(host).Length;
        for (var i = 0; i < 50 && quiet < 3; i++)
        {
            await Task.Delay(300);
            var now = StatusPushes(host).Length;
            quiet = now == seen ? quiet + 1 : 0;
            seen = now;
        }
    }

    /// <summary>オーバーレイの履歴には拍だけ。切れていた所の 0 はパネルのグラフのためのもので、オーバーレイの線には混ぜない。</summary>
    [Fact]
    public async Task TheOverlayHistoryLeavesOutTheZerosWhereTheLinkWasDown()
    {
        var source = new BeatingSource();
        var port = FreePort();
        var module = new HrsModule { CreateSources = () => [source] };

        var config = (HrsConfig)module.Config!;
        config.Broadcast.Enabled = true;
        config.Broadcast.Port = port;
        config.RecordingEnabled = false;

        var host = new FakeModuleHost();
        Assert.True(module.OnInit(host));

        try
        {
            await WaitFor(() => StatusPushes(host).Any(p => p.GetProperty("heartRate").GetInt32() == 72
                                                             && p.GetProperty("wsStatus").GetString() == "listening"));

            // 線が落ちる → パネルのグラフには 0 が入る。
            source.Drop();
            await WaitFor(() => StatusPushes(host).Any(p => p.GetProperty("samples").EnumerateArray().Any(s => s.GetProperty("bpm").GetInt32() == 0)));
            Assert.Contains(StatusPushes(host), p => p.GetProperty("samples").EnumerateArray().Any(s => s.GetProperty("bpm").GetInt32() == 0));

            using var socket = await Connect(port);
            var welcome = await Read(socket);
            int[] history = [.. welcome.GetProperty("history").EnumerateArray().Select(b => b.GetInt32())];
            Assert.Equal([72], history);
        }
        finally
        {
            module.OnShutdown();
        }
    }

    private static JsonElement[] StatusPushes(FakeModuleHost host)
    {
        lock (host.Panel.Pushed)
            return [.. host.Panel.Pushed.Where(p => p.Channel == "status").Select(p => JsonDocument.Parse(p.Payload).RootElement.Clone())];
    }

    /// <summary>配信の様子を配る口（試験では誰も聞かない。owner はどれでも通す）。</summary>
    private static IEventHub BroadcastEvents() =>
        new EventHubs(_ => true, NullLoggerFactory.Instance).CreateHub(HostOwner.Instance, [typeof(IHrsBroadcastEventListener)]);

    /// <summary>繋いだふりをして 1 拍だけ流し、畳まれるまで待つ出どころ。</summary>
    private sealed class BeatingSource(TimeSpan? silenceAfter = null, bool beat = true) : IHeartRateSource
    {
        public IReadOnlyList<MonitorIdentity> Discovered => [new(0xA1B2C3D4E5F6, "Fake")];

        public MonitorIdentity? Connected { get; private set; }

        public HeartRateConnection Connection =>
            Connected is null ? HeartRateConnection.None : HeartRateConnection.BleGatt;

        public TimeSpan SilenceAfter { get; } = silenceAfter ?? TimeSpan.FromSeconds(10);

        private IHeartRateSink? _sink;

        public void ReportTo(IHeartRateSink sink) => _sink = sink;

        public void Disconnect(MonitorIdentity monitor)
        {
        }

        public void Connect(MonitorIdentity monitor)
        {
        }

        public void Drop()
        {
            Connected = null;
            _sink?.LinkChanged(this);
        }

        public async Task RunAsync(DeviceSelector selector, CancellationToken cancellationToken)
        {
            Connected = Discovered[0];
            _sink?.LinkChanged(this);

            if (beat) _sink?.Beat(this, 72);

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

    private static async Task<ClientWebSocket> Connect(int port)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://localhost:{port}/ws"), CancellationToken.None);

        return socket;
    }

    private static async Task<JsonElement> Read(ClientWebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var buffer = new byte[8 * 1024];
        var received = await socket.ReceiveAsync(buffer, timeout.Token);

        return JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, received.Count)).RootElement.Clone();
    }

    private static async Task<JsonElement> ReadUntil(ClientWebSocket socket, Func<JsonElement, bool> wanted)
    {
        for (var i = 0; i < 20; i++)
        {
            var message = await Read(socket);
            if (wanted(message)) return message;
        }

        throw new TimeoutException("待っていた内容が来なかった");
    }
}
