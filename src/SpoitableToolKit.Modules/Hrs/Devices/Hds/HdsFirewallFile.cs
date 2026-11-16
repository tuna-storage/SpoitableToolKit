namespace SpoitableToolKit.Modules.Hrs.Devices.Hds;

/// <summary>
/// e2e のファイアウォール（<c>SPOITABLE_HDS_FIREWALL=&lt;ファイル&gt;</c>）。<b>本物の PowerShell も UAC も起こさない。</b>
/// 中身が答え（<c>missing</c> / <c>present</c> / <c>blocked</c>。ほかは「読めなかった」）で、足すと <c>present</c> を書き、
/// <c>&lt;ファイル&gt;.added</c> にポートを 1 行足す（試験は押された回数とポートをそこで見る）。
/// <para>
/// これを使う間、HDS はループバックだけで待つ（<see cref="HrsModule"/>）。0.0.0.0 で待つと、試験の exe に Windows がファイアウォールの確認を出す
/// （キャンセルすると、その exe にブロックのルールが残る）。LAN から届くかは、どのみち偽のファイアウォールでは確かめられない。
/// </para>
/// </summary>
internal sealed class HdsFirewallFile(string path)
{
    public const string Variable = "SPOITABLE_HDS_FIREWALL";

    public string Path => path;

    public HdsFirewall.State Check()
    {
        try
        {
            return File.ReadAllText(path).Trim() switch
            {
                "present" => HdsFirewall.State.Present,
                "missing" => HdsFirewall.State.Missing,
                "blocked" => HdsFirewall.State.Blocked,
                _ => HdsFirewall.State.Failed,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return HdsFirewall.State.Failed;
        }
    }

    public bool Add(int port)
    {
        File.AppendAllText(path + ".added", port + Environment.NewLine);
        File.WriteAllText(path, "present");
        return true;
    }
}
