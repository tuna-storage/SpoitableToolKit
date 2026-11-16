using SpoitableToolKit.Modules.Hrs.Config;
using SpoitableToolKit.Modules.Hrs.Devices.Hds;
using SpoitableToolKit.Tests.Fakes;
using Xunit;

namespace SpoitableToolKit.Tests.Hrs.Devices;

/// <summary>
/// ファイアウォールを見る係。「使っていない」「確認中」「確認できなかった」を言い分け（設定画面の行が出し分ける）、
/// 分かったことはパネルと設定画面の行の両方へ流す。足すのは押されたときだけで、続けて押されても UAC は 1 回。
/// </summary>
public sealed class HdsFirewallWatchTests
{
    private static readonly FakeLocalizer Text = new();

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(30);
    }

    /// <summary>引数で enum を受けると、internal な型が public な試験メソッドに出てしまう。</summary>
    private static HdsFirewall.State Parse(string state) => Enum.Parse<HdsFirewall.State>(state);

    [Fact]
    public async Task OffCheckingAndCouldNotTellAreToldApart()
    {
        var config = new HrsConfig();
        var answer = new TaskCompletionSource<HdsFirewall.State>();
        var pushes = 0;
        var watch = new HdsFirewallWatch(config, _ => answer.Task.Result, _ => true, () => Interlocked.Increment(ref pushes));

        watch.Refresh();
        Assert.Equal(HdsFirewall.State.Off, watch.State);

        config.Hds.Enabled = true;
        watch.Refresh();
        Assert.Equal(HdsFirewall.State.Checking, watch.State);

        answer.SetResult(HdsFirewall.State.Failed);
        await WaitFor(() => watch.State == HdsFirewall.State.Failed && Volatile.Read(ref pushes) == 3);

        Assert.Equal(HdsFirewall.State.Failed, watch.State);
        Assert.Equal(3, Volatile.Read(ref pushes));
    }

    /// <summary>ポートを続けて変えると、前のポートの答えが後から届くことがある。後から始めた確認の答えが残る。</summary>
    [Fact]
    public async Task TheLaterCheckWinsWhenThePortChangesTwice()
    {
        var config = new HrsConfig();
        config.Hds.Enabled = true;
        var slow = new TaskCompletionSource<HdsFirewall.State>();
        var watch = new HdsFirewallWatch(config, port => port == 1000 ? slow.Task.Result : HdsFirewall.State.Present, _ => true, () => { });

        config.Hds.Port = 1000;
        watch.Refresh();
        config.Hds.Port = 2000;
        watch.Refresh();
        await WaitFor(() => watch.State == HdsFirewall.State.Present);

        slow.SetResult(HdsFirewall.State.Missing);
        await Task.Delay(200);

        Assert.Equal(HdsFirewall.State.Present, watch.State);
    }

    /// <summary>パネルと設定画面から続けて押されても、足しに行くのは 1 回（UAC は 1 回）。後から押した方は同じ終わりを待つ。</summary>
    [Fact]
    public async Task PressingFromThePanelAndTheSettingsAtOnceAddsOnce()
    {
        var config = new HrsConfig();
        config.Hds.Enabled = true;
        var adds = 0;
        var answer = new TaskCompletionSource<bool>();
        var seen = HdsFirewall.State.Missing;
        var watch = new HdsFirewallWatch(config, _ => seen, _ =>
        {
            Interlocked.Increment(ref adds);
            var added = answer.Task.Result;
            seen = HdsFirewall.State.Present;
            return added;
        }, () => { });

        var fromSettings = watch.AddRuleAsync();
        await watch.AddRule();
        var again = watch.AddRuleAsync();
        Assert.False(again.IsCompleted);

        answer.SetResult(true);
        await fromSettings;
        await again;

        Assert.Equal(1, Volatile.Read(ref adds));
        Assert.Equal(HdsFirewall.State.Present, watch.State);
    }

    /// <summary>
    /// UAC の返事を待つ間（数分ありうる）に設定を適用し直して見直しが走っても、足し終わったあとの見直しが残る。
    /// 押した時点の番号で見直すと、待つ間の見直しに番号を追い越されて答えが捨てられ、足したのに「見当たりません」のままになる。
    /// </summary>
    [Fact]
    public async Task ARecheckWhileThePromptIsOpenDoesNotHideTheFix()
    {
        var config = new HrsConfig();
        config.Hds.Enabled = true;
        var rule = false;
        var prompted = new TaskCompletionSource();
        var answer = new TaskCompletionSource<bool>();
        var watch = new HdsFirewallWatch(config, _ => Volatile.Read(ref rule) ? HdsFirewall.State.Present : HdsFirewall.State.Missing, _ =>
        {
            prompted.SetResult();
            var added = answer.Task.Result;
            Volatile.Write(ref rule, added);
            return added;
        }, () => { });

        watch.Refresh();
        await WaitFor(() => watch.State == HdsFirewall.State.Missing);

        // UAC が出ている間に、設定を適用し直した（見直しが走って、まだ見当たらないと答える）。
        var adding = watch.AddRuleAsync();
        await prompted.Task;
        watch.Refresh();
        await WaitFor(() => watch.State == HdsFirewall.State.Missing);
        Assert.False(adding.IsCompleted);

        answer.SetResult(true);
        await adding;

        Assert.Equal(HdsFirewall.State.Present, watch.State);
    }

    /// <summary>待つ間にポートを変えていたら、足し終わったあとの見直しはいまのポートで見る（押した時点のポートの答えで上書きしない）。</summary>
    [Fact]
    public async Task TheRecheckAfterAnAddLooksAtThePortAppliedMeanwhile()
    {
        var config = new HrsConfig();
        config.Hds.Enabled = true;
        config.Hds.Port = 1000;
        var allowed = new System.Collections.Concurrent.ConcurrentDictionary<int, bool>();
        var answer = new TaskCompletionSource<bool>();
        var watch = new HdsFirewallWatch(config, port => allowed.ContainsKey(port) ? HdsFirewall.State.Present : HdsFirewall.State.Missing, port =>
        {
            var added = answer.Task.Result;
            allowed[port] = true;
            return added;
        }, () => { });

        watch.Refresh();
        await WaitFor(() => watch.State == HdsFirewall.State.Missing);

        var adding = watch.AddRuleAsync();
        config.Hds.Port = 2000;
        watch.Refresh();
        await WaitFor(() => watch.State == HdsFirewall.State.Missing);

        answer.SetResult(true);
        await adding;

        Assert.True(allowed.ContainsKey(1000));
        Assert.Equal(HdsFirewall.State.Missing, watch.State);
    }

    /// <summary>UAC で断られたら、前の答えのまま（見直さない）。</summary>
    [Fact]
    public async Task ADeclinedAddLeavesTheAnswerAsItWas()
    {
        var config = new HrsConfig();
        config.Hds.Enabled = true;
        var checks = 0;
        var watch = new HdsFirewallWatch(config, _ =>
        {
            Interlocked.Increment(ref checks);
            return HdsFirewall.State.Blocked;
        }, _ => false, () => { });

        watch.Refresh();
        await WaitFor(() => watch.State == HdsFirewall.State.Blocked);

        await watch.AddRuleAsync();

        Assert.Equal(HdsFirewall.State.Blocked, watch.State);
        Assert.Equal(1, Volatile.Read(ref checks));
    }

    /// <summary>
    /// 設定画面の行の姿。説明はパネルの注意と同じ文（ポート入り）で、ボタンは見当たらない・ブロックのときだけ、
    /// コピーの 1 行は見当たらないときだけ（ブロックは手で足しても届かない）。
    /// </summary>
    [Theory]
    [InlineData("Off", "Neutral", "hrs.settings.hds.firewall.off", "hrs.settings.hds.firewall.offDetail", null, false)]
    [InlineData("Checking", "Neutral", "hrs.settings.hds.firewall.checking", null, null, false)]
    [InlineData("Present", "Good", "hrs.settings.hds.firewall.present", "hrs.settings.hds.firewall.presentDetail", null, false)]
    [InlineData("Missing", "Warn", "hrs.settings.hds.firewall.missing", "hrs.panel.firewall.missing", "hrs.panel.firewall.add", true)]
    [InlineData("Blocked", "Bad", "hrs.settings.hds.firewall.blocked", "hrs.panel.firewall.blocked", "hrs.panel.firewall.unblock", false)]
    [InlineData("Failed", "Neutral", "hrs.settings.hds.firewall.failed", "hrs.settings.hds.firewall.failedDetail", null, false)]
    public async Task TheRowSaysWhatTheFirewallSays(string state, string tone, string status, string? detail, string? button, bool copies)
    {
        var config = new HrsConfig();
        config.Hds.Enabled = state != "Off";
        config.Hds.Port = 4100;
        var hold = new TaskCompletionSource<HdsFirewall.State>();
        var watch = new HdsFirewallWatch(config, _ => state == "Checking" ? hold.Task.Result : Parse(state), _ => true, () => { });

        watch.Refresh();
        await WaitFor(() => watch.State == Parse(state));

        var row = watch.Describe(Text);
        hold.TrySetResult(HdsFirewall.State.Failed);

        Assert.Equal(tone, row.Tone.ToString());
        Assert.Equal(status, row.Status);
        Assert.Equal(button, row.Button);
        Assert.Equal(button is null ? null : "hrs.panel.firewall.uac", row.ButtonNote);

        // FakeLocalizer は材料を後ろに並べる。ポートが入る文はそのポートで引いている。
        if (detail is null) Assert.Null(row.Detail);
        else Assert.StartsWith(detail, row.Detail);
        if (row.Detail?.Contains('(') is true) Assert.EndsWith("(4100)", row.Detail);

        if (copies) Assert.Equal(HdsFirewall.CommandFor(4100), row.Copy);
        else Assert.Null(row.Copy);
    }

    /// <summary>e2e のファイアウォール（ファイル）。読めない中身は「確認できなかった」で、足すと present を書き、押された回数を残す。</summary>
    [Fact]
    public void TheE2eFirewallIsAFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "stk-firewall-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = new HdsFirewallFile(path);
            Assert.Equal(HdsFirewall.State.Failed, file.Check());

            File.WriteAllText(path, "blocked\r\n");
            Assert.Equal(HdsFirewall.State.Blocked, file.Check());

            Assert.True(file.Add(4100));
            Assert.Equal(HdsFirewall.State.Present, file.Check());
            Assert.Equal(["4100"], File.ReadAllLines(path + ".added"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".added");
        }
    }
}
