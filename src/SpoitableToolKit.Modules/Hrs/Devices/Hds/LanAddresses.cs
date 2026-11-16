using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SpoitableToolKit.Modules.Hrs.Devices.Hds;

/// <summary>
/// 時計（Health Data Server）に入れてもらう、この PC のアドレス。待ち受けは全部の口（0.0.0.0）なので、
/// その中から<b>時計から届きそうな口</b> — 動いていて、ループバックでもトンネルでもなく、既定のゲートウェイ（IPv4）を持つ口の IPv4 — を選ぶ（ふつうは LAN の 1 つ）。
/// <para>
/// 口を数えるのは数 ms かかることがあるので、<see cref="KeepFor"/> の間は覚えておく（パネルは心拍計タブを開いている間、3 秒おきに読み直す）。
/// Wi-Fi を替えたら、その時間のうちに追いつく。
/// </para>
/// </summary>
/// <param name="read">口を読む。本物は <see cref="ReadSystem"/>、試験は自分で並べる（この PC の本当のアドレスに頼らない）。</param>
/// <param name="now">覚えておく時間を数える時計。</param>
internal sealed class LanAddresses(Func<IReadOnlyList<NetworkCard>> read, Func<DateTimeOffset> now)
{
    internal static readonly TimeSpan KeepFor = TimeSpan.FromSeconds(5);

    private readonly Lock _gate = new();
    private (DateTimeOffset At, IReadOnlyList<LanAddress> Addresses)? _kept;

    public IReadOnlyList<LanAddress> Current
    {
        get
        {
            var at = now();

            lock (_gate)
            {
                if (_kept is { } kept && at - kept.At < KeepFor) return kept.Addresses;
            }

            IReadOnlyList<LanAddress> addresses;
            try
            {
                addresses = Pick(read());
            }
            catch (Exception ex) when (ex is NetworkInformationException or SocketException or InvalidOperationException)
            {
                // 読めなくても画面は開く（「LAN のアドレスが見つかりません」が出る）。
                addresses = [];
            }

            lock (_gate) _kept = (at, addresses);
            return addresses;
        }
    }

    /// <summary>口から、時計から届きそうなアドレスを選ぶ。並びは口の並び（Windows の順）のまま。</summary>
    internal static IReadOnlyList<LanAddress> Pick(IEnumerable<NetworkCard> cards) =>
    [
        .. from card in cards
           where card.Status == OperationalStatus.Up
           where card.Type is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
           where card.Gateways.Any(IsIPv4Gateway)
           from address in card.Addresses
           where address.AddressFamily == AddressFamily.InterNetwork
           select new LanAddress(card.Name, address),
    ];

    /// <summary>0.0.0.0 はゲートウェイではない（VPN などの口が持っていることがある）。</summary>
    private static bool IsIPv4Gateway(IPAddress gateway) =>
        gateway.AddressFamily == AddressFamily.InterNetwork && !gateway.Equals(IPAddress.Any);

    /// <summary>この PC の口を読む。</summary>
    public static IReadOnlyList<NetworkCard> ReadSystem() =>
    [
        .. NetworkInterface.GetAllNetworkInterfaces().Select(nic =>
        {
            var properties = nic.GetIPProperties();
            return new NetworkCard(
                nic.Name,
                nic.OperationalStatus,
                nic.NetworkInterfaceType,
                [.. properties.UnicastAddresses.Select(unicast => unicast.Address)],
                [.. properties.GatewayAddresses.Select(gateway => gateway.Address)]);
        }),
    ];
}
