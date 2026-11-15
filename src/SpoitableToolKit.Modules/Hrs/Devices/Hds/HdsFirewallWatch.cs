using SpoitableToolKit.Modules.Hrs.Config;

namespace SpoitableToolKit.Modules.Hrs.Devices.Hds;

/// <summary>
/// HDS の待ち受けがファイアウォールを通れそうかを見る（と、パネルの「ルールを足す」）。
/// ファイアウォールは<b>アプリからは足さない</b>。初回の待ち受けで Windows 自身が聞く。ここで見るのは「通れそうか」だけで、駄目そうならパネルがボタンを出す。
/// </summary>
internal sealed class HdsFirewallWatch(HrsConfig config, Func<int, HdsFirewall.State> check, Func<int, bool> add, Action pushStatus)
{
    public HdsFirewall.State State { get; private set; }

    /// <summary>
    /// 見に行く。<b>netsh を起こすので裏で</b>（設定画面のスレッドから来るため、
    /// ここで待つと画面ごと固まる）。分かったらパネルへ流す。
    /// </summary>
    public void Refresh()
    {
        if (!config.Hds.Enabled)
        {
            State = HdsFirewall.State.Unknown;
            return;
        }

        var port = config.Hds.Port;

        _ = Task.Run(() =>
        {
            State = check(port);
            pushStatus();
        });
    }

    /// <summary>見当たらないと言われたルールを足す。<b>押した人が居るので UAC が出てよい。</b></summary>
    public Task<string> AddRule()
    {
        var port = config.Hds.Port;

        // UAC の返事を待つので裏で。押した側は結果を push で受け取る。
        _ = Task.Run(() =>
        {
            // 直せたと言われても<b>見直す</b>。ブロックが残っていれば通れないままなので、
            // 「足しました」とだけ出すと嘘になる。
            if (add(port))
                State = check(port);

            pushStatus();
        });

        return Task.FromResult("{}");
    }
}
