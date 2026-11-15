using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Shell;

/// <summary>モーダルは裏を覆って押させないが、Tab では裏の欄へ入れる。</summary>
public sealed class ModalFocusTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private IPage Page => app.Page;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await Page.EvaluateAsync("""
            () => {
              if (!el.filterModal.hidden) closeFilters();
              if (!el.shortcutModal.hidden) closeShortcutModal();
              if (!el.switchModal.hidden) closeSwitchModal();
              if (!el.licenseModal.hidden) closeLicenses();
            }
            """);
        if (await Page.Locator("#cfgCancel").IsVisibleAsync()) await Page.Locator("#cfgCancel").ClickAsync();
    }

    private async Task<ILocator> OpenAsync(string which)
    {
        switch (which)
        {
            case "filter":
                await Page.Locator("#sidebar .item[data-id='log']").ClickAsync();
                await Page.Locator("#logFilter").ClickAsync();
                break;
            case "shortcut":
                await app.OpenSettings("vr");
                await Page.Locator("#viewSettings .cfg-item[data-key='WristShortcuts']").GetByRole(AriaRole.Button).ClickAsync();
                break;
            case "switch":
                await app.OpenSettings("vr");
                await Page.Locator("#viewSettings .cfg-item[data-key='HiddenWristNotes'] .cfg-switch-field button").ClickAsync();
                break;
            case "license":
                await app.OpenSettings();
                await Page.Locator("#cfgLicenses").ClickAsync();
                break;
        }

        var modal = Page.Locator($"#{which}Modal");
        await Assertions.Expect(modal).ToBeVisibleAsync();
        return modal;
    }

    private Task<string?> OutsideAsync(string which) => Page.EvaluateAsync<string?>("""
        id => {
          const active = document.activeElement;
          if (document.getElementById(id).contains(active)) return null;
          return active ? `${active.tagName.toLowerCase()}#${active.id}.${active.className}` : 'nothing';
        }
        """, which + "Modal");

    [Theory]
    [InlineData("filter")]
    [InlineData("shortcut")]
    [InlineData("switch")]
    [InlineData("license")]
    public async Task TabStaysInsideTheOpenModalBothWays(string which)
    {
        await OpenAsync(which);

        // 1 周より多く回す（中にあるのは多くて 30 ほど）。
        foreach (var key in new[] { "Tab", "Shift+Tab" })
        {
            for (var press = 0; press < 40; press++)
            {
                await Page.Keyboard.PressAsync(key);
                Assert.Null(await OutsideAsync(which));
            }
        }
    }

    [Fact]
    public async Task TabGoesRoundTheModalFromTheLastBackToTheFirst()
    {
        await OpenAsync("switch");

        // 試験の側で数える（製品の数え方を借りない）。
        const string first = """
            () => [...el.switchModal.querySelectorAll('button, input, select, textarea, a[href], [tabindex]')]
              .find(node => !node.disabled && node.tabIndex >= 0 && node.getClientRects().length > 0 && !node.closest('[inert]'))
            """;
        await Page.EvaluateAsync($"() => ({first})().dataset.e2eFirst = '1'");

        await Page.Locator("#swDone").FocusAsync();
        await Page.Keyboard.PressAsync("Tab");
        Assert.True(await Page.EvaluateAsync<bool>("() => document.activeElement.dataset.e2eFirst === '1'"),
            "Tab で最初へ戻っていない: " + await OutsideAsync("switch"));

        await Page.Keyboard.PressAsync("Shift+Tab");
        Assert.Equal("swDone", await Page.EvaluateAsync<string>("() => document.activeElement.id"));
    }

    /// <summary>「完了」を押せなくする作りもありうる。数えてしまうと、焦点を受けられない所へ送ろうとして最初で止まるので、端の後ろに 2 つ足して確かめる。</summary>
    [Fact]
    public async Task TabStillGoesRoundWhenTheEdgeHoldsSomethingYouCannotFocus()
    {
        await OpenAsync("switch");

        await Page.EvaluateAsync("""
            () => {
              const foot = el.switchModal.querySelector('.spoitable-dialog-foot');
              const off = document.createElement('button');
              off.disabled = true;
              off.className = 'e2e-edge';
              const gone = document.createElement('button');
              gone.hidden = true;
              gone.className = 'e2e-edge';
              foot.append(off, gone);
            }
            """);

        try
        {
            await Page.Locator("#swDone").FocusAsync();
            await Page.Keyboard.PressAsync("Tab");
            Assert.Null(await OutsideAsync("switch"));

            await Page.Keyboard.PressAsync("Shift+Tab");
            Assert.Equal("swDone", await Page.EvaluateAsync<string>("() => document.activeElement.id"));
        }
        finally
        {
            await Page.EvaluateAsync("() => el.switchModal.querySelectorAll('.e2e-edge').forEach(node => node.remove())");
        }
    }

    [Fact]
    public async Task EscapeRightAfterOpeningTheFilterCloses()
    {
        var modal = await OpenAsync("filter");
        Assert.Equal("filterSearch", await Page.EvaluateAsync<string>("() => document.activeElement.id"));

        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(modal).ToBeHiddenAsync();
    }

    [Fact]
    public async Task EscapeInTheFilterSearchClearsTheTextBeforeClosing()
    {
        var modal = await OpenAsync("filter");
        var items = Page.Locator("#filters .fitem");
        var all = await items.CountAsync();
        Assert.True(all > 0);

        await Page.Locator("#filterSearch").FillAsync("zzz-no-such-kind");
        await Assertions.Expect(items).ToHaveCountAsync(0);

        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Page.Locator("#filterSearch")).ToHaveValueAsync("");
        await Assertions.Expect(items).ToHaveCountAsync(all);
        await Assertions.Expect(modal).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(modal).ToBeHiddenAsync();
    }

    [Fact]
    public async Task FocusSentBehindComesBackSoEscapeStillCloses()
    {
        var modal = await OpenAsync("shortcut");

        await Page.EvaluateAsync("() => document.getElementById('cfgSearch').focus()");
        Assert.Null(await OutsideAsync("shortcut"));

        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(modal).ToBeHiddenAsync();
    }
}
