using System.Net;
using System.Net.Sockets;

namespace SpoitableToolKit.E2E.Hrs;

/// <summary>
/// HDS（Apple Watch）を入にした hrs のアプリ。ファイアウォールは本物ではなくファイル（<c>SPOITABLE_HDS_FIREWALL</c>）で、
/// 中身（missing / present / blocked）が答え、「追加」は present を書いて <see cref="Added"/> にポートを 1 行足す（本物の PowerShell も UAC も起こさない）。
/// その間、HDS はループバックだけで待つ — 0.0.0.0 で待つと、試験の exe に Windows がファイアウォールの確認を出す。
/// ポートは空いている番号（手元のアプリが HDS の標準ポートで待っていてもぶつからない）。
/// </summary>
public sealed class HdsFirewallAppFixture : AppFixture
{
    public string Firewall => Path.Combine(DataDirectory, "firewall.txt");

    public string Added => Firewall + ".added";

    /// <summary>いま適用してある HDS のポート。ポートを変えた試験が書き換える。</summary>
    public int Port { get; set; } = FreeLoopbackPort();

    public void SetFirewall(string state) => File.WriteAllText(Firewall, state);

    public string[] AddedPorts() => File.Exists(Added) ? File.ReadAllLines(Added) : [];

    public static int FreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    protected override void Prepare()
    {
        SetFirewall("missing");

        // 心拍計は居ないアドレスのまま（AppFixture と同じ）。HDS を入にして、空いているポートで待たせる。
        File.WriteAllText(Path.Combine(DataDirectory, "module-data", "hrs", "config.json"),
            $$"""{ "Device": "{{NoHeartRateMonitor}}", "Hds": { "Enabled": true, "Port": {{Port}} } }""");

        ExtraEnvironment["SPOITABLE_HDS_FIREWALL"] = Firewall;
    }
}
