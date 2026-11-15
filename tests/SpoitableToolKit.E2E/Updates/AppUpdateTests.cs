using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Updates;

public sealed class AppUpdateTests(UpdateFeedAppFixture app) : IClassFixture<UpdateFeedAppFixture>
{
    private IPage Page => app.Page;

    [Fact]
    public async Task ANewerVersionIsOfferedInABannerAboveTheOthers()
    {
        var banner = Page.Locator("#updateBanner");

        await Assertions.Expect(banner).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(banner.Locator(".title")).ToHaveTextAsync($"新しいバージョン {UpdateFeedAppFixture.NewVersion} があります");
        await Assertions.Expect(banner.Locator("#updateInstall")).ToHaveTextAsync("更新");
        await Assertions.Expect(banner.Locator("#updateSkip")).ToHaveTextAsync("スキップ");

        // 読み込めなかったモジュールの帯（AppFixture が置く）より上。
        Assert.True(await Page.EvaluateAsync<bool>("() => document.getElementById('banners').firstElementChild?.id === 'updateBanner'"));
    }

    [Fact]
    public async Task WhatsNewShowsThatVersionsNotesInTheScreensLanguage()
    {
        await Page.Locator("#updateChanges").ClickAsync(new() { Timeout = 15_000 });

        var modal = Page.Locator("#updateNotesModal");
        await Assertions.Expect(modal).ToBeVisibleAsync();
        await Assertions.Expect(modal.Locator("#updateNotesTitle")).ToHaveTextAsync($"{UpdateFeedAppFixture.NewVersion} の変更点");

        var body = modal.Locator("#updateNotesBody");
        await Assertions.Expect(body.Locator("li").First).ToHaveTextAsync(UpdateFeedAppFixture.JapaneseLine);
        await Assertions.Expect(body).Not.ToContainTextAsync(UpdateFeedAppFixture.EnglishLine);

        await Assertions.Expect(body.Locator("li").Nth(1)).ToHaveTextAsync("設定が増えました");
        await Assertions.Expect(body.Locator("strong")).ToHaveCountAsync(0);

        await modal.Locator("#updateNotesClose").ClickAsync();
        await Assertions.Expect(modal).ToBeHiddenAsync();
    }
}

public sealed class AppUpdateSkipTests(UpdateFeedAppFixture app) : IClassFixture<UpdateFeedAppFixture>
{
    [Fact]
    public async Task ASkippedVersionStaysHiddenAfterARestartUntilSomeoneChecksByHand()
    {
        var banner = app.Page.Locator("#updateBanner");
        await Assertions.Expect(banner).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await banner.Locator("#updateSkip").ClickAsync();
        await Assertions.Expect(app.Page.Locator("#updateBanner")).ToHaveCountAsync(0);
        Assert.Equal(UpdateFeedAppFixture.NewVersion, SkippedVersion());

        await app.RestartAsync();

        // 起動のあとの確かめが済んで、同じ版を見つけている。それでも出さない。
        await WaitForPhaseAsync(app.Page, "available");
        await Assertions.Expect(app.Page.Locator("#updateBanner")).ToHaveCountAsync(0);

        await app.OpenSettings("updates");
        await app.Page.Locator("#updateCheck").ClickAsync();
        await Assertions.Expect(app.Page.Locator("#updateBanner")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(app.Page.Locator("#updateRow .desc")).ToHaveTextAsync($"新しいバージョン {UpdateFeedAppFixture.NewVersion} があります");
        Assert.Equal("", SkippedVersion());
    }

    private string? SkippedVersion() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(app.DataDirectory, "config.json")))?["SkippedAppVersion"]?.GetValue<string>();

    internal static async Task WaitForPhaseAsync(IPage page, string phase)
    {
        await page.WaitForFunctionAsync("async phase => (await bridge.getAppUpdate()).phase === phase", phase,
            new PageWaitForFunctionOptions { Timeout = 15_000, PollingInterval = 200 });
    }
}

public sealed class AppUpdateInstallTests(UpdateFeedAppFixture app) : IClassFixture<UpdateFeedAppFixture>
{
    [Fact]
    public async Task UpdatingDownloadsThatVersionAndHandsItOverToInstall()
    {
        var banner = app.Page.Locator("#updateBanner");
        await banner.Locator("#updateInstall").ClickAsync(new() { Timeout = 15_000 });

        await Assertions.Expect(banner.Locator(".body")).ToHaveTextAsync("インストールして再起動しています…",new() { Timeout = 15_000 });
        await Assertions.Expect(banner.Locator("button")).ToHaveCountAsync(0);

        Assert.Equal(UpdateFeedAppFixture.NewVersion, File.ReadAllText(app.AppliedRecord));
        Assert.True(File.Exists(Path.Combine(app.Packages, $"SpoitableToolKit.App-{UpdateFeedAppFixture.NewVersion}-stable-full.nupkg")));
    }
}

public sealed class AppUpdateNotInstalledTests(AppFixture app) : IClassFixture<AppFixture>
{
    [Fact]
    public async Task CheckingInABuildThatIsNotInstalledSaysItOnlyWorksWhenInstalled()
    {
        await app.OpenSettings("updates");
        var row = app.Page.Locator("#updateRow");

        // 版と「更新を確認」は「アプリの更新」のページに居る（ほかのページには無い）。
        await Assertions.Expect(app.Page.Locator(".cfg-group[data-page='host:updates'] #updateRow")).ToBeVisibleAsync();
        await Assertions.Expect(row.Locator(".name")).ToContainTextAsync("バージョン ");
        await Assertions.Expect(app.Page.Locator(".cfg-item[data-key='UpdateChannel']")).ToContainTextAsync("更新のチャンネル");
        await Assertions.Expect(app.Page.Locator(".cfg-item[data-key='CheckAppUpdates']")).ToContainTextAsync("更新を自動で確認する");

        await row.Locator("#updateCheck").ClickAsync();
        await Assertions.Expect(row.Locator(".desc")).ToHaveTextAsync("インストールしたアプリでのみ使用できます");
        Assert.Equal("idle", await app.Page.EvaluateAsync<string>("async () => (await bridge.getAppUpdate()).phase"));
        await Assertions.Expect(app.Page.Locator("#updateBanner")).ToHaveCountAsync(0);
    }
}
