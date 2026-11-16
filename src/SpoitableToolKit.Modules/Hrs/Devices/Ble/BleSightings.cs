using System.Collections.Concurrent;
using SpoitableToolKit.Modules.Hrs.Devices.Models;

namespace SpoitableToolKit.Modules.Hrs.Devices.Ble;

/// <summary>
/// スキャンで見つかった心拍計。<b>一度分かった名前は手放さない</b> —
/// 能動スキャンでは名前の無い広告と名前のある応答が交互に来るので、最後の 1 通で上書きすると
/// 名前がアドレスへ戻ってしまう。
/// <para>
/// 電波の強さと最後に見た時刻は名前と逆で、<b>最後の 1 通が正しい</b>（広告のたびに差し替える）。
/// 並びは見つけた順（辞書の並びは決まっていない）。
/// </para>
/// </summary>
internal sealed class BleSightings
{
    private readonly ConcurrentDictionary<ulong, Entry> _devices = [];
    private readonly Lock _gate = new();

    /// <summary>次に見つけた心拍計に振る番号（見つけた順に並べるため）。書くのは <see cref="_gate"/> の中。</summary>
    private long _found;

    /// <param name="Order">見つけた順の番号。</param>
    private readonly record struct Entry(MonitorSighting Seen, long Order);

    public IReadOnlyList<MonitorIdentity> All => [.. Ordered().Select(entry => entry.Seen.Monitor)];

    /// <summary>見つけた心拍計と、最後に広告を受けたときの様子（<see cref="All"/> と同じ並び）。</summary>
    public IReadOnlyList<MonitorSighting> Seen => [.. Ordered().Select(entry => entry.Seen)];

    private IEnumerable<Entry> Ordered() => _devices.Values.OrderBy(entry => entry.Order);

    /// <summary>
    /// 心拍サービスを名乗る広告を 1 通受けた。返す <see cref="Sighting"/> は<b>名前の変わり目だけ</b>を見る（ログに書くかどうか）—
    /// 電波の強さと時刻は毎回変わるので、そこで「変わった」とは言わない。
    /// </summary>
    public (MonitorIdentity Device, Sighting What) See(ulong address, string? localName, int signalDbm, DateTimeOffset at)
    {
        var named = !string.IsNullOrWhiteSpace(localName);
        var seen = new MonitorIdentity(address, named ? Clean(localName!) : MonitorIdentity.FormatAddress(address));

        lock (_gate)
        {
            if (!_devices.TryGetValue(address, out var known))
            {
                _devices[address] = new Entry(new MonitorSighting(seen, signalDbm, at), _found++);
                return (seen, Sighting.New);
            }

            var keep = !named || known.Seen.Monitor.Name == seen.Name;
            var monitor = keep ? known.Seen.Monitor : seen;

            _devices[address] = known with { Seen = new MonitorSighting(monitor, signalDbm, at) };
            return (monitor, keep ? Sighting.Known : Sighting.Named);
        }
    }

    /// <summary>
    /// 心拍サービスを名乗らない広告を受けた（広告から心拍を読んでいる相手の製造元データ）。<b>知っている相手だけ</b>電波の強さと時刻を差し替える —
    /// 一覧に足すのは心拍サービスを名乗る機器だけ（近くの BLE 機器を全部並べない）。
    /// </summary>
    public void Touch(ulong address, int signalDbm, DateTimeOffset at)
    {
        lock (_gate)
        {
            if (_devices.TryGetValue(address, out var known))
                _devices[address] = known with { Seen = known.Seen with { SignalDbm = signalDbm, LastSeen = at } };
        }
    }

    /// <summary>
    /// 繋いだ先が名乗った名前（<c>BluetoothLEDevice.Name</c>）。広告に名前が無い機器でも、
    /// 繋げば分かることが多い。空なら何もしない。電波の強さ・時刻・並びはそのまま（名乗りは広告ではない）。
    /// </summary>
    public MonitorIdentity Rename(MonitorIdentity device, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || device.Name == Clean(name)) return device;

        var renamed = device with { Name = Clean(name) };
        lock (_gate)
        {
            _devices[device.Address] = _devices.TryGetValue(device.Address, out var known)
                ? known with { Seen = known.Seen with { Monitor = renamed } }
                : new Entry(new MonitorSighting(renamed, null, null), _found++);
        }

        return renamed;
    }

    /// <summary>名前の長さの上限。ふつうの心拍計は 20 字前後（「Polar Sense 12345678」）。</summary>
    internal const int MaxName = 48;

    /// <summary>
    /// 機器が名乗った名前を、<b>ログと画面に出してよい形</b>にする。名前は近くの誰でも好きに付けられるので、
    /// 改行や制御文字を混ぜるとログの行を偽造できる（繋いだ機器の名前は最大 248 バイトまで来うる）。
    /// 制御文字（行区切りの U+2028 / U+2029 も）は <c>?</c> に置き換え、長すぎれば切る。
    /// </summary>
    internal static string Clean(string name)
    {
        var trimmed = name.Trim();
        var chars = trimmed.Select(c => char.IsControl(c) || c is '\u2028' or '\u2029' ? '?' : c).ToArray();
        var clean = new string(chars);
        return clean.Length > MaxName ? clean[..MaxName] + "…" : clean;
    }
}
