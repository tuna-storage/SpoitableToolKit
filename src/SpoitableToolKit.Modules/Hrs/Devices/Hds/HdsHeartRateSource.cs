using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Modules.Hrs.Config;
using SpoitableToolKit.Modules.Hrs.Devices.Interfaces;
using SpoitableToolKit.Modules.Hrs.Devices.Models;

namespace SpoitableToolKit.Modules.Hrs.Devices.Hds;

/// <summary>
/// Health Data Server（Apple Watch）から心拍を受ける。
/// <para>
/// 時計のアプリが <c>PUT /</c> に <c>{"data":"heartRate:72"}</c> を 1 件ずつ投げてくるだけ。
/// <b>HTTP のライブラリは足していない</b> — 読むのは 1 つの動詞と本文だけだし、
/// <c>HttpListener</c> は 0.0.0.0 で待つのに urlacl の登録（＝管理者）が要る。
/// </para>
/// <para>
/// <b>誰でも投げ込める口</b>なので、既定では開かない。心拍として読めない本文は黙って捨てて、
/// 常に 200 を返す（時計側は結果を見ていないし、こちらの都合を教える相手でもない）。
/// </para>
/// </summary>
/// <param name="bindTo">
/// 待ち受ける宛先。<b>既定の 0.0.0.0 が本番の姿</b>で、LAN の時計から届くのはこれだけ。
/// 試験からループバックを渡すのは、<b>試験のたびにファイアウォールの許可を求めない</b>ため
/// （LAN から本当に届くかは実機でしか確かめられない）。
/// </param>
internal sealed class HdsHeartRateSource(
    HrsConfig config,
    ILogger logger,
    TimeProvider? time = null,
    IPAddress? bindTo = null)
    : IHeartRateSource
{
    /// <summary>本文の上限。心拍 1 つの JSON しか来ないので、これ以上は読まずに切る。</summary>
    private const int MaxBody = 64 * 1024;

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private DateTimeOffset _lastSample;
    private MonitorIdentity? _sender;

    /// <summary>設定の候補には出さない。BLE のアドレスで指すものではないため。</summary>
    public IReadOnlyList<MonitorIdentity> Discovered => [];

    /// <summary>
    /// 最後に心拍をくれた相手。<b>黙ったら自然に消える</b>ので、切れたことを知らせる係が要らない
    /// （時計は「もう送りません」とは言わずに黙るだけ）。
    /// </summary>
    public MonitorIdentity? Connected =>
        _time.GetUtcNow() - _lastSample < SilenceAfter ? _sender : null;

    public HeartRateConnection Connection =>
        Connected is null ? HeartRateConnection.None : HeartRateConnection.Hds;

    public TimeSpan SilenceAfter => TimeSpan.FromSeconds(config.Hds.NoSignalSeconds);

    private volatile IHeartRateSink? _sink;

    public void ReportTo(IHeartRateSink sink) => _sink = sink;

    /// <summary>手で切った送り主（パネルの「切断」）。「接続」まで、その送り主の心拍は受けない（ほかの送り主は受ける）。</summary>
    private string[] _heldOff = [];

    private readonly Lock _heldGate = new();

    private int _ignored;

    /// <summary>手で切ってある送り主から届いて捨てた心拍の数。試験が「捨てた」を待たずに見るための口。</summary>
    internal int IgnoredWhileDisconnected => Volatile.Read(ref _ignored);

    /// <summary>手で切る（その送り主だけ）。いまの送り主なら、その場で「繋がっていない」にする。</summary>
    public void Disconnect(MonitorIdentity monitor)
    {
        lock (_heldGate) _heldOff = [.. _heldOff, monitor.Name];

        if (_sender is { } sender && sender.Name == monitor.Name)
        {
            _sender = null;
            logger.LogInformation("HDS: disconnected {From} by hand; ignoring it until Connect", monitor.Name);
            _sink?.LinkChanged(this);
        }
    }

    public void Connect(MonitorIdentity monitor)
    {
        lock (_heldGate) _heldOff = [.. _heldOff.Where(name => name != monitor.Name)];
    }

    /// <summary>
    /// 待ち受ける。<b>無効なら何もせず、畳まれるまで黙って待つ</b> —
    /// 「動いていない入口」も他と同じ形で並べておけると、調停する側が場合分けを持たずに済む。
    /// </summary>
    public async Task RunAsync(DeviceSelector selector, CancellationToken cancellationToken)
    {
        if (!config.Hds.Enabled)
        {
            await Idle(cancellationToken).ConfigureAwait(false);
            return;
        }

        var port = config.Hds.Port;
        TcpListener listener;

        try
        {
            listener = new TcpListener(bindTo ?? IPAddress.Any, port);
            listener.Start();
        }
        catch (Exception ex)
        {
            // ポートが埋まっている / 使えないのはふつうに起きる。BLE 側は動かしたままにする。
            logger.LogWarning(ex, "Could not start listening for HDS (port {Port})", port);
            await Idle(cancellationToken).ConfigureAwait(false);
            return;
        }

        logger.LogInformation("Listening for HDS on port {Port}", port);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);

                // 1 件ずつ投げてくるだけなので、受けた順に捌く。取りこぼしても次が来る。
                _ = HandleAsync(client, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 畳まれた。
        }
        finally
        {
            listener.Stop();
            _sender = null;
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

    private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);

                // 読めなくても 200 を返す。時計は結果を見ていないし、
                // 「何が悪いか」を教える相手でもない（誰でも投げ込める口なので）。
                await stream.WriteAsync(
                    Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"),
                    cancellationToken).ConfigureAwait(false);

                if (request is null) return;

                if (ParseHeartRate(request) is not { } heartRate) return;

                var from = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
                Report(from, heartRate);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Stumbled while receiving HDS");
            }
        }
    }

    /// <summary>
    /// 本文だけ取り出す。<b>読むのはヘッダの終わりと <c>Content-Length</c> だけ</b> —
    /// 相手は 1 種類のアプリなので、HTTP を一般に喋れる必要が無い。
    /// </summary>
    private static async Task<string?> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxBody];
        var filled = 0;
        var headerEnd = -1;

        while (headerEnd < 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(filled), cancellationToken).ConfigureAwait(false);
            if (read == 0) return null;

            filled += read;
            headerEnd = Find(buffer, filled);

            // ヘッダだけで上限を埋めるような相手は、HDS のアプリではない。
            if (headerEnd < 0 && filled == buffer.Length) return null;
        }

        var header = Encoding.ASCII.GetString(buffer, 0, headerEnd);
        var length = ContentLength(header);
        var bodyStart = headerEnd + 4;

        if (length <= 0) return null;
        if (length > MaxBody - bodyStart) return null;

        while (filled - bodyStart < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(filled), cancellationToken).ConfigureAwait(false);
            if (read == 0) return null;

            filled += read;
        }

        return Encoding.UTF8.GetString(buffer, bodyStart, length);
    }

    private static int Find(byte[] buffer, int filled)
    {
        for (var i = 0; i + 3 < filled; i++)
            if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                return i;

        return -1;
    }

    private static int ContentLength(string header)
    {
        foreach (var line in header.Split("\r\n"))
        {
            if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) continue;

            return int.TryParse(line["Content-Length:".Length..].Trim(), out var length) ? length : 0;
        }

        return 0;
    }

    /// <summary>
    /// <c>{"data":"heartRate:72"}</c> から心拍を取る。<b>heartRate 以外は捨てる</b>
    /// （時計は歩数や消費カロリーも同じ口へ投げてくる）。
    /// </summary>
    internal static int? ParseHeartRate(string body)
    {
        string? data;

        try
        {
            data = JsonDocument.Parse(body).RootElement.TryGetProperty("data", out var value)
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }

        if (data is null || !data.StartsWith("heartRate:", StringComparison.OrdinalIgnoreCase)) return null;
        if (!double.TryParse(data["heartRate:".Length..].Trim(), out var bpm)) return null;

        // 0 や 4桁は時計側の取りこぼしなので、心拍として流さない。
        return bpm is >= 1 and <= 400 ? (int)Math.Round(bpm) : null;
    }

    private void Report(string from, int heartRate)
    {
        // 手で切った送り主は、繋がった印を書く前に捨てる（書くと、窓口が捨てても「繋がっている」が時刻で残る）。
        if (Array.IndexOf(Volatile.Read(ref _heldOff), from) >= 0)
        {
            Interlocked.Increment(ref _ignored);
            return;
        }

        var changed = Connected is null || _sender?.Name != from;

        _sender = new MonitorIdentity(0, from);
        _lastSample = _time.GetUtcNow();

        if (changed)
        {
            logger.LogInformation("HDS: heart rate received from {From}", from);
            _sink?.LinkChanged(this);
        }

        _sink?.Beat(this, heartRate);
    }
}
