using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Settings;

/// <summary>掴むのは本物のマウス（押す・動かす・離す）で通す。モーダルの DnD は自前で、HTML5 の drag は使っていない。</summary>
public sealed class ShortcutModalTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private IPage Page => app.Page;

    private ILocator Field => Page.Locator("#viewSettings .cfg-item[data-key='WristShortcuts']");

    private ILocator Modal => Page.Locator("#shortcutModal");

    private ILocator Placed => Page.Locator("#scPlaced .sc-row");

    public async Task InitializeAsync()
    {
        await app.OpenSettings("vr");
        await Assertions.Expect(Field.Locator(".cfg-shortcuts")).ToBeVisibleAsync();
    }

    public async Task DisposeAsync()
    {
        await Page.EvaluateAsync("() => { if (!el.shortcutModal.hidden) closeShortcutModal(); }");
        if (await Page.Locator("#cfgCancel").IsVisibleAsync()) await Page.Locator("#cfgCancel").ClickAsync();

        var current = await Page.EvaluateAsync<string[]>("async () => (await bridge.getStatus()).wristShortcuts");
        if (!current.SequenceEqual(["hrs", "videourl"]))
        {
            await Page.EvaluateAsync(
                "async () => { await bridge.applyConfigChanges(JSON.stringify({ host: { WristShortcuts: ['hrs', 'videourl'] } })); await loadSettings(); }");
        }
    }

    private Task<string[]> PlacedKeysAsync() =>
        Placed.EvaluateAllAsync<string[]>("rows => rows.map(row => row.dataset.key)");

    private async Task OpenModalAsync()
    {
        await Field.GetByRole(AriaRole.Button).ClickAsync();
        await Assertions.Expect(Modal).ToBeVisibleAsync();
    }

    /// <summary>
    /// 動かしている間に落ちる所の印が出ることも見る（左へ落とすなら線 <c>.sc-drop</c>、左から真ん中へ引き戻すなら真ん中の縁 <c>.sc-drop-out</c>）。
    /// </summary>
    private async Task DragAsync(ILocator grip, ILocator target, bool above = true)
    {
        var from = await grip.BoundingBoxAsync() ?? throw new InvalidOperationException("つまみが見えない");
        var to = await target.BoundingBoxAsync() ?? throw new InvalidOperationException("落とす所が見えない");

        await Page.Mouse.MoveAsync(from.X + from.Width / 2, from.Y + from.Height / 2);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(to.X + to.Width / 2, to.Y + (above ? to.Height * 0.25f : to.Height * 0.75f), new() { Steps = 6 });

        var mark = await Page.EvaluateAsync<string>(
            "() => el.scPlaced.querySelector('.sc-drop') ? 'line' : el.scOfferedCol.classList.contains('sc-drop-out') ? 'out' : 'none'");
        var intoTheLeft = await target.EvaluateAsync<bool>("node => el.scPlacedCol.contains(node)");
        Assert.Equal(intoTheLeft ? "line" : "out", mark);

        await Page.Mouse.UpAsync();
        await Assertions.Expect(Page.Locator(".sc-ghost, #shortcutModal .sc-drop, #scOfferedCol.sc-drop-out")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task TheFieldIsOneButtonAndTheModalShowsWhereEachComesFrom()
    {
        var edit = await Page.EvaluateAsync<string>("() => t('host.settings.shortcuts.edit')");
        var parts = await Field.Locator(".cfg-shortcuts > *").EvaluateAllAsync<string[]>("nodes => nodes.map(node => node.tagName + ':' + node.textContent)");
        Assert.Equal(["BUTTON:" + edit], parts);
        // ボタン 1 つなので、一覧のように横幅いっぱいには出さない。
        await Assertions.Expect(Field).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bwide\b"));

        await OpenModalAsync();

        Assert.Equal(["hrs", "videourl"], await PlacedKeysAsync());
        var sources = await Page.Locator("#scSources .sc-src").EvaluateAllAsync<string[]>("tabs => tabs.map(tab => tab.dataset.source)");
        Assert.Equal("host", sources[0]);
        Assert.Contains("videourl", sources);
        await Assertions.Expect(Page.Locator("#scSources .sc-src[data-source='host']")).ToHaveAttributeAsync("aria-selected", "true");

        var offered = await Page.Locator("#scOffered .sc-row").EvaluateAllAsync<string[]>("rows => rows.map(row => row.dataset.key)");
        Assert.Equal(["log", "settings", "dashboard"], offered);

        await Page.Locator("#scSources .sc-src[data-source='videourl']").ClickAsync();
        await Assertions.Expect(Page.Locator("#scOffered .sc-row[data-key='videourl']")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bplaced\b"));
        await Assertions.Expect(Page.Locator("#scOffered .sc-row[data-key='videourl'] .sc-grip.off")).ToHaveCountAsync(1);
        var runs = await Page.EvaluateAsync<string>("() => t('host.settings.shortcuts.runs')");
        await Assertions.Expect(Page.Locator("#scOffered .sc-row[data-key='videourl/resolve-latest']")).ToContainTextAsync(runs);
    }

    [Fact]
    public async Task DroppingFromTheMiddleAddsItWhereItLandsAndApplyingKeepsIt()
    {
        await OpenModalAsync();

        await DragAsync(Page.Locator("#scOffered .sc-row[data-key='log'] .sc-grip"), Placed.First);
        Assert.Equal(["log", "hrs", "videourl"], await PlacedKeysAsync());

        await Page.Locator("#scDone").ClickAsync();
        await Assertions.Expect(Modal).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeVisibleAsync();

        // 開き直すと、溜めた並び（まだ適用していない）から始まる。Esc で閉じても溜めは残る。
        await OpenModalAsync();
        Assert.Equal(["log", "hrs", "videourl"], await PlacedKeysAsync());
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Modal).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeVisibleAsync();

        await Page.Locator("#cfgCommit").ClickAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeHiddenAsync();
        Assert.Equal(["log", "hrs", "videourl"], await Page.EvaluateAsync<string[]>("async () => (await bridge.getStatus()).wristShortcuts"));
    }

    [Fact]
    public async Task RowsMoveWithinTheLeftComeOffAndRevert()
    {
        await OpenModalAsync();

        // すぐ下の行の上半分へ落としても動かない（掴んでいる行を数えに入れると、ここで 1 つずれる）。
        await DragAsync(Page.Locator("#scPlaced .sc-row[data-key='hrs'] .sc-grip"), Page.Locator("#scPlaced .sc-row[data-key='videourl']"));
        Assert.Equal(["hrs", "videourl"], await PlacedKeysAsync());

        await DragAsync(Page.Locator("#scPlaced .sc-row[data-key='videourl'] .sc-grip"), Placed.First);
        Assert.Equal(["videourl", "hrs"], await PlacedKeysAsync());

        await Page.Locator("#scPlaced .sc-row[data-key='hrs'] .sc-x").ClickAsync();
        Assert.Equal(["videourl"], await PlacedKeysAsync());

        await DragAsync(Page.Locator("#scPlaced .sc-row[data-key='videourl'] .sc-grip"), Page.Locator("#scOfferedCol"));
        Assert.Empty(await PlacedKeysAsync());
        await Assertions.Expect(Page.Locator("#scPlaced .sc-empty")).ToBeVisibleAsync();

        await Page.Locator("#scReset").ClickAsync();
        Assert.Equal(["hrs", "videourl"], await PlacedKeysAsync());

        await DragAsync(Page.Locator("#scOffered .sc-row[data-key='dashboard'] .sc-grip"), Placed.First);
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Modal).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeHiddenAsync();

        // 外（暗い所）を押しても、何も残さずに閉じる。
        await OpenModalAsync();
        Assert.Equal(["hrs", "videourl"], await PlacedKeysAsync());
        await DragAsync(Page.Locator("#scOffered .sc-row[data-key='dashboard'] .sc-grip"), Placed.First);
        await Page.Mouse.ClickAsync(4, 4);
        await Assertions.Expect(Modal).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeHiddenAsync();
    }

    /// <summary>台帳に無い名前（外したモジュールなど）は、黙って消すと入れ直したときに並びが戻らないので、捨てずに「入っていない」と出す。</summary>
    [Fact]
    public async Task EverySixthRowStartsAPageAndUnknownNamesStay()
    {
        var page = await Page.EvaluateAsync<string>("() => t('host.settings.shortcuts.page', 2)");
        var missing = await Page.EvaluateAsync<string>("() => t('host.settings.shortcuts.missing')");

        await Page.EvaluateAsync(
            "() => openShortcutModal(['hrs', 'videourl', 'log', 'settings', 'dashboard', 'videourl/resolve-latest', 'gone/away'], () => {})");
        await Assertions.Expect(Modal).ToBeVisibleAsync();

        var shape = await Page.EvaluateAsync<string[]>(
            "() => [...el.scPlaced.children].map(node => node.classList.contains('sc-page') ? 'page:' + node.textContent : node.dataset.key)");
        Assert.Equal(["hrs", "videourl", "log", "settings", "dashboard", "videourl/resolve-latest", "page:" + page, "gone/away"], shape);
        await Assertions.Expect(Page.Locator("#scPlaced .sc-row[data-key='gone/away']")).ToContainTextAsync(missing);
    }

    /// <summary>開いている間に設定の画面を作り直すと「完了」の溜めが外れた行に付き、「変更」の印が出ないので、作り直さない。</summary>
    [Fact]
    public async Task ChangesFromElsewhereWaitUntilTheModalCloses()
    {
        // 溜めると、今の行（作り直していない）に「変更」の印が付く。
        await Field.EvaluateAsync("row => row.dataset.before = '1'");
        await OpenModalAsync();
        await ApplyElsewhereAsync("['hrs', 'videourl', 'settings']");
        await DragAsync(Page.Locator("#scOffered .sc-row[data-key='log'] .sc-grip"), Placed.First);
        await Page.Locator("#scDone").ClickAsync();
        await Assertions.Expect(Field).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bdirty\b"));
        await Assertions.Expect(Field).ToHaveAttributeAsync("data-before", "1");
        await Page.Locator("#cfgCancel").ClickAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeHiddenAsync();
        // 取り消しの作り直しが終わってから印を付け直す（先に付けると、消える前の行に付く）。
        await Assertions.Expect(Page.Locator("#viewSettings .cfg-item[data-key='WristShortcuts']:not([data-before])")).ToHaveCountAsync(1);

        // 溜めずに閉じたら、閉じてから追いつく（開き直すと、別の所で入れた並び）。
        await Field.EvaluateAsync("row => row.dataset.before = '1'");
        await OpenModalAsync();
        await ApplyElsewhereAsync("['log']");
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Page.Locator("#viewSettings .cfg-item[data-key='WristShortcuts']:not([data-before])")).ToHaveCountAsync(1);
        await OpenModalAsync();
        Assert.Equal(["log"], await PlacedKeysAsync());

        // ほかの画面へ移ったら閉じる（VR の窓で手首のタイルを押したときなど）。設定へ戻っても何も残らない。
        await DragAsync(Page.Locator("#scOffered .sc-row[data-key='dashboard'] .sc-grip"), Placed.First);
        await Page.EvaluateAsync("() => selectView('log')");
        await Assertions.Expect(Modal).ToBeHiddenAsync();
        await app.OpenSettings("vr");
        await Assertions.Expect(Field.Locator(".cfg-shortcuts")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeHiddenAsync();
    }

    /// <summary>別の所（VR の窓の設定など）での適用を作る。この画面のフォームを通さずに当て、configChanged を届ける。</summary>
    private Task ApplyElsewhereAsync(string list) => Page.EvaluateAsync(
        "async () => { await bridge.applyConfigChanges(JSON.stringify({ host: { WristShortcuts: " + list + " } })); stk.receive('configChanged', {}); }");

    [Fact]
    public async Task TheLeftRowsShowWhatTheTilesShow()
    {
        await OpenModalAsync();

        await Page.EvaluateAsync("() => stk.receive('shortcutStatus', [{ key: 'hrs', text: '88 bpm', state: 'warn' }])");

        var value = Page.Locator("#scPlaced .sc-row[data-key='hrs'] .val");
        await Assertions.Expect(value).ToHaveTextAsync("88 bpm");
        await Assertions.Expect(value).ToHaveAttributeAsync("data-state", "warn");
    }
}
