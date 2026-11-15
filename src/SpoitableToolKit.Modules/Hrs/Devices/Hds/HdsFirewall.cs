using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SpoitableToolKit.Modules.Hrs.Devices.Hds;

/// <summary>
/// HDS の待ち受けが Windows のファイアウォールを通れるか見る。通れなければ人が押して直せる。
/// <para>
/// <b>アプリからは勝手に足さない。</b> ルールの追加はどの API でも管理者が要る
/// （<c>netsh</c> でも <c>INetFwPolicy2</c> でも、拒まれるのは操作の方）。設定を保存しただけで
/// UAC が出るのは筋が悪いので、<b>初回の待ち受けで Windows 自身が出すプロンプトに任せる</b> —
/// 許可すると<b>アプリ単位</b>のルールが作られる（ポート単位より狭い）。
/// </para>
/// <para>
/// そこで「キャンセル」を押すと<b>ブロックのルールが残り、二度と聞かれなくなる</b>。
/// <b>ブロックは許可より強い</b>ので、足すだけでは届かない — 先に消す必要がある。
/// それを 1 回の UAC でやるのが <see cref="Fix"/> で、<b>人がボタンを押したときだけ</b>呼ぶ。
/// </para>
/// <para>
/// 見るのも直すのも PowerShell の <c>*-NetFirewallRule</c> を使う。<c>netsh</c> の一覧は
/// <b>項目名も値も翻訳される</b>ので、「許可かブロックか」を読むのに向かない
/// （<c>Action</c> は enum で返る）。
/// </para>
/// </summary>
internal static class HdsFirewall
{
    /// <summary>通れそうか。<b>見えない = 必ず届かない、ではない</b>（読めないこともある）。</summary>
    internal enum State
    {
        Unknown,
        Present,
        Missing,

        /// <summary>
        /// <b>ブロックのルールがある。</b> Windows のプロンプトでキャンセルすると作られる。
        /// 許可を足しても<b>ブロックが勝つ</b>ので、消さないと届かない。
        /// </summary>
        Blocked,
    }

    private static string RuleName(int port) => $"SpoitableToolKit HDS ({port})";

    /// <summary>
    /// ルールの説明。<b>Windows のファイアウォール一覧に並ぶのはこれ</b>なので、
    /// あとから見て「何のために開けた口か」が分かるように書く（消す判断ができるように）。
    /// </summary>
    private static string Description(int port) =>
        $"SpoitableToolKit の hrs モジュール: Health Data Server (Apple Watch) の心拍を "
        + $"LAN から受け取るための受信口 (TCP {port})。HDS を使わないなら消してよい。";

    /// <summary>
    /// 通れそうか見る。<b>時間がかかるので UI のスレッドから呼ばないこと</b>（PowerShell を起こす）。
    /// 読むだけなので管理者は要らない。
    /// </summary>
    public static State Check(int port, ILogger logger)
    {
        var script = $$"""
            $ErrorActionPreference = 'SilentlyContinue'
            $exe = '{{Quote(Environment.ProcessPath ?? "")}}'
            $rule = '{{Quote(RuleName(port))}}'
            $actions = @()
            if ($exe) { $actions += Get-NetFirewallApplicationFilter -Program $exe | Get-NetFirewallRule | Where-Object { $_.Direction -eq 'Inbound' -and $_.Enabled -eq 'True' } | ForEach-Object { $_.Action.ToString() } }
            $actions += Get-NetFirewallRule -DisplayName $rule | Where-Object { $_.Direction -eq 'Inbound' -and $_.Enabled -eq 'True' } | ForEach-Object { $_.Action.ToString() }
            if ($actions -contains 'Block') { 'blocked' } elseif ($actions -contains 'Allow') { 'present' } else { 'missing' }
            """;

        try
        {
            using var process = Process.Start(new ProcessStartInfo("powershell", Arguments(script))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process is null) return State.Unknown;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(TimeSpan.FromSeconds(30));

            return output switch
            {
                "blocked" => State.Blocked,
                "present" => State.Present,
                "missing" => State.Missing,

                // 読めなかっただけ。届かないと決まったわけではないので、パネルには何も出さない。
                _ => State.Unknown,
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not check the firewall rules");
            return State.Unknown;
        }
    }

    /// <summary>
    /// 通れるようにする。<b>人がボタンを押したときだけ呼ぶ</b> — 管理者へ上げるので UAC が出る
    /// （押した直後なら、出るのが自然に見える）。
    /// <para>
    /// <b>ブロックを先に消してから許可を足す。</b> 消さずに足しても、ブロックが勝つので届かない。
    /// 消すのは<b>この実行ファイル宛の受信ルール</b>だけに絞る。
    /// </para>
    /// <para>
    /// 足すのはプライベートとドメインだけ（<b>公衆 Wi-Fi でこの口を開けない</b>）。
    /// 裏返すと、そのネットワークが「パブリック」だと届かない。
    /// </para>
    /// </summary>
    public static bool Fix(int port, ILogger logger)
    {
        var script = $$"""
            try {
            $exe = '{{Quote(Environment.ProcessPath ?? "")}}'
            $rule = '{{Quote(RuleName(port))}}'
            if ($exe) { Get-NetFirewallApplicationFilter -Program $exe -ErrorAction SilentlyContinue | Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object { $_.Direction -eq 'Inbound' -and $_.Action -eq 'Block' } | Remove-NetFirewallRule -ErrorAction SilentlyContinue }
            Remove-NetFirewallRule -DisplayName $rule -ErrorAction SilentlyContinue
            New-NetFirewallRule -DisplayName $rule -Description '{{Quote(Description(port))}}' -Direction Inbound -Action Allow -Protocol TCP -LocalPort {{port}} -Profile Private,Domain -ErrorAction Stop | Out-Null
            exit 0
            } catch { exit 1 }
            """;

        try
        {
            using var process = Process.Start(new ProcessStartInfo("powershell", Arguments(script))
            {
                // 管理者に上げる。UseShellExecute でないと runas が効かない
                // （その代わり出力は受け取れないので、結果は終了コードで見る）。
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            if (process is null) return false;

            process.WaitForExit(TimeSpan.FromMinutes(2));

            if (process.HasExited && process.ExitCode == 0)
            {
                logger.LogInformation("Fixed the firewall rule '{Rule}'", RuleName(port));
                return true;
            }

            logger.LogWarning("Could not fix the firewall rules (exit code {Code})", process.ExitCode);
            return false;
        }
        catch (Win32Exception ex)
        {
            // 1223 = 利用者が UAC を断った。断るのは正当な選択なので、警告より下で残す。
            logger.LogInformation(ex, "Left the firewall rules untouched");
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not fix the firewall rules");
            return false;
        }
    }

    /// <summary>手で直したい人のための 1 行。パネルに出してコピーできるようにする。</summary>
    public static string CommandFor(int port) =>
        $"New-NetFirewallRule -DisplayName '{RuleName(port)}' -Description '{Description(port)}' "
        + $"-Direction Inbound -Action Allow -Protocol TCP -LocalPort {port} -Profile Private,Domain";

    /// <summary>
    /// 複数行の script を <c>-Command</c> の 1 つの引数に畳む。
    /// <b>改行はセミコロンに置き換える</b>（引数に改行をそのまま入れると途中で切れる）。
    /// </summary>
    private static string Arguments(string script) =>
        $"-NoProfile -NonInteractive -Command \"{script.ReplaceLineEndings("; ").Replace("\"", "\\\"")}\"";

    /// <summary>PowerShell の単引用符の中に入れる。中の <c>'</c> は 2 つ重ねて逃がす。</summary>
    private static string Quote(string value) => value.Replace("'", "''");
}
