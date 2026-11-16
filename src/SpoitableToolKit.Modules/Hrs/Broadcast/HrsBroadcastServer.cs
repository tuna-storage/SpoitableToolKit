using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Modules.Hrs.Config;
using SpoitableToolKit.Modules.Hrs.Events.Broadcast;

namespace SpoitableToolKit.Modules.Hrs.Broadcast;

/// <summary>
/// 配信者向けの WebSocket 配信と、OBS に貼るオーバーレイ。
/// <para>
/// <b>ループバックだけで待ち受ける。</b> OBS も VRChat も同じ機械に居るので、これで足りる。
/// LAN へ出そうとすると <c>http://+:port/</c> になり、urlacl の登録（＝管理者）と
/// ファイアウォールの許可が要る — HDS で一度通った道をもう一度やる価値が無い。
/// ループバックの 3 つ（<c>localhost</c> / <c>127.0.0.1</c> / <c>[::1]</c>）は
/// <b>管理者無しで登録できる</b>ことを実測で確かめてある。
/// </para>
/// <para>
/// <c>localhost</c> のプレフィックスは <c>127.0.0.1</c> 宛のリクエストを<b>拾わない</b>
/// （HTTP.SYS が 400 を返す）。OBS に何を入れるかは人によるので 3 つとも登録する。
/// </para>
/// <para>
/// 送るのは<b>キュー越し</b>。相手（OBS のブラウザソース）が固まっても、
/// 心拍を運んでいるスレッドをそこで待たせない。溢れたら古いものから捨てる —
/// 遅れて届く心拍に価値は無い。
/// </para>
/// </summary>
internal sealed class HrsBroadcastServer(
    HrsConfig config,
    ILogger logger,
    IEventHub events,
    Func<IReadOnlyList<int>>? history = null)
{
    /// <summary>1 つの相手に溜めておく上限。</summary>
    private const int OutboxLimit = 16;

    /// <summary>オーバーレイの名前（URL の末尾になる）。</summary>
    public static readonly string[] Overlays = ["pill", "glass", "neon", "widget"];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

        // 無いものは書かない。<c>history</c> は welcome にしか付かないので、
        // 付けないと毎通 <c>"history":null</c> が載る（実測）。
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ConcurrentDictionary<Guid, Client> _clients = new();

    /// <summary>最後に流した内容と何通目か。<b>後から繋いだ相手へ渡す</b>ので持っておく。待ち受けていなくても持つ（パネルのプレビューが読む）。</summary>
    private HrsBroadcastSent? _last;

    private long _sent;

    /// <summary>
    /// 最後に流した内容と何通目か（まだ 1 通も無ければ null）。<b>待ち受けていなくても</b>流した中身は持っているので、
    /// パネルの配信タブのプレビューは配信を切っていても OBS と同じものを見せられる。
    /// </summary>
    public HrsBroadcastSent? Latest => _last;

    public HrsBroadcastStatus Status { get; private set; } = HrsBroadcastStatus.Disabled;

    /// <summary>始められなかった理由。パネルにそのまま出す（翻訳しない — OS の言葉）。</summary>
    public string? Error { get; private set; }

    /// <summary>いま繋がっている数。パネルに出す（貼れているかの確かめになる）。</summary>
    public int Clients => _clients.Count;

    /// <summary>いま待ち受けているポート。設定を変えても、開き直すまでは古い方が正。</summary>
    public int Port { get; private set; }

    /// <summary>
    /// 待ち受けの様子か、繋がっている数が変わった、と配る（<see cref="IHrsBroadcastEventListener"/>）。<b>パネルは押した結果をここで知る</b> —
    /// 待ち受けを始められたかどうかは、始めてみるまで分からない。
    /// </summary>
    private void AnnounceStatus()
    {
        var status = new HrsBroadcastStatusEventParams(Status, Port, Clients);
        events.Fire<IHrsBroadcastEventListener>(l => l.OnStatusChanged(status));
    }

    /// <summary>
    /// 配信する。<b>心拍のスレッドから呼ばれる。</b>戻るまでにネットワークへは触らない。
    /// </summary>
    public void Publish(HrsBroadcastState state)
    {
        _last = new HrsBroadcastSent(state, Interlocked.Increment(ref _sent));

        if (_clients.IsEmpty) return;

        var json = Serialize(state, HrsBroadcastMessage.Update, withHistory: false);
        foreach (var client in _clients.Values) client.Outbox.Writer.TryWrite(json);
    }

    /// <summary>
    /// 待ち受ける。<b>無効なら何もせず、畳まれるまで黙って待つ</b>
    /// （入口と同じ形にしておくと、呼ぶ側が場合分けを持たずに済む）。
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Status = HrsBroadcastStatus.Disabled;
        Error = null;

        if (!config.Broadcast.Enabled)
        {
            AnnounceStatus();
            await Idle(cancellationToken).ConfigureAwait(false);
            return;
        }

        var port = config.Broadcast.Port;
        var listener = new HttpListener();

        foreach (var host in (string[])["localhost", "127.0.0.1", "[::1]"])
            listener.Prefixes.Add($"http://{host}:{port}/");

        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            // ポートが埋まっているのはふつうに起きる。
            // **心拍の側は動かしたままにする** — 配信が始まらないのと心拍が止まるのは別の困り方。
            logger.LogWarning(ex, "Could not start broadcasting (port {Port})", port);

            Status = HrsBroadcastStatus.Failed;
            Error = ex.Message;
            AnnounceStatus();

            await Idle(cancellationToken).ConfigureAwait(false);
            return;
        }

        Status = HrsBroadcastStatus.Listening;
        Port = port;
        AnnounceStatus();
        logger.LogInformation("Broadcasting on port {Port}", port);

        // GetContextAsync は token を取らないので、畳むときは listener ごと止めて例外で抜ける。
        await using var stop = cancellationToken.Register(listener.Stop);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().ConfigureAwait(false);

                // 1 本が長引いても次を受けたいので、投げっぱなしにする。
                _ = HandleAsync(context, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or OperationCanceledException)
        {
            // 畳まれた。
        }
        finally
        {
            Status = HrsBroadcastStatus.Disabled;
            await CloseAllAsync().ConfigureAwait(false);
            listener.Close();
            AnnounceStatus();
        }
    }

    private static async Task Idle(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 畳まれた。
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath.TrimEnd('/') ?? "";

            if (path is "/ws")
            {
                await AcceptAsync(context, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (path is "/health")
            {
                await WriteAsync(context, "application/json; charset=utf-8", JsonSerializer.Serialize(new
                {
                    status = "ok",
                    clients = Clients,
                    connected = _last?.State.Connected ?? false,
                    bpm = _last?.State.HeartRate ?? 0,
                }, Json)).ConfigureAwait(false);
                return;
            }

            if (path.StartsWith("/overlay/", StringComparison.Ordinal)
                && Overlay(path["/overlay/".Length..]) is { } page)
            {
                await WriteAsync(context, "text/html; charset=utf-8", page).ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = 404;
            context.Response.Close();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Stumbled while accepting a broadcast client");

            try
            {
                context.Response.Abort();
            }
            catch (Exception inner)
            {
                logger.LogTrace(inner, "Could not close the response");
            }
        }
    }

    private static async Task WriteAsync(HttpListenerContext context, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);

        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;

        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        context.Response.Close();
    }

    /// <summary>
    /// オーバーレイの HTML。<b>dll に埋め込んである。</b>
    /// <para>
    /// モジュールのアセンブリはストリームから読み込まれるので <c>Assembly.Location</c> が空で、
    /// 自分の置き場所を辿れない。ファイルで配るなら置き場所をホストから貰う話になるが、
    /// それは<b>テーマをディレクトリで差し替えられるようにするとき</b>にまとめてやる。
    /// </para>
    /// </summary>
    internal static string? Overlay(string name)
    {
        if (!Overlays.Contains(name)) return null;

        var assembly = typeof(HrsBroadcastServer).GetTypeInfo().Assembly;
        using var stream = assembly.GetManifestResourceStream($"SpoitableToolKit.Modules.Hrs.overlays.{name}.html");
        if (stream is null) return null;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 繋ぎに来た相手を受ける。<b>最初に「いまどうなっているか」を渡す</b> —
    /// 心拍は 1 秒に 1 回しか来ないので、渡さないと OBS を開いた直後だけ空になる。
    /// </summary>
    private async Task AcceptAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            context.Response.Close();
            return;
        }

        var accepted = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
        var client = new Client(accepted.WebSocket);
        var id = Guid.NewGuid();

        _clients[id] = client;
        AnnounceStatus();

        // 誰が見に来たか（OBS のブラウザソースなど）。繋がらないと言われたときに、来ているかどうかで切り分けられる。
        logger.LogInformation("Broadcast: a client connected from {From} ({Count} now)",
            context.Request.RemoteEndPoint, _clients.Count);

        if (_last is { } last)
            client.Outbox.Writer.TryWrite(Serialize(last.State, HrsBroadcastMessage.Welcome, withHistory: true));

        try
        {
            await client.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "A broadcast client dropped");
        }
        finally
        {
            _clients.TryRemove(id, out _);
            client.Dispose();
            AnnounceStatus();

            logger.LogInformation("Broadcast: a client left ({Count} now)", _clients.Count);
        }
    }

    private async Task CloseAllAsync()
    {
        foreach (var (id, client) in _clients)
        {
            _clients.TryRemove(id, out _);
            client.Dispose();
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// 1 通ぶんの JSON。形は OBS のオーバーレイや外部ツールが読んでいるので、変えると読めなくなる。
    /// <para>
    /// <c>welcome</c> にだけ直近の心拍を付ける。オーバーレイは <c>d.history</c> を読む枝を持っているので、
    /// 付けないとグラフが埋まるまで 1 分かかる。
    /// </para>
    private string Serialize(HrsBroadcastState state, string type, bool withHistory) =>
        JsonSerializer.Serialize(HrsBroadcastMessage.Of(state, type, withHistory ? history?.Invoke() : null), Json);

    /// <summary>
    /// 繋がっている相手 1 つ。<b>送る係と受ける係を分ける</b> —
    /// WebSocket は閉じられたことを受信側でしか知れないので、読まずにいると
    /// 落ちた相手をいつまでも数に入れてしまう。
    /// </summary>
    private sealed class Client(WebSocket socket) : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();

        /// <summary>溜まったら古いものから捨てる。遅れて届く心拍に価値は無い。</summary>
        public Channel<string> Outbox { get; } = Channel.CreateBounded<string>(
            new BoundedChannelOptions(OutboxLimit) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

            await Task.WhenAny(SendLoopAsync(linked.Token), ReceiveLoopAsync(linked.Token)).ConfigureAwait(false);

            await _cts.CancelAsync().ConfigureAwait(false);
        }

        private async Task SendLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (await Outbox.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (Outbox.Reader.TryRead(out var json))
                    {
                        await socket.SendAsync(
                                Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
            {
                // 相手が居なくなった。
            }
        }

        /// <summary>
        /// 読み捨てる。<b>中身に用は無い</b>（配信は一方通行）が、読まないと
        /// 閉じられたことに気付けない。
        /// </summary>
        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[256];

            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    var received = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (received.MessageType == WebSocketMessageType.Close) return;
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
            {
                // 相手が居なくなった。
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            socket.Dispose();
        }
    }
}
