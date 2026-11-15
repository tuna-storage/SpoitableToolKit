using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Shell;

public sealed class SwitchModalTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private static readonly string[] Defaults = ["VideoPlaybackStarted", "WRN"];

    private IPage Page => app.Page;

    private ILocator Field => Page.Locator("#viewSettings .cfg-item[data-key='HiddenWristNotes']");

    private ILocator Modal => Page.Locator("#switchModal");

    private ILocator Switch(string name) => Page.Locator($"#swList .cfg-switch[data-name='{name}'] input.spoitable-switch");

    public async Task InitializeAsync()
    {
        await app.OpenSettings("vr");
        await Assertions.Expect(Field.Locator(".cfg-switch-field")).ToBeVisibleAsync();
    }

    public async Task DisposeAsync()
    {
        await Page.EvaluateAsync("() => { if (!el.switchModal.hidden) closeSwitchModal(); }");
        if (await Page.Locator("#cfgCancel").IsVisibleAsync()) await Page.Locator("#cfgCancel").ClickAsync();
        await SetElsewhereAsync(Defaults);
        await Page.EvaluateAsync("() => loadSettings()");
    }

    private async Task OpenAsync()
    {
        await Field.Locator(".cfg-switch-field button").ClickAsync();
        await Assertions.Expect(Modal).ToBeVisibleAsync();
    }

    private Task<int> SwitchCountAsync() => Page.Locator("#swList .cfg-switch").CountAsync();

    private Task<string> CountTextAsync(int on, int total) =>
        Page.EvaluateAsync<string>($"() => t('host.settings.switches.count', {on}, {total})");

    /// <summary>読み直しの最中だと消える前の行に付くので、印が残っているまで付け直す（設定を開くたび・適用のたびに読み直しが走る）。</summary>
    private async Task TagFieldAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Field.EvaluateAsync("row => row.dataset.before = '1'");
            await Task.Delay(150);
            if (await Field.GetAttributeAsync("data-before") == "1") return;
        }

        throw new InvalidOperationException("設定の画面の読み直しが落ち着かない");
    }

    private Task SetElsewhereAsync(string[] off) => Page.EvaluateAsync(
        "async off => { await bridge.applyConfigChanges(JSON.stringify({ host: { HiddenWristNotes: off } })); stk.receive('configChanged', {}); }", off);

    private async Task ExpectSavedAsync(params string[] expected)
    {
        var file = Path.Combine(app.DataDirectory, "config.json");
        var waited = System.Diagnostics.Stopwatch.StartNew();
        string[]? saved = null;

        while (waited.Elapsed < TimeSpan.FromSeconds(15))
        {
            try
            {
                saved = JsonNode.Parse(File.ReadAllText(file))?["HiddenWristNotes"]?.AsArray().Select(v => (string?)v ?? "").ToArray();
                if (saved is not null && saved.Order().SequenceEqual(expected.Order())) return;
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
            }

            await Task.Delay(100);
        }

        Assert.Fail($"保存された HiddenWristNotes が [{string.Join(", ", saved ?? [])}] のまま（期待 [{string.Join(", ", expected)}]）");
    }

    [Fact]
    public async Task TheFieldCountsWhatIsOnAndTheSwitchesLiveInTheModal()
    {
        await Assertions.Expect(Page.Locator("#viewSettings .cfg-switches")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("#viewSettings .cfg-switch-field")).ToHaveCountAsync(3);
        await Assertions.Expect(Field).Not.ToHaveClassAsync(new Regex(@"\bwide\b"));

        await OpenAsync();
        var total = await SwitchCountAsync();
        Assert.True(total > Defaults.Length, $"スイッチが {total} 個しかない");

        var label = await Page.EvaluateAsync<string>("() => t('host.settings.hiddenWristNotes.label')");
        await Assertions.Expect(Page.Locator("#swTitle")).ToHaveTextAsync(label);
        await Assertions.Expect(Switch("PlayerLeft")).ToBeCheckedAsync();
        await Assertions.Expect(Switch("VideoPlaybackStarted")).Not.ToBeCheckedAsync();

        var count = await CountTextAsync(total - Defaults.Length, total);
        await Assertions.Expect(Page.Locator("#swCount")).ToHaveTextAsync(count);

        // お知らせの「エラー」とアプリの「エラー」は別の段にいる（1 つの格子に混ぜると見分けられない）。
        var sections = await Page.Locator("#swList .sw-sec").EvaluateAllAsync<string[]>(
            "boxes => boxes.map(box => box.dataset.group + ':' + box.querySelector('.fsec-head .h').textContent + ':' + [...box.querySelectorAll('.cfg-switch')].map(row => row.dataset.name).join(','))");
        var heading = await Page.EvaluateAsync<string[]>(
            "() => ['vrc', 'notify', 'app'].map(group => t('host.settings.switches.section.' + group))");
        Assert.Equal(
            [
                $"vrc:{heading[0]}:PlayerJoined,PlayerLeft,WorldJoined,VideoPlaybackStarted,VideoPlaybackFailed,VideoResolved",
                $"notify:{heading[1]}:info,done,warn,error,download,host.vrchat",
                $"app:{heading[2]}:WRN,ERR,CRT",
            ],
            sections);
        await Assertions.Expect(Page.Locator("#swList .cfg-switch .src")).ToHaveCountAsync(0);

        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Modal).ToBeHiddenAsync();

        // サイドバーの方は「画面」のページに居る。
        await app.OpenSettings("display");
        await Page.Locator("#viewSettings .cfg-item[data-key='HiddenSidebarStatus'] .cfg-switch-field button").ClickAsync();
        Assert.Equal(["host", "module"], await Page.Locator("#swList .sw-sec").EvaluateAllAsync<string[]>("boxes => boxes.map(box => box.dataset.group)"));
        await Assertions.Expect(Page.Locator("#swList .sw-sec[data-group='module'] .cfg-switch[data-name='hrs']")).ToHaveCountAsync(1);

        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Modal).ToBeHiddenAsync();
        await app.OpenSettings("vr");
        await Assertions.Expect(Field.Locator(".cnt")).ToHaveTextAsync(count);

        var left = await Page.EvaluateAsync<string>("() => t('host.log.kind.PlayerLeft')");
        await Page.Locator("#cfgSearch").FillAsync(left);
        var wrap = Page.Locator("#viewSettings .cfg-row").Filter(new() { Has = Page.Locator(".cfg-item[data-key='HiddenWristNotes']") });
        await Assertions.Expect(wrap).Not.ToHaveClassAsync(new Regex(@"\bnomatch\b"));
        await Assertions.Expect(Page.Locator("#viewSettings .cfg-row").Filter(new() { Has = Page.Locator(".cfg-item[data-key='HiddenSidebarStatus']") }))
            .ToHaveClassAsync(new Regex(@"\bnomatch\b"));
        await Page.Locator("#cfgSearch").FillAsync("");
    }

    [Fact]
    public async Task AllOnAndAllOffLeaveUnknownNamesAloneAndClosingKeepsNothing()
    {
        await SetElsewhereAsync([.. Defaults, "gone.kind"]);
        await Page.EvaluateAsync("() => loadSettings({ keepScroll: true })");

        await OpenAsync();
        var total = await SwitchCountAsync();

        await Page.Locator("#swList .sw-sec[data-group='vrc'] .sw-off").ClickAsync();
        await Assertions.Expect(Page.Locator("#swList .sw-sec[data-group='vrc'] input.spoitable-switch:checked")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("#swList .sw-sec[data-group='notify'] input.spoitable-switch:not(:checked)")).ToHaveCountAsync(0);
        await Assertions.Expect(Switch("ERR")).ToBeCheckedAsync();
        await Assertions.Expect(Switch("WRN")).Not.ToBeCheckedAsync();

        foreach (var group in new[] { "notify", "app" })
            await Page.Locator($"#swList .sw-sec[data-group='{group}'] .sw-off").ClickAsync();
        await Assertions.Expect(Page.Locator("#swList input.spoitable-switch:checked")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("#swCount")).ToHaveTextAsync(await CountTextAsync(0, total));

        foreach (var group in new[] { "vrc", "notify", "app" })
            await Page.Locator($"#swList .sw-sec[data-group='{group}'] .sw-on").ClickAsync();
        await Assertions.Expect(Page.Locator("#swList input.spoitable-switch:not(:checked)")).ToHaveCountAsync(0);
        await Page.Locator("#swDone").ClickAsync();
        await Assertions.Expect(Modal).ToBeHiddenAsync();
        await Assertions.Expect(Field).ToHaveClassAsync(new Regex(@"\bdirty\b"));
        await Assertions.Expect(Field.Locator(".cnt")).ToHaveTextAsync(await CountTextAsync(total, total));
        await Page.Locator("#cfgCommit").ClickAsync();
        await ExpectSavedAsync("gone.kind");

        await OpenAsync();
        await Page.Locator("#swList .sw-sec[data-group='notify'] .sw-off").ClickAsync();
        await Page.Locator("#swReset").ClickAsync();
        await Assertions.Expect(Page.Locator("#swList input.spoitable-switch:not(:checked)")).ToHaveCountAsync(0);
        await Switch("PlayerLeft").ClickAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Modal).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeHiddenAsync();

        await OpenAsync();
        await Assertions.Expect(Switch("PlayerLeft")).ToBeCheckedAsync();
        await Switch("PlayerLeft").ClickAsync();
        await Page.Mouse.ClickAsync(4, 4);
        await Assertions.Expect(Modal).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeHiddenAsync();
    }

    /// <summary>切ったものは後ろに足すので並びが変わるが、一覧は順序込みで比べる。</summary>
    [Fact]
    public async Task TurningOneOnAndOffAgainIsNoChange()
    {
        await OpenAsync();
        var total = await SwitchCountAsync();

        await Switch("VideoPlaybackStarted").ClickAsync();
        await Assertions.Expect(Page.Locator("#swCount")).ToHaveTextAsync(await CountTextAsync(total - 1, total));
        await Switch("VideoPlaybackStarted").ClickAsync();
        await Assertions.Expect(Switch("VideoPlaybackStarted")).Not.ToBeCheckedAsync();
        await Assertions.Expect(Page.Locator("#swCount")).ToHaveTextAsync(await CountTextAsync(total - 2, total));
        await Page.Locator("#swDone").ClickAsync();

        await Assertions.Expect(Modal).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeHiddenAsync();
        await Assertions.Expect(Field).Not.ToHaveClassAsync(new Regex(@"\bdirty\b"));

        await OpenAsync();
        await Switch("VideoPlaybackStarted").ClickAsync();
        await Page.Locator("#swDone").ClickAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeVisibleAsync();
        await OpenAsync();
        await Switch("VideoPlaybackStarted").ClickAsync();
        await Page.Locator("#swDone").ClickAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeHiddenAsync();
        await Assertions.Expect(Field).Not.ToHaveClassAsync(new Regex(@"\bdirty\b"));
    }

    /// <summary>作り直すと「完了」の溜めが外れた行に付くので、開いている間は作り直さない。</summary>
    [Fact]
    public async Task ChangesFromElsewhereWaitUntilTheModalCloses()
    {
        await TagFieldAsync();
        await OpenAsync();
        var total = await SwitchCountAsync();
        await SetElsewhereAsync(["WRN"]);
        await Switch("PlayerLeft").ClickAsync();
        await Page.Locator("#swDone").ClickAsync();
        await Assertions.Expect(Field).ToHaveClassAsync(new Regex(@"\bdirty\b"));
        await Assertions.Expect(Field).ToHaveAttributeAsync("data-before", "1");
        await Page.Locator("#cfgCancel").ClickAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeHiddenAsync();
        // 作り直しが終わってから印を付け直す（先に付けると消える前の行に付く）。
        await Assertions.Expect(Page.Locator("#viewSettings .cfg-item[data-key='HiddenWristNotes']:not([data-before])")).ToHaveCountAsync(1);

        await TagFieldAsync();
        await OpenAsync();
        await SetElsewhereAsync(["PlayerLeft"]);
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Page.Locator("#viewSettings .cfg-item[data-key='HiddenWristNotes']:not([data-before])")).ToHaveCountAsync(1);
        await Assertions.Expect(Field.Locator(".cnt")).ToHaveTextAsync(await CountTextAsync(total - 1, total));

        await OpenAsync();
        await Switch("PlayerJoined").ClickAsync();
        await Page.EvaluateAsync("() => selectView('log')");
        await Assertions.Expect(Modal).ToBeHiddenAsync();
        await app.OpenSettings("vr");
        await Assertions.Expect(Field.Locator(".cfg-switch-field")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeHiddenAsync();

        // 返事は bridge の口を差し替えて止める。
        await TagFieldAsync();
        await Page.EvaluateAsync(
            """
            () => {
              const real = bridge.getConfigs;
              let release;
              const hold = new Promise(resolve => { release = resolve; });
              bridge.getConfigs = async (...args) => { await hold; return real(...args); };
              window.__e2eRelease = async () => { release(); await window.__e2eLoad; bridge.getConfigs = real; };
              window.__e2eLoad = loadSettings({ keepScroll: true });
            }
            """);
        await OpenAsync();
        await Page.EvaluateAsync("() => window.__e2eRelease()");
        await Assertions.Expect(Field).ToHaveAttributeAsync("data-before", "1");
        await Switch("PlayerLeft").ClickAsync();
        await Page.Locator("#swDone").ClickAsync();
        await Assertions.Expect(Field).ToHaveClassAsync(new Regex(@"\bdirty\b"));
    }
}
