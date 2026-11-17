using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Desktop;

/// <summary>
/// アプリが終わる道（問いの答え・タスクマネージャー）。どれもアプリが自分で終わるのを待ち（終了コード 0 = Program の最後まで畳んだ）、次の試験のために起動し直す。
/// <c>taskkill</c> の道は <see cref="TaskkillTests"/>（起動し直しが重なるので、クラスを分けて並べて走らせる）。
/// </summary>
public sealed class AppExitTests(AppFixture app) : IClassFixture<AppFixture>
{
    private IPage Page => app.Page;

    private Task SetCloseButtonAsync(string value) =>
        Page.EvaluateAsync($"() => bridge.applyConfigChanges(JSON.stringify({{ host: {{ CloseButton: '{value}' }} }}))");

    private string? Saved(string key) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(app.DataDirectory, "config.json")))?[key]?.GetValue<string>();

    /// <summary>問いで「終了する」を選ぶと、答えが設定に入ってから、アプリが終わる。</summary>
    [Fact]
    public async Task AnsweringExitRemembersItAndEndsTheApp()
    {
        try
        {
            await SetCloseButtonAsync("Ask");
            await Page.Locator("#winClose").ClickAsync();
            await Page.Locator("#closeExit").ClickAsync();

            Assert.Equal(0, await app.WaitForTheAppToExitAsync(TaskkillTests.ExitLimit));
            Assert.Equal("Exit", Saved("CloseButton"));
            Assert.Contains(app.Output, line => line.Contains("Exiting the app", StringComparison.Ordinal));
        }
        finally
        {
            await app.RestartAsync();
        }
    }

    /// <summary>タスクマネージャーの「タスクの終了」（窓への WM_CLOSE）は、聞く設定でも聞かずに終わる（終わらせたい人がしていること）。</summary>
    [Fact]
    public async Task TheTaskManagerEndsTheAppWithoutAsking()
    {
        try
        {
            await SetCloseButtonAsync("Ask");

            TopWindows.Close(Assert.Single(TopWindows.Of(app.ProcessId, "SpoitableToolKit")));

            Assert.Equal(0, await app.WaitForTheAppToExitAsync(TaskkillTests.ExitLimit));
            Assert.Contains(app.Output, line => line.Contains("(TaskManagerClosing): Exit", StringComparison.Ordinal));
        }
        finally
        {
            await app.RestartAsync();
        }
    }
}
