using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Shell;

/// <summary>モジュールの読み込みは画面より先に走り、push を受け取る側が居ない。getStatus で追いつく道も通る。</summary>
public sealed class BannerTests(AppFixture app) : IClassFixture<AppFixture>
{
    private IPage Page => app.Page;

    private ILocator Banners => Page.Locator("#banners .banner");

    [Fact]
    public async Task AModuleThatFailedToLoadIsAnnouncedInABanner()
    {
        var banner = Banners.Filter(new() { HasTextString = "broken" });

        await Assertions.Expect(banner).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Assertions.Expect(banner.Locator(".body")).ToContainTextAsync("Exception");

        await banner.Locator(".x").ClickAsync();
        await Assertions.Expect(banner).ToHaveCountAsync(0, new() { Timeout = 5_000 });
    }
}
