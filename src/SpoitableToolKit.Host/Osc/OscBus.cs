using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Osc;
using SpoitableToolKit.Host.Settings;

namespace SpoitableToolKit.Host.Osc;

/// <summary>
/// OSC 1.0 のメッセージを組み立てて UDP で投げる。
/// <para>
/// ライブラリを入れていないのは、要るのが<b>送信側の 4 つの型だけ</b>だから
/// （<c>int</c> / <c>float</c> / <c>bool</c> / <c>string</c>）。バンドルもタイムタグも
/// blob も使わないので、仕様のうち実際に踏む部分は「ビッグエンディアン」と
/// 「4 バイト境界のパディング」の 2 つしかない。
/// </para>
/// <para>
/// 宛先ポートは<b>送るたびに設定から読む</b>。設定のインスタンスはホストと共有しているので、
/// 設定画面で変えたポートがその場から効く（listener が要らない）。
/// </para>
/// </summary>
internal sealed class OscBus(HostConfig config, ILoggerFactory loggerFactory) : IOscBus, IDisposable
{
    private readonly ILogger<OscBus> _logger = loggerFactory.CreateLogger<OscBus>();

    // ソケットは 1 本を使い回す。心拍のように毎秒何通も出す使い方をされるので、
    // 送るたびに開くと短命ポートを食い潰す。
    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

    public void Send(string address, params object[] args)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);

        var packet = Encode(address, args);
        var destination = new IPEndPoint(IPAddress.Loopback, config.OscPort);

        try
        {
            _socket.SendTo(packet, destination);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // 送りっぱなしの経路。返す相手が居ないので、記録して捨てる
            // （VRChat が起動していないのはふつうのことで、異常ではない）。
            // 畳んだあとの送信もここに来る。モジュールの後片付けが間に合わなかった、
            // というだけなのでアプリの終了は止めない。
            _logger.LogWarning(ex, "Could not send OSC to {Destination} ({Address})", destination, address);
        }
    }

    /// <summary>
    /// 1 通分のバイト列。<b>アドレス → 型タグ → 引数</b>の順で、それぞれ 4 バイト境界に揃える。
    /// </summary>
    internal static byte[] Encode(string address, params object[] args)
    {
        var buffer = new List<byte>();

        WriteString(buffer, address);

        // 型タグは必ず ',' で始まる 1 本の文字列。引数が 0 個でも "," を書く。
        var tags = new StringBuilder(",");
        foreach (var arg in args)
        {
            tags.Append(arg switch
            {
                int => 'i',
                float => 'f',
                bool value => value ? 'T' : 'F',
                string => 's',
                _ => throw new ArgumentException(
                    $"Cannot send {arg?.GetType().FullName ?? "null"} over OSC. " +
                    "Use one of int / float / bool / string.", nameof(args)),
            });
        }

        WriteString(buffer, tags.ToString());

        foreach (var arg in args)
        {
            switch (arg)
            {
                case int value:
                    WriteInt32(buffer, value);
                    break;

                case float value:
                    WriteFloat(buffer, value);
                    break;

                // bool は型タグ（T / F）だけで表す。中身は 1 バイトも書かない。
                case bool:
                    break;

                case string value:
                    WriteString(buffer, value);
                    break;
            }
        }

        return [.. buffer];
    }

    /// <summary>
    /// 文字列は null 終端し、<b>終端まで含めて</b> 4 バイト境界へ揃える
    /// （4 文字なら終端 1 + パディング 3 で 8 バイトになる）。
    /// </summary>
    private static void WriteString(List<byte> buffer, string value)
    {
        buffer.AddRange(Encoding.UTF8.GetBytes(value));
        buffer.Add(0);

        while (buffer.Count % 4 != 0) buffer.Add(0);
    }

    // OSC はネットワークバイトオーダー（ビッグエンディアン）。x64 はリトルエンディアンなので、
    // BitConverter をそのまま使うと通るのに読めない、という形で壊れる。
    private static void WriteInt32(List<byte> buffer, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        buffer.AddRange(bytes);
    }

    private static void WriteFloat(List<byte> buffer, float value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteSingleBigEndian(bytes, value);
        buffer.AddRange(bytes);
    }

    public void Dispose() => _socket.Dispose();
}
