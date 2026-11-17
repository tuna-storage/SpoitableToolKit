using Xunit;

namespace SpoitableToolKit.E2E.Desktop;

/// <summary>しまった状態で始まるアプリ（Windows の起動時の起動の <c>--tray</c>）。窓は 2 つ目の起動（トレイのアイコンと同じ道）で初めて作る。</summary>
public sealed class StartInTrayTests(TrayStartAppFixture app) : IClassFixture<TrayStartAppFixture>
{
    /// <summary>始まりきった時点で窓（ページ）は 1 つも無い。2 つ目の起動に頼まれて作った窓は、ふつうに動く。</summary>
    [Fact]
    public async Task TheAppStartsWithNoWindowAndOpensOneWhenAsked()
    {
        Assert.Equal(0, app.PagesWhileInTray);

        Assert.True(await app.Page.EvaluateAsync<bool>("() => window.__stkReady === true"));
        Assert.Equal(1, await app.PageTargetsAsync());
        Assert.Contains(app.Output, line => line.Contains("opening the window from the tray", StringComparison.Ordinal));
    }

    /// <summary>自動で起動した回に窓から「再起動」しても、しまって始める印は引き継がない（窓が消えてトレイに隠れたまま戻ってこない）。</summary>
    [Fact]
    public async Task TheRestartDoesNotCarryTheTrayArgument()
    {
        if (File.Exists(app.RestartRecord)) File.Delete(app.RestartRecord);

        Assert.True(await app.Page.EvaluateAsync<bool>("() => bridge.restartApp()"));

        var passed = File.ReadAllLines(app.RestartRecord);
        Assert.DoesNotContain("--tray", passed);
        Assert.Contains("--after-pid", passed);
    }
}
