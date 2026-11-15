using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Shell;

public sealed class StatusTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private IPage Page => app.Page;

    private ILocator HostStatus => Page.Locator("#hostStatus");

    private ILocator HeartStatus => Page.Locator("#sidebar .item[data-id='hrs'] .st");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Page.Locator("#sidebar .item[data-id='log']").ClickAsync();

    [Fact]
    public async Task TheHostAndItsModulesShareOneBoard()
    {
        await Assertions.Expect(HostStatus).ToContainTextAsync("VRChat のログ", new() { Timeout = 15_000 });

        // 文言は見ない（この機械に心拍計があれば本当に繋がってしまう）。
        await Assertions.Expect(HeartStatus).ToBeVisibleAsync();
        Assert.False(string.IsNullOrWhiteSpace(await HeartStatus.TextContentAsync()), "心拍の行に状態が出ていない");
    }

    [Fact]
    public async Task TheHostStatusIsReadOnly()
    {
        await Assertions.Expect(HostStatus).ToBeVisibleAsync(new() { Timeout = 15_000 });

        Assert.NotEqual("button", await HostStatus.EvaluateAsync<string>("e => e.tagName.toLowerCase()"));
        Assert.Equal("button", await HeartStatus.EvaluateAsync<string>("e => e.closest('.item').tagName.toLowerCase()"));
    }

    /// <summary>押し出しの方が先に届くので、普通に見ているだけでは壊れても気付けない。効くのは読み込み直しと、後から繋いだ面（VR オーバーレイ）。</summary>
    [Fact]
    public async Task WhatIsOnTheBoardCanBeReadFromGetStatus()
    {
        await Assertions.Expect(HostStatus).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var ids = await Page.EvaluateAsync<string[]>("async () => (await bridge.getStatus()).status.map(s => s.id)");

        Assert.Contains("host", ids);
        Assert.Contains("hrs", ids);
    }

    [Fact]
    public async Task ClickingTheHeartRateTileOpensItsPanel()
    {
        await Page.Locator("#sidebar .item[data-id='dashboard']").ClickAsync();

        var panel = Page.Locator("iframe.view[data-id='hrs']");
        await Assertions.Expect(panel).Not.ToBeVisibleAsync();

        await Page.Locator("#dashTiles .spoitable-tile").Filter(new() { HasTextString = "心拍" }).ClickAsync();

        await Assertions.Expect(panel).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task TheDashboardCountsTheWorldAndThePeopleFromTheLog()
    {
        await Page.Locator("#sidebar .item[data-id='dashboard']").ClickAsync();

        app.AppendLog(
            // 入ったことになるのは Joining の行（名前は 1 つ前の Entering Room から持ち越す）。
            "[Behaviour] Entering Room: E2E Dashboard World",
            "[Behaviour] Joining wrld_4432ea9b-729c-46e3-8eaf-846aa0a37fdd:12345~private(usr_e2e)~region(jp)",
            "[Behaviour] OnPlayerJoined e2e-dash-a",
            "[Behaviour] OnPlayerJoined e2e-dash-b",
            "[Behaviour] OnPlayerLeft e2e-dash-a");

        var tiles = Page.Locator("#dashTiles .spoitable-tile");
        await Assertions.Expect(tiles.Filter(new() { HasTextString = "現在のワールド" }))
            .ToContainTextAsync("E2E Dashboard World", new() { Timeout = 15_000 });

        var people = tiles.Filter(new() { HasTextString = "現在の人数" });
        await Assertions.Expect(people.Locator(".spoitable-tile-value")).ToHaveTextAsync("1 人");
        await Assertions.Expect(people.Locator(".spoitable-tile-note")).ToHaveTextAsync("入室 2 / 退出 1");
    }
}
