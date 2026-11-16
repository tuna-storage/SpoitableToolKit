using System.Net;
using System.Net.NetworkInformation;

namespace SpoitableToolKit.Modules.Hrs.Devices.Hds;

/// <summary>この PC のネットワークの口 1 つ（<see cref="LanAddresses"/> が選ぶ元）。本物は <see cref="LanAddresses.ReadSystem"/> が読み、試験は自分で並べる。</summary>
/// <param name="Name">Windows が付けた口の名前（Wi-Fi・イーサネットなど）。どのアドレスかを見分けるために横に出す。</param>
/// <param name="Gateways">既定のゲートウェイ。無ければ空。</param>
internal sealed record NetworkCard(
    string Name,
    OperationalStatus Status,
    NetworkInterfaceType Type,
    IReadOnlyList<IPAddress> Addresses,
    IReadOnlyList<IPAddress> Gateways);
