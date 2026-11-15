using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Shell;

public sealed class LicenseScreenTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private IPage Page => app.Page;

    private ILocator Modal => Page.Locator("#licenseModal");

    private ILocator Text => Page.Locator("#licText");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await Page.EvaluateAsync("() => { if (!el.licenseModal.hidden) closeLicenses(); }");

    private async Task OpenAsync()
    {
        await app.OpenSettings();
        await Page.Locator("#cfgLicenses").ClickAsync();
        await Assertions.Expect(Modal).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TheScreenShowsTheAppsMitLicenseAndTheThirdPartyList()
    {
        await OpenAsync();

        await Assertions.Expect(Text).ToContainTextAsync("MIT License");
        await Assertions.Expect(Text).ToContainTextAsync("Copyright (c) 2026 fltuna");
        await Assertions.Expect(Text).ToContainTextAsync("Permission is hereby granted, free of charge");

        await Page.Locator("#licTabs [data-tab='thirdParty']").ClickAsync();
        await Assertions.Expect(Page.Locator("#licTabs [data-tab='thirdParty']")).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(Text).ToContainTextAsync("Third-party notices");
        await Assertions.Expect(Text).ToContainTextAsync("- NuGet: CefSharp.Common.NETCore");
        await Assertions.Expect(Text).ToContainTextAsync("Copyright © The CefSharp Authors");
        await Assertions.Expect(Text).ToContainTextAsync("Apache License");
        await Assertions.Expect(Text).Not.ToContainTextAsync("Copyright (c) 2026 fltuna");

        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Modal).ToBeHiddenAsync();
    }

    [Fact]
    public async Task TheTextIsTheFileTheAppShips()
    {
        await OpenAsync();
        await Page.Locator("#licTabs [data-tab='thirdParty']").ClickAsync();
        await Assertions.Expect(Text).ToContainTextAsync("Third-party notices");

        var shown = await Text.TextContentAsync();
        var shipped = File.ReadAllText(Path.Combine(AppFixture.HostDirectory, "THIRD-PARTY-NOTICES.md"));
        Assert.Equal(shipped.ReplaceLineEndings("\n"), shown!.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task TheChromiumLicensesOpenOnceInTheirOwnWindow()
    {
        await OpenAsync();
        await Assertions.Expect(Page.Locator("#licChromiumHint")).ToBeHiddenAsync();

        await Page.Locator("#licChromium").ClickAsync();
        await WaitForCreditsAsync();

        // 繋いだあとに立った browser のページは、繋いだままの Playwright には出てこない。繋ぎ直して中を読む。
        using (var playwright = await Playwright.CreateAsync())
        {
            var browser = await playwright.Chromium.ConnectOverCDPAsync(app.CdpEndpoint);
            try
            {
                var credits = browser.Contexts.SelectMany(context => context.Pages).Single(page => page.Url.StartsWith(Credits, StringComparison.Ordinal));
                var products = await credits.EvaluateAsync<int>("() => document.querySelectorAll('.product .license').length");
                Assert.True(products > 100, $"chrome://credits の部品が {products} 個しか無い");
            }
            finally
            {
                await browser.CloseAsync();
            }
        }

        await Page.Locator("#licChromium").ClickAsync();
        await Task.Delay(1_000);
        Assert.Single(await CreditsTargetsAsync());
    }

    /// <summary><c>surface=window</c> は空間の窓（字は 21px の 0.8）、<c>surface=dashboard</c> は SteamVR のダッシュボード。</summary>
    [Theory]
    [InlineData("window", 16)]
    [InlineData("dashboard", 0)]
    public async Task InVrTheTextWrapsAndScrollsDownOnly(string surface, double smallestFont)
    {
        var home = Page.Url;
        try
        {
            await Page.GotoAsync(new Uri(new Uri(home), $"index.html?surface={surface}").ToString());
            await Page.WaitForFunctionAsync("() => window.__stkReady === true", null, new PageWaitForFunctionOptions { Timeout = 30_000 });

            await Page.Locator("#sidebar .item[data-id='settings']").ClickAsync();
            await Page.Locator("#cfgLicenses").ClickAsync();
            await Assertions.Expect(Modal).ToBeVisibleAsync();
            await Assertions.Expect(Page.Locator("#licChromiumHint")).ToBeVisibleAsync();

            await Page.Locator("#licTabs [data-tab='thirdParty']").ClickAsync();
            await Assertions.Expect(Text).ToContainTextAsync("Third-party notices");

            var box = await Page.EvaluateAsync<double[]>("""
                () => {
                  const body = el.licBody;
                  return [parseFloat(getComputedStyle(el.licText).fontSize), body.scrollHeight - body.clientHeight, body.scrollWidth - body.clientWidth];
                }
                """);
            Assert.True(box[0] >= smallestFont, $"字が {box[0]}px");
            Assert.True(box[1] > 0, "縦に巻けない（全部が見えているはずがない長さ）");
            Assert.True(box[2] <= 0, $"横に {box[2]}px はみ出す");
        }
        finally
        {
            await Page.GotoAsync(home);
            await Page.WaitForFunctionAsync("() => window.__stkReady === true", null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        }
    }

    private const string Credits = "chrome://credits/";

    private async Task<string[]> CreditsTargetsAsync()
    {
        var session = await Page.Context.Browser!.NewBrowserCDPSessionAsync();
        try
        {
            var targets = await session.SendAsync("Target.getTargets");
            return
            [
                .. targets!.Value.GetProperty("targetInfos").EnumerateArray()
                    .Where(target => target.GetProperty("type").GetString() == "page")
                    .Where(target => target.GetProperty("url").GetString() == Credits && target.GetProperty("title").GetString() == "Credits")
                    .Select(target => target.GetProperty("targetId").GetString()!),
            ];
        }
        finally
        {
            await session.DetachAsync();
        }
    }

    private async Task WaitForCreditsAsync()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if ((await CreditsTargetsAsync()).Length > 0) return;
            await Task.Delay(100);
        }

        throw new TimeoutException("chrome://credits の窓が 10 秒現れなかった。最後の出力:\n" + string.Join("\n", app.Output.TakeLast(15)));
    }
}
