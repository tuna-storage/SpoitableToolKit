using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Vr;

/// <summary>
/// VrLoop がダッシュボードの browser に開かせる URL（<c>index.html?surface=dashboard</c>）を、そのまま本物のシェルに読ませる。
/// ホストの口は押す前に差し替える。本物の <c>closeWindow</c> へ流れるとアプリの窓が閉じて、後の試験が全部落ちる。
/// </summary>
public sealed class VrDashboardShellTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private static readonly string[] WindowCalls = ["minimizeWindow", "toggleMaximizeWindow", "closeWindow", "closeVrWindow", "beginWindowDrag"];

    private IPage Page => app.Page;

    private string _home = "";

    public async Task InitializeAsync()
    {
        _home = Page.Url;
        await Open(new Uri(new Uri(_home), "index.html?surface=dashboard").ToString());
    }

    public async Task DisposeAsync() => await Open(_home);

    private async Task Open(string url)
    {
        await Page.GotoAsync(url);
        await Page.WaitForFunctionAsync("() => window.__stkReady === true", null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
    }

    private Task Spy(params string[] names) => Page.EvaluateAsync(
        """
        names => {
          window.__calls = [];
          for (const name of names) stkHost[name] = (token, ...args) => { window.__calls.push([name, ...args].join(' ')); return false; };
        }
        """,
        names);

    private Task<string[]> Calls() => Page.EvaluateAsync<string[]>("() => window.__calls");

    [Fact]
    public async Task TheDashboardHasNoWindowButtons()
    {
        await Assertions.Expect(Page.Locator("#winMin")).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator("#winMax")).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator("#winClose")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task TheHiddenButtonsDoNothingEvenIfPressed()
    {
        await Spy(WindowCalls);

        await Page.EvaluateAsync("() => { for (const id of ['winMin', 'winMax', 'winClose']) document.getElementById(id).click(); }");

        Assert.Empty(await Calls());
    }

    [Fact]
    public async Task PressingTheTitleBarDoesNotMoveTheDesktopWindow()
    {
        await Spy(WindowCalls);

        // 掴む所（`.grab`）を押す。ボタンの上だと、ダッシュボードかどうかに関係なく掴まないので試験にならない。
        var box = (await Page.Locator("#titlebar .grab").BoundingBoxAsync())!;
        var (x, y) = (box.X + box.Width / 2, box.Y + box.Height / 2);

        for (var i = 0; i < 2; i++)
        {
            await Page.Mouse.MoveAsync((float)x, (float)y);
            await Page.Mouse.DownAsync();
            await Page.Mouse.UpAsync();
        }

        Assert.Empty(await Calls());
    }

    [Fact]
    public async Task TheDashboardIsNotTheVrWindow()
    {
        Assert.False(await Page.EvaluateAsync<bool>("() => document.body.classList.contains('vr-window')"));
        await Assertions.Expect(Page.Locator("#sidebar .item[data-id='dashboard']")).ToBeVisibleAsync();
    }
}
