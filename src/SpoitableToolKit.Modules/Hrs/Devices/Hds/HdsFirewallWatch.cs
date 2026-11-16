using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Modules.Hrs.Config;

namespace SpoitableToolKit.Modules.Hrs.Devices.Hds;

/// <summary>
/// HDS の待ち受けがファイアウォールを通れそうかを見る（と、パネルと設定画面の「ルールを追加」）。
/// ファイアウォールは<b>アプリからは足さない</b>。初回の待ち受けで Windows 自身が聞く。ここで見るのは「通れそうか」だけで、
/// 駄目そうならパネルの注意と設定画面の行（<see cref="HdsSettings.Firewall"/>）がボタンを出す。
/// <para>
/// 分かったこと（確認中・結果・直したあと）は、パネル（<c>pushStatus</c>）と設定画面の行（<see cref="SpoitableSettingsAction.Changed"/>）の両方へ流す。
/// </para>
/// </summary>
internal sealed class HdsFirewallWatch(HrsConfig config, Func<int, HdsFirewall.State> check, Func<int, bool> add, Action pushStatus)
{
    private readonly Lock _gate = new();

    private volatile HdsFirewall.State _state;

    /// <summary>見に行った回。<b>後から始めた確認の答えだけを書く</b> — ポートを続けて変えると、前のポートの答えが後から届くことがある。</summary>
    private int _generation;

    /// <summary>足している途中の分。パネルと設定画面から続けて押されても、UAC は 1 回しか出さない（後から押した方は同じ終わりを待つ）。</summary>
    private Task? _adding;

    public HdsFirewall.State State => _state;

    /// <summary>
    /// 見に行く。<b>PowerShell を起こすので裏で</b>（設定画面のスレッドから来るため、
    /// ここで待つと画面ごと固まる）。見ている間は「確認中」で、分かったらパネルと設定画面へ流す。
    /// </summary>
    public void Refresh()
    {
        var generation = Interlocked.Increment(ref _generation);

        if (!config.Hds.Enabled)
        {
            Tell(HdsFirewall.State.Off);
            return;
        }

        var port = config.Hds.Port;
        Tell(HdsFirewall.State.Checking);

        _ = Task.Run(() =>
        {
            var state = check(port);
            if (Volatile.Read(ref _generation) == generation) Tell(state);
        });
    }

    /// <summary>
    /// 見当たらないと言われたルールを足す（ブロックがあれば先に消す）。<b>押した人が居るので UAC が出てよい。</b>
    /// 終わるのは UAC の返事と見直しのあと。設定画面はこれを待ってボタンを押せるに戻す。
    /// </summary>
    public Task AddRuleAsync()
    {
        lock (_gate)
        {
            if (_adding is { IsCompleted: false } running) return running;

            var port = config.Hds.Port;

            // UAC の返事を待つので裏で。
            return _adding = Task.Run(() =>
            {
                var generation = Interlocked.Increment(ref _generation);

                // 直せたと言われても<b>見直す</b>。ブロックが残っていれば通れないままなので、
                // 「足しました」とだけ出すと嘘になる。断られた（UAC で「いいえ」）ときは前の答えのまま。
                if (!add(port))
                {
                    Tell(_state);
                    return;
                }

                var state = check(port);
                if (Volatile.Read(ref _generation) == generation) Tell(state);
            });
        }
    }

    /// <summary>パネルの「ルールを追加」。結果は push で返す（管理者の確認を待つので、その場では分からない）。</summary>
    public Task<string> AddRule()
    {
        _ = AddRuleAsync();
        return Task.FromResult("{}");
    }

    /// <summary>
    /// 設定画面の行の姿。<b>手元の答えを読むだけ</b>（どのスレッドからも呼ばれる）。
    /// 説明はパネルの注意と同じ文で、コピーの 1 行は見当たらないときだけ（ブロックは手で足しても届かないので出さない）。
    /// </summary>
    public SpoitableSettingsActionState Describe(ILocalizer text)
    {
        var port = config.Hds.Port;

        return _state switch
        {
            HdsFirewall.State.Off => new SpoitableSettingsActionState(
                Tone: SpoitableSettingsTone.Neutral,
                Status: text["hrs.settings.hds.firewall.off"],
                Detail: text["hrs.settings.hds.firewall.offDetail"],
                Button: null,
                ButtonNote: null,
                Copy: null),

            HdsFirewall.State.Checking => new SpoitableSettingsActionState(
                Tone: SpoitableSettingsTone.Neutral,
                Status: text["hrs.settings.hds.firewall.checking"],
                Detail: null,
                Button: null,
                ButtonNote: null,
                Copy: null),

            HdsFirewall.State.Present => new SpoitableSettingsActionState(
                Tone: SpoitableSettingsTone.Good,
                Status: text["hrs.settings.hds.firewall.present"],
                Detail: text.Format("hrs.settings.hds.firewall.presentDetail", port),
                Button: null,
                ButtonNote: null,
                Copy: null),

            HdsFirewall.State.Missing => new SpoitableSettingsActionState(
                Tone: SpoitableSettingsTone.Warn,
                Status: text["hrs.settings.hds.firewall.missing"],
                Detail: text.Format("hrs.panel.firewall.missing", port),
                Button: text["hrs.panel.firewall.add"],
                ButtonNote: text["hrs.panel.firewall.uac"],
                Copy: HdsFirewall.CommandFor(port)),

            HdsFirewall.State.Blocked => new SpoitableSettingsActionState(
                Tone: SpoitableSettingsTone.Bad,
                Status: text["hrs.settings.hds.firewall.blocked"],
                Detail: text.Format("hrs.panel.firewall.blocked", port),
                Button: text["hrs.panel.firewall.unblock"],
                ButtonNote: text["hrs.panel.firewall.uac"],
                Copy: null),

            // 読めなかった。届かないと決まったわけではないので、直すボタンは出さずに、手で確かめる先だけ言う。
            _ => new SpoitableSettingsActionState(
                Tone: SpoitableSettingsTone.Neutral,
                Status: text["hrs.settings.hds.firewall.failed"],
                Detail: text.Format("hrs.settings.hds.firewall.failedDetail", port),
                Button: null,
                ButtonNote: null,
                Copy: null),
        };
    }

    private void Tell(HdsFirewall.State state)
    {
        _state = state;
        pushStatus();
        config.Hds.Firewall.Changed();
    }
}
