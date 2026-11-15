using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Shell;

/// <summary>VRChat が動いているかは、アプリの見張り（2 秒おき）が印のファイルで見る。このクラスのアプリは VRChat を止めたり動かしたりするので、動いている前提の試験を足さない。</summary>
public sealed class VRChatPresenceTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private IPage Page => app.Page;

    private ILocator HostStatus => Page.Locator("#hostStatus");

    /// <summary>トーストは 3 秒で消えるので、画面ではなく履歴で見る。</summary>
    private Task<int> AnnouncedAsync(string title) => Page.EvaluateAsync<int>(
        "async title => (await bridge.getStatus()).history.filter(h => h.title === title).length", title);

    /// <summary>タイトルバーが変わるのと履歴に載るのは少しずれる。</summary>
    private async Task WaitUntilAnnouncedAsync(string title, int count)
    {
        var seen = 0;
        for (var i = 0; i < 100; i++)
        {
            seen = await AnnouncedAsync(title);
            if (seen == count) return;
            await Task.Delay(100);
        }

        Assert.Fail($"「{title}」の通知が {count} 件にならない（{seen} 件）");
    }

    private ILocator WorldTile => Page.Locator("#dashTiles .spoitable-tile").Filter(new() { HasTextString = "現在のワールド" });

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>止めたままにすると、次の試験が止まった所から始まる。</summary>
    public async Task DisposeAsync()
    {
        app.SetVRChatRunning(true);
        await Assertions.Expect(HostStatus).ToHaveTextAsync("VRChat のログを監視中", new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task ClosingVRChatClearsTheWorldChangesTheTitleAndSaysSo()
    {
        await Page.Locator("#sidebar .item[data-id='dashboard']").ClickAsync();
        app.AppendLog(
            "[Behaviour] Entering Room: E2E Closing World",
            "[Behaviour] Joining wrld_4432ea9b-729c-46e3-8eaf-846aa0a37fdd:12345~private(usr_e2e)~region(jp)");
        await Assertions.Expect(WorldTile).ToContainTextAsync("E2E Closing World", new() { Timeout = 15_000 });
        await Assertions.Expect(HostStatus).ToHaveTextAsync("VRChat のログを監視中");
        var before = await AnnouncedAsync("VRChat が終了しました");

        app.SetVRChatRunning(false);

        await Assertions.Expect(HostStatus).ToHaveTextAsync("VRChat は停止中です", new() { Timeout = 15_000 });
        await Assertions.Expect(WorldTile).Not.ToContainTextAsync("E2E Closing World");
        await WaitUntilAnnouncedAsync("VRChat が終了しました", before + 1);
    }

    [Fact]
    public async Task StartingVRChatBringsTheTitleBackAndSaysSo()
    {
        app.SetVRChatRunning(false);
        await Assertions.Expect(HostStatus).ToHaveTextAsync("VRChat は停止中です", new() { Timeout = 15_000 });
        var before = await AnnouncedAsync("VRChat が起動しました");

        app.SetVRChatRunning(true);

        await Assertions.Expect(HostStatus).ToHaveTextAsync("VRChat のログを監視中", new() { Timeout = 15_000 });
        await WaitUntilAnnouncedAsync("VRChat が起動しました", before + 1);
    }
}
