using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Shell;

/// <summary>動きを減らす設定は試験ごとに決める（Windows で「アニメーション効果」を切っていると既定で減らす側になる）。</summary>
public sealed class ReloadButtonTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private IPage Page => app.Page;

    private ILocator Reload => Page.Locator("#logReload");

    public async Task InitializeAsync()
    {
        await Page.Locator("#sidebar .item[data-id='log']").ClickAsync();
        await Assertions.Expect(Reload).ToBeVisibleAsync();
        await Assertions.Expect(Reload).Not.ToHaveAttributeAsync("aria-busy", "true", new() { Timeout = 10_000 });
    }

    public async Task DisposeAsync()
    {
        await Assertions.Expect(Reload).Not.ToHaveAttributeAsync("aria-busy", "true", new() { Timeout = 10_000 });
        await Page.EvaluateAsync(
            "() => { if (window.__realFill) { window.fillFromStore = window.__realFill; delete window.__realFill; }"
            + " if (window.__realUpdateTiming) { KeyframeEffect.prototype.updateTiming = window.__realUpdateTiming; delete window.__realUpdateTiming; } }");
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Null });
    }

    private Task CountReloadsAsync(int delayMs = 0) => Page.EvaluateAsync(
        "delay => { window.__fills = 0; window.__realFill = fillFromStore;"
        + " window.fillFromStore = async (...args) => { window.__fills++;"
        + " if (delay) await new Promise(done => setTimeout(done, delay)); return window.__realFill(...args); }; }",
        delayMs);

    private Task<int> ReloadsAsync() => Page.EvaluateAsync<int>("() => window.__fills");

    private Task<int> SpinsAsync() => Page.EvaluateAsync<int>("() => el.logReload.querySelector('.ic').getAnimations().length");

    private Task WaitUntilStillAsync() =>
        Assertions.Expect(Reload).Not.ToHaveAttributeAsync("aria-busy", "true", new() { Timeout = 10_000 });

    [Fact]
    public async Task PressingReloadSpinsTheIconForAtLeastOneTurnThenStops()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });

        var pressedAt = await Page.EvaluateAsync<double>("() => performance.now()");
        await Reload.ClickAsync();
        Assert.Equal(1, await SpinsAsync());

        await WaitUntilStillAsync();
        var stoppedAt = await Page.EvaluateAsync<double>("() => performance.now()");

        Assert.True(stoppedAt - pressedAt >= 650, $"stopped after {stoppedAt - pressedAt:0} ms");
        Assert.Equal(0, await SpinsAsync());
        Assert.Equal("none", await Page.EvaluateAsync<string>("() => getComputedStyle(el.logReload.querySelector('.ic')).transform"));
    }

    /// <summary>周の数は機械の重さで変わるので、ページの中で回す数を決める所（updateTiming）を包んで取る。</summary>
    [Fact]
    public async Task ALongReloadKeepsSpinningAndStopsAtTheEndOfATurn()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await CountReloadsAsync(delayMs: 1000);
        await Page.EvaluateAsync(
            "() => { window.__realUpdateTiming = KeyframeEffect.prototype.updateTiming;"
            + " KeyframeEffect.prototype.updateTiming = function (timing) {"
            + " window.__stop = { iterations: timing.iterations, at: this.getComputedTiming().localTime };"
            + " return window.__realUpdateTiming.call(this, timing); }; }");

        var pressedAt = await Page.EvaluateAsync<double>("() => performance.now()");
        await Reload.ClickAsync();
        await WaitUntilStillAsync();
        var stoppedAt = await Page.EvaluateAsync<double>("() => performance.now()");

        var turns = await Page.EvaluateAsync<double>("() => window.__stop.iterations");
        var spunFor = await Page.EvaluateAsync<double>("() => window.__stop.at");

        // 回り始めるのは作った次のフレームから（数えた時間は 1 フレームほど短い）。
        Assert.True(spunFor >= 950, $"decided after {spunFor:0} ms");
        Assert.Equal(Math.Ceiling(spunFor / 700), turns);
        Assert.True(turns >= 2);
        Assert.True(stoppedAt - pressedAt >= turns * 700 - 50, $"{turns} turns, stopped after {stoppedAt - pressedAt:0} ms");
    }

    [Fact]
    public async Task PressingAgainWhileItSpinsDoesNotReloadAgain()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await CountReloadsAsync();

        await Reload.ClickAsync();
        await Reload.ClickAsync();
        await Reload.ClickAsync();
        await WaitUntilStillAsync();
        Assert.Equal(1, await ReloadsAsync());

        await Reload.ClickAsync();
        await WaitUntilStillAsync();
        Assert.Equal(2, await ReloadsAsync());
    }

    [Fact]
    public async Task WithReducedMotionItReloadsWithoutSpinning()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await CountReloadsAsync();

        await Reload.ClickAsync();
        Assert.Equal(0, await SpinsAsync());

        await WaitUntilStillAsync();
        Assert.Equal(1, await ReloadsAsync());
    }

    [Fact]
    public async Task WithReducedMotionPressesDuringAReloadAreStillIgnored()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await CountReloadsAsync(delayMs: 300);

        await Reload.ClickAsync();
        await Assertions.Expect(Reload).ToHaveAttributeAsync("aria-busy", "true");
        await Reload.ClickAsync();

        await WaitUntilStillAsync();
        Assert.Equal(1, await ReloadsAsync());
    }

    [Fact]
    public async Task AButtonDoesNotMoveWhilePressed()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await Reload.HoverAsync();

        await Page.Mouse.DownAsync();
        try
        {
            Assert.True(await Reload.EvaluateAsync<bool>("button => button.matches(':active')"));
            Assert.Equal("none", await Reload.EvaluateAsync<string>("button => getComputedStyle(button).transform"));
        }
        finally
        {
            await Page.Mouse.UpAsync();
        }
    }
}
