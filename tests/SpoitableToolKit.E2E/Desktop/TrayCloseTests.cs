using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Desktop;

/// <summary>
/// 閉じるボタンとトレイ。しまうと窓は閉じて破棄され（CDP のページが消える）、2 つ目の起動の「前に出て」で作り直される（トレイのアイコンは Playwright から押せないので、この道で開く）。
/// どの試験も、しまったら開き直して終わる（次の試験がページを使う）。閉じるボタンの設定は、試験ごとに最初に決める。
/// </summary>
public sealed class TrayCloseTests(AppFixture app) : IClassFixture<AppFixture>
{
    private IPage Page => app.Page;

    private ILocator Modal => Page.Locator("#closeModal");

    private Task SetCloseButtonAsync(string value) =>
        Page.EvaluateAsync($"() => bridge.applyConfigChanges(JSON.stringify({{ host: {{ CloseButton: '{value}' }} }}))");

    private string? Saved(string key) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(app.DataDirectory, "config.json")))?[key]?.GetValue<string>();

    private void AssertTheAppIsRunning()
    {
        using var process = Process.GetProcessById(app.ProcessId);
        Assert.False(process.HasExited);
    }

    /// <summary>
    /// 初めて × を押すと、しまうか終了するかを聞く（あとで設定から変えられる、と添えて）。「トレイにしまう」を選ぶと、答えが設定に入り、窓はしまわれる（アプリは動いたまま）。
    /// 開き直した窓の設定の画面にも、その答えが出ている。
    /// </summary>
    [Fact]
    public async Task TheFirstCloseAsksAndTheTrayAnswerIsRemembered()
    {
        await SetCloseButtonAsync("Ask");

        await Page.Locator("#winClose").ClickAsync();

        await Assertions.Expect(Modal).ToBeVisibleAsync();
        await Assertions.Expect(Modal.Locator("#closeTitle")).ToHaveTextAsync("タスクトレイに最小化しますか?");
        await Assertions.Expect(Modal).ToContainTextAsync("設定画面から動作を変更できます");
        await Assertions.Expect(Page.Locator("#closeTray")).ToBeFocusedAsync();

        await Page.Locator("#closeTray").ClickAsync();
        await app.WaitUntilTheWindowIsGoneAsync();

        Assert.Equal("Tray", Saved("CloseButton"));
        AssertTheAppIsRunning();

        await app.OpenTheWindowFromTheTrayAsync();
        await app.OpenSettings("startup");
        await Assertions.Expect(Page.Locator(".cfg-item[data-key='CloseButton'] select")).ToHaveValueAsync("Tray");
        Assert.Contains(app.Output, line => line.Contains("opening the window from the tray", StringComparison.Ordinal));
    }

    /// <summary>しまう設定なら聞かずにしまう。開き直せる（作り直した窓で、画面がまた動く）。</summary>
    [Fact]
    public async Task WithTheTraySetTheCloseButtonPutsTheWindowAwayWithoutAsking()
    {
        await SetCloseButtonAsync("Tray");

        await Page.Locator("#winClose").ClickAsync();
        await app.WaitUntilTheWindowIsGoneAsync();

        AssertTheAppIsRunning();
        Assert.Contains(app.Output, line => line.Contains("The window is in the tray", StringComparison.Ordinal));

        await app.OpenTheWindowFromTheTrayAsync();
        Assert.True(await Page.EvaluateAsync<bool>("() => window.__stkReady === true"));
        await Assertions.Expect(Modal).ToBeHiddenAsync();
    }

    /// <summary>
    /// 聞きたいのに、シェルのページが答えられない（読み込み中・起動の途中）ときは、聞かずにしまう。設定は聞くのまま（次に閉じるときにまた聞く）。
    /// 答えられないページは、askClose を false を返すもの（起動の途中のページと同じ答え）に差し替えて作る。
    /// しまうときに自分で閉じる道が、閉じかけの知らせでもう一度聞きに行かないこと（聞きに行くと、しまえないまま聞き直しが続く）もここで見る。
    /// </summary>
    [Fact]
    public async Task WhenTheScreenCannotAskTheWindowGoesToTheTrayAndAsksNextTime()
    {
        await SetCloseButtonAsync("Ask");
        await Page.EvaluateAsync("() => { window.stk.askClose = () => false; }");

        await Page.Locator("#winClose").ClickAsync();
        await app.WaitUntilTheWindowIsGoneAsync();

        AssertTheAppIsRunning();
        Assert.Equal("Ask", Saved("CloseButton") ?? "Ask");
        Assert.Contains(app.Output, line => line.Contains("Could not ask how to close the window", StringComparison.Ordinal));

        await app.OpenTheWindowFromTheTrayAsync();
        await Page.Locator("#winClose").ClickAsync();
        await Assertions.Expect(Modal).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Modal).ToBeHiddenAsync();
    }

    /// <summary>
    /// Alt+F4 とタスクバーの「ウィンドウを閉じる」（システムメニューの「閉じる」）も × と同じで、閉じるのをいったん止めて聞く。
    /// Esc・キャンセル・外を押したら何もしない（窓は開いたまま、設定は聞くのまま、次にまた聞く）。
    /// </summary>
    [Fact]
    public async Task TheSystemCloseAsksTheSameWayAndBackingOutKeepsTheWindow()
    {
        await SetCloseButtonAsync("Ask");
        var window = Assert.Single(TopWindows.Of(app.ProcessId, "SpoitableToolKit"));

        TopWindows.SystemClose(window);
        await Assertions.Expect(Modal).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Modal).ToBeHiddenAsync();

        TopWindows.SystemClose(window);
        await Assertions.Expect(Modal).ToBeVisibleAsync();
        await Page.Locator("#closeCancel").ClickAsync();
        await Assertions.Expect(Modal).ToBeHiddenAsync();

        await Page.Locator("#winClose").ClickAsync();
        await Assertions.Expect(Modal).ToBeVisibleAsync();
        await Page.Mouse.ClickAsync(5, 5);
        await Assertions.Expect(Modal).ToBeHiddenAsync();

        Assert.Equal(1, await app.PageTargetsAsync());

        // 聞くのままなら、既定と同じなので config.json には書かれていないこともある。
        Assert.Equal("Ask", Saved("CloseButton") ?? "Ask");
    }

    /// <summary>
    /// 「起動と終了」のページ。閉じるボタンの動きとしまって始めるは出るが、Windows の起動時の起動は出ない
    /// （開発の bin は登録できない形。押しても登録しないので、押せる行を見せない）。
    /// </summary>
    [Fact]
    public async Task TheStartupPageShowsWhatThisBuildCanDo()
    {
        await app.OpenSettings("startup");

        var shown = await Page.Locator("#viewSettings .cfg-group[data-page='host:startup'] .cfg-item").EvaluateAllAsync<string[]>("rows => rows.map(row => row.dataset.key)");
        Assert.Equal(["CloseButton", "StartInTray"], shown);
    }

    /// <summary>e2e のアプリはトレイにアイコンを出さない（8 つ同時に立てるので、出すと手元のトレイに並ぶ）。</summary>
    [Fact]
    public void NoTrayIconIsShownForAnAppKeptOffScreen()
    {
        Assert.Contains(app.Output, line => line.Contains("SPOITABLE_UI_OFFSCREEN is set; no tray icon", StringComparison.Ordinal));
    }
}
