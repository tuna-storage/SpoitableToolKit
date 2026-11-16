using System.Net;
using System.Net.NetworkInformation;
using SpoitableToolKit.Modules.Hrs.Devices.Hds;
using Xunit;

namespace SpoitableToolKit.Tests.Hrs.Devices;

/// <summary>時計（HDS）に入れてもらう、この PC のアドレスの選び方。口は試験で並べる（この PC の本当のアドレスに頼らない）。</summary>
public sealed class LanAddressesTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 16, 3, 0, 0, TimeSpan.Zero);

    private static IPAddress Ip(string text) => IPAddress.Parse(text);

    private static NetworkCard Card(string name, OperationalStatus status, NetworkInterfaceType type, string[] addresses, string[] gateways) =>
        new(name, status, type, [.. addresses.Select(Ip)], [.. gateways.Select(Ip)]);

    /// <summary>
    /// 選ぶのは、動いていて、ループバックでもトンネルでもなく、IPv4 の既定のゲートウェイを持つ口の IPv4 だけ（ふつうは LAN の 1 つ）。
    /// 落とす口はそれぞれ 1 つの決まりだけに掛かるように並べる（ループバックとトンネルにもゲートウェイを持たせる）。
    /// </summary>
    [Fact]
    public void OnlyUpLanCardsWithAnIPv4GatewayGiveTheirIPv4Addresses()
    {
        NetworkCard[] cards =
        [
            Card("Wi-Fi", OperationalStatus.Up, NetworkInterfaceType.Wireless80211, ["192.168.1.20", "fe80::1234"], ["fe80::1", "192.168.1.1"]),
            Card("Down", OperationalStatus.Down, NetworkInterfaceType.Ethernet, ["192.168.1.30"], ["192.168.1.1"]),
            Card("Loopback", OperationalStatus.Up, NetworkInterfaceType.Loopback, ["127.0.0.1"], ["127.0.0.1"]),
            Card("VPN", OperationalStatus.Up, NetworkInterfaceType.Tunnel, ["10.8.0.2"], ["10.8.0.1"]),
            Card("vEthernet (WSL)", OperationalStatus.Up, NetworkInterfaceType.Ethernet, ["172.20.0.1"], []),
            Card("Zero gateway", OperationalStatus.Up, NetworkInterfaceType.Ethernet, ["10.9.0.2"], ["0.0.0.0"]),
            Card("IPv6 gateway only", OperationalStatus.Up, NetworkInterfaceType.Ethernet, ["10.10.0.2"], ["fe80::1"]),
            Card("イーサネット 2", OperationalStatus.Up, NetworkInterfaceType.Ethernet, ["10.0.0.5"], ["10.0.0.1"]),
        ];

        string[] picked = [.. LanAddresses.Pick(cards).Select(address => $"{address.Name} {address.Ip}")];

        Assert.Equal(["Wi-Fi 192.168.1.20", "イーサネット 2 10.0.0.5"], picked);
    }

    /// <summary>口を数えるのは数 ms かかることがあり、パネルは 3 秒おきに読み直すので、少しの間は覚えておく。過ぎたら読み直す（Wi-Fi を替えたら追いつく）。</summary>
    [Fact]
    public void TheAddressesAreKeptBrieflyThenReadAgain()
    {
        var reads = 0;
        var now = At;
        var lan = new LanAddresses(() =>
        {
            reads++;
            return [Card("Wi-Fi", OperationalStatus.Up, NetworkInterfaceType.Wireless80211, ["192.168.1.20"], ["192.168.1.1"])];
        }, () => now);

        Assert.Single(lan.Current);
        Assert.Single(lan.Current);
        Assert.Equal(1, reads);

        now += LanAddresses.KeepFor - TimeSpan.FromSeconds(1);
        Assert.Single(lan.Current);
        Assert.Equal(1, reads);

        now += TimeSpan.FromSeconds(2);
        Assert.Single(lan.Current);
        Assert.Equal(2, reads);
    }
}
