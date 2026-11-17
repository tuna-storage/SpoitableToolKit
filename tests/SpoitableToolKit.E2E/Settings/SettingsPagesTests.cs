using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Settings;

/// <summary>
/// 設定の画面の左の一覧（本体のカテゴリ → モジュール）と、モジュールのページの右の目次。
/// ページは画面を離れても覚えているので、どの試験も自分で開くページを選んでから見る。
/// </summary>
public sealed class SettingsPagesTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private static readonly Regex On = new(@"\bon\b");

    private static readonly Regex NoHit = new(@"\bnohit\b");

    private IPage Page => app.Page;

    private ILocator PageOf(string page) =>
        Page.Locator($"#viewSettings .cfg-group[data-page='host:{page}'], #viewSettings .cfg-group[data-page='{page}']");

    private ILocator Item(string key) => Page.Locator($"#viewSettings .cfg-item[data-key='{key}']");

    private Task<string[]> ShownPagesAsync() =>
        Page.Locator("#viewSettings .cfg-group:not([hidden])").EvaluateAllAsync<string[]>(
            "pages => pages.map(page => page.dataset.page + ' ' + page.querySelector('.cfg-head h2').textContent)");

    public async Task InitializeAsync() => await app.OpenSettings();

    public async Task DisposeAsync()
    {
        if (await Page.Locator("#cfgApply").IsVisibleAsync()) await Page.Locator("#cfgCancel").ClickAsync();
        await Page.EvaluateAsync("() => { el.cfgSearch.value = ''; applyCfgSearch(); }");
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Null });
        await Page.EvaluateAsync("() => bridge.applyConfigChanges(JSON.stringify({ host: { WristLayoutLockSeconds: 10 } }))");
        await Page.EvaluateAsync("() => document.querySelectorAll('#toasts .toast .x').forEach(b => b.click())");
    }

    /// <summary>見出しの下にページが 1 つずつ。本体のカテゴリは HostConfig の束ねの順、モジュールはその後ろ。</summary>
    [Fact]
    public async Task TheListHasTheHostCategoriesThenTheModules()
    {
        var rows = await Page.Locator("#cfgNav > *").EvaluateAllAsync<string[]>(
            "nodes => nodes.map(node => node.classList.contains('heading') ? '― ' + node.textContent : node.dataset.page + ' ' + node.querySelector('.name').textContent)");

        Assert.Equal(
            [
                "― 本体",
                "host:display 画面", "host:startup 起動と終了", "host:log ログ", "host:notify 通知", "host:osc OSC", "host:vr VR / 手首",
                "host:modules モジュール管理", "host:updates アプリの更新",
                "― モジュール",
                "hrs 心拍", "videourl 動画 URL",
            ],
            rows);

        // 押せる行は button（Tab で一覧から本文へ行き来できる）。開いているページには aria-current。
        var tags = await Page.Locator("#cfgNav .cfg-nav-item").EvaluateAllAsync<string[]>("rows => [...new Set(rows.map(row => row.tagName))]");
        Assert.Equal(["BUTTON"], tags);
        await Assertions.Expect(Page.Locator("#cfgNav .cfg-nav-item[aria-current='page']")).ToHaveCountAsync(1);
        await Assertions.Expect(app.SettingsPage("display")).ToHaveAttributeAsync("aria-current", "page");
    }

    /// <summary>本体のカテゴリは束ね 1 つなので、見出しはページの名前だけで、右の目次は出さない。</summary>
    [Fact]
    public async Task AHostCategoryShowsOnlyItsOwnItems()
    {
        await app.OpenSettings("log");

        var shown = await Page.Locator("#viewSettings .cfg-group:not([hidden]) .cfg-item").EvaluateAllAsync<string[]>("rows => rows.map(row => row.dataset.key)");
        Assert.Equal(["StoreAllLines", "StoreApiRequests", "AppLogKeepDays"], shown);
        await Assertions.Expect(PageOf("log").Locator(".cfg-head h2")).ToHaveTextAsync("ログ");
        await Assertions.Expect(PageOf("log").Locator(".cfg-sub")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("#cfgToc")).ToBeHiddenAsync();

        // 版と「更新を確認」の行は「アプリの更新」のページにだけ居る。
        await Assertions.Expect(Page.Locator("#updateRow")).ToBeHiddenAsync();
        await app.OpenSettings("updates");
        await Assertions.Expect(PageOf("updates").Locator("#updateRow")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TheModulesAreSplitIntoTheirSubcategories()
    {
        await app.OpenSettings("hrs");
        var hrs = (await PageOf("hrs").Locator(".cfg-sub").AllTextContentsAsync()).ToArray();
        Assert.Equal(["心拍計", "記録", "OSC", "Apple Watch (HDS)", "配信", "ゾーン"], hrs);

        // 束ねごとの囲みに、その束ねの項目（入れ子の中身は入れ物と同じ囲み）。
        var boxes = await PageOf("hrs").Locator(".cfg-box").EvaluateAllAsync<string[]>(
            "boxes => boxes.map(box => [...box.querySelectorAll('.cfg-item:not([data-depth])')].map(row => row.dataset.key).join(','))");
        Assert.Equal(
            ["Device,NoSignalSeconds,ReconnectSeconds", "RecordingEnabled", "OscPreset,SmoothValues,Parameters,VrcoscParameters", "Hds", "Broadcast", "Zones"],
            boxes);

        await app.OpenSettings("videourl");
        var videourl = (await PageOf("videourl").Locator(".cfg-sub").AllTextContentsAsync()).ToArray();
        Assert.Equal(["外部サーバー", "クライアント", "確認"], videourl);
    }

    /// <summary>目次は本文の右。押すとその束ねまで巻き、押した束ねに印が付く（下の端で止まっても、押した束ねのまま）。</summary>
    [Fact]
    public async Task AModulePageHasContentsOnTheRightThatJump()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await app.OpenSettings("hrs");

        var toc = Page.Locator("#cfgToc");
        await Assertions.Expect(toc).ToBeVisibleAsync();
        var links = (await toc.Locator("button").AllTextContentsAsync()).ToArray();
        Assert.Equal(["心拍", "心拍計", "記録", "OSC", "Apple Watch (HDS)", "配信", "ゾーン"], links);

        var (contents, body) = (await toc.BoundingBoxAsync(), await Page.Locator("#cfgScroll").BoundingBoxAsync());
        Assert.True(contents!.X >= body!.X + body.Width, $"目次が本文の右に無い（目次 {contents.X}、本文の右端 {body.X + body.Width}）");

        await Assertions.Expect(toc.Locator("button.on")).ToHaveTextAsync("心拍");

        var hds = toc.Locator("button", new() { HasTextString = "Apple Watch (HDS)" });
        await hds.ClickAsync();
        await Assertions.Expect(hds).ToHaveClassAsync(On);
        var gap = await Page.EvaluateAsync<double>(
            """
            () => {
              const sub = [...document.querySelectorAll('.cfg-group[data-page="hrs"] .cfg-sub')].find(s => s.textContent === 'Apple Watch (HDS)');
              return sub.getBoundingClientRect().top - el.cfgScroll.getBoundingClientRect().top;
            }
            """);
        Assert.InRange(gap, -2, 40);

        // 配信は上の端まで来ない（その前に下の端で止まる）。それでも印は押した束ね（下の束ねへ移らない）。
        var broadcast = toc.Locator("button", new() { HasTextString = "配信" });
        await broadcast.ClickAsync();
        await Assertions.Expect(broadcast).ToHaveClassAsync(On);
        await Assertions.Expect(toc.Locator("button.on")).ToHaveCountAsync(1);
        Assert.True(await Page.EvaluateAsync<bool>("() => el.cfgScroll.scrollTop + el.cfgScroll.clientHeight >= el.cfgScroll.scrollHeight - 2"),
            "配信を押しても下の端まで巻いていない（この試験の前提が崩れている）");

        // 自分で巻くと、印は読んでいる所へ移る。上の端なら先頭、下の端なら最後の束ね。
        await Page.Locator("#cfgScroll").HoverAsync();
        await Page.Mouse.WheelAsync(0, -100_000);
        await Assertions.Expect(toc.Locator("button.on")).ToHaveTextAsync("心拍");
        await Page.Mouse.WheelAsync(0, 100_000);
        await Assertions.Expect(toc.Locator("button.on")).ToHaveTextAsync("ゾーン");
    }

    /// <summary>
    /// 探すのはページをまたいで全部から。当たったページを見出しごと並べ、一覧には当たった数、当たらない行は薄く。
    /// 一覧を押すと探すのをやめて、そのページだけになる。
    /// </summary>
    [Fact]
    public async Task SearchingLooksThroughEveryPageAndCountsHitsInTheList()
    {
        // 目次のあるページから探し始める（探している間は、開いていたページの目次も出さない）。
        await app.OpenSettings("hrs");
        await Assertions.Expect(Page.Locator("#cfgToc")).ToBeVisibleAsync();
        await Page.Locator("#cfgSearch").FillAsync("ポート");

        var found = await ShownPagesAsync();
        Assert.Equal(["host:osc OSC", "hrs 心拍"], found);
        var subs =(await PageOf("hrs").Locator(".cfg-sub:not([hidden])").AllTextContentsAsync()).ToArray();
        Assert.Equal(["記録", "Apple Watch (HDS)", "配信"], subs);

        // 待ち受けポートは切の入れ物の中にあって見えない。入れ物の見出しを出して、そこから開けるようにする（中身の無い囲みにしない）。
        await Assertions.Expect(Item("Hds")).ToBeVisibleAsync();
        await Assertions.Expect(Item("Hds.Port")).ToBeHiddenAsync();
        await Assertions.Expect(Item("Zones")).ToBeHiddenAsync();

        await Assertions.Expect(app.SettingsPage("osc").Locator(".hits")).ToHaveTextAsync("1");
        await Assertions.Expect(app.SettingsPage("hrs").Locator(".hits")).ToHaveTextAsync("4");
        await Assertions.Expect(app.SettingsPage("osc")).Not.ToHaveClassAsync(NoHit);
        await Assertions.Expect(app.SettingsPage("display")).ToHaveClassAsync(NoHit);
        await Assertions.Expect(app.SettingsPage("videourl")).ToHaveClassAsync(NoHit);
        await Assertions.Expect(app.SettingsPage("display").Locator(".hits")).ToHaveTextAsync("");

        // 探している間は右の目次を出さない（ページをまたいで出している）。
        await Assertions.Expect(Page.Locator("#cfgToc")).ToBeHiddenAsync();

        // 当たらなかったページでも押せる。押すと探すのをやめて、そのページだけになる。
        await app.SettingsPage("videourl").ClickAsync();
        await Assertions.Expect(Page.Locator("#cfgSearch")).ToHaveValueAsync("");
        var after = await ShownPagesAsync();
        Assert.Equal(["videourl 動画 URL"], after);
        await Assertions.Expect(app.SettingsPage("videourl")).Not.ToHaveClassAsync(NoHit);
        await Assertions.Expect(app.SettingsPage("hrs").Locator(".hits")).ToHaveTextAsync("");
        await Assertions.Expect(app.SettingsPage("videourl")).ToHaveAttributeAsync("aria-current", "page");
        await Assertions.Expect(Page.Locator("#cfgToc")).ToBeVisibleAsync();
    }

    /// <summary>VR のページの頭に、VR オーバーレイと SteamVR のダッシュボードの 2 つ。既定はどちらも入で、手首の行はその下に今までどおり並ぶ。</summary>
    [Fact]
    public async Task TheVrPageStartsWithTheTwoVrSwitches()
    {
        await app.OpenSettings("vr");

        var head = await Page.Locator("#viewSettings .cfg-group:not([hidden]) .cfg-item").EvaluateAllAsync<string[]>(
            "rows => rows.slice(0, 3).map(row => row.dataset.key)");
        Assert.Equal(["VrOverlay", "VrDashboard", "WristHand"], head);

        await Assertions.Expect(Item("VrOverlay")).ToContainTextAsync("VR オーバーレイを表示する");
        await Assertions.Expect(Item("VrDashboard")).ToContainTextAsync("SteamVR のダッシュボードに表示する");
        await Assertions.Expect(Item("VrOverlay").Locator("input[type=checkbox]")).ToBeCheckedAsync();
        await Assertions.Expect(Item("VrDashboard").Locator("input[type=checkbox]")).ToBeCheckedAsync();
    }

    /// <summary>切り替えて「適用」すると config.json に残る。VR の周はこの値を毎周読むだけなので、残れば次の周から効く（e2e のアプリは VR を動かさないので、効き目は unit で見る）。</summary>
    [Fact]
    public async Task TurningAVrSwitchOffIsSaved()
    {
        await app.OpenSettings("vr");
        var dashboard = Item("VrDashboard").Locator("input[type=checkbox]");

        try
        {
            await Assertions.Expect(dashboard).ToBeCheckedAsync();
            await dashboard.ClickAsync();
            await Page.Locator("#cfgCommit").ClickAsync();
            await Assertions.Expect(Page.Locator("#cfgApply")).ToBeHiddenAsync();

            Assert.True(await SavedAsync("VrDashboard", false), "VrDashboard の切が config.json に残らなかった");
            await Assertions.Expect(dashboard).Not.ToBeCheckedAsync();
            await Assertions.Expect(Item("VrOverlay").Locator("input[type=checkbox]")).ToBeCheckedAsync();
        }
        finally
        {
            // 戻りきるまで待つ（同じクラスのほかの試験は既定の入を見る。投げただけで抜けると、画面と config.json が戻る前に次が読む）。
            await Page.EvaluateAsync("() => bridge.applyConfigChanges(JSON.stringify({ host: { VrDashboard: true } }))");
            Assert.True(await SavedAsync("VrDashboard", true), "VrDashboard が入に戻らなかった");
            await Assertions.Expect(dashboard).ToBeCheckedAsync();
        }
    }

    /// <summary>本体の config.json を読み、<paramref name="key"/> が <paramref name="expected"/> になるまで待つ（書き終わる前に読むと、壊れた JSON か前の値が見える）。</summary>
    private async Task<bool> SavedAsync(string key, bool expected)
    {
        var path = Path.Combine(app.DataDirectory, "config.json");

        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path));

                if (json.RootElement.TryGetProperty(key, out var value)
                    && value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False
                    && value.GetBoolean() == expected)
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
            {
                // 書いている最中。少し待って読み直す。
            }

            await Task.Delay(100);
        }

        return false;
    }

    /// <summary>適用して読み直しても、開いていたページのまま。</summary>
    [Fact]
    public async Task ApplyingKeepsTheOpenPage()
    {
        await app.OpenSettings("vr");
        var seconds = Item("WristLayoutLockSeconds").Locator("input[type=number]");
        await seconds.FillAsync((await seconds.InputValueAsync()) == "11" ? "12" : "11");

        await Page.Locator("#cfgCommit").ClickAsync();

        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeHiddenAsync();
        await Assertions.Expect(app.SettingsPage("vr")).ToHaveAttributeAsync("aria-current", "page");
        await Assertions.Expect(seconds).ToBeVisibleAsync();
        var shown = await ShownPagesAsync();
        Assert.Equal(["host:vr VR / 手首"], shown);
    }
}
