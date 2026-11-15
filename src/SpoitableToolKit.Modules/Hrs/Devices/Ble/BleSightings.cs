using System.Collections.Concurrent;
using SpoitableToolKit.Modules.Hrs.Devices.Models;

namespace SpoitableToolKit.Modules.Hrs.Devices.Ble;

/// <summary>
/// スキャンで見つかった心拍計。<b>一度分かった名前は手放さない</b> —
/// 能動スキャンでは名前の無い広告と名前のある応答が交互に来るので、最後の 1 通で上書きすると
/// 名前がアドレスへ戻ってしまう。
/// </summary>
internal sealed class BleSightings
{
    private readonly ConcurrentDictionary<ulong, MonitorIdentity> _devices = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<MonitorIdentity> All => [.. _devices.Values];

    public (MonitorIdentity Device, Sighting What) See(ulong address, string? localName)
    {
        var named = !string.IsNullOrWhiteSpace(localName);
        var seen = new MonitorIdentity(address, named ? Clean(localName!) : MonitorIdentity.FormatAddress(address));

        lock (_gate)
        {
            if (!_devices.TryGetValue(address, out var known))
            {
                _devices[address] = seen;
                return (seen, Sighting.New);
            }

            if (!named || known.Name == seen.Name) return (known, Sighting.Known);

            _devices[address] = seen;
            return (seen, Sighting.Named);
        }
    }

    /// <summary>
    /// 繋いだ先が名乗った名前（<c>BluetoothLEDevice.Name</c>）。広告に名前が無い機器でも、
    /// 繋げば分かることが多い。空なら何もしない。
    /// </summary>
    public MonitorIdentity Rename(MonitorIdentity device, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || device.Name == Clean(name)) return device;

        var renamed = device with { Name = Clean(name) };
        lock (_gate) _devices[device.Address] = renamed;
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
