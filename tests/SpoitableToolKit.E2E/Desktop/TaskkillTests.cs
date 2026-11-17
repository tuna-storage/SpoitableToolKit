using System.Diagnostics;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Desktop;

/// <summary>
/// <c>taskkill</c>（<c>/F</c> なし）で終わる。使い捨ての起動を閉じる手順がこれに頼っている。
/// <c>taskkill</c> はトップレベルの窓のうち重なりのいちばん上の 1 枚にだけ WM_CLOSE を送るので、アプリの見えない窓がいつもそこに居て受ける。
/// どれもアプリが自分で終わるのを待ち（終了コード 0）、次の試験のために起動し直す。
/// </summary>
public sealed class TaskkillTests(AppFixture app) : IClassFixture<AppFixture>
{
    public static readonly TimeSpan ExitLimit = TimeSpan.FromSeconds(60);

    private IPage Page => app.Page;

    private Task SetCloseButtonAsync(string value) =>
        Page.EvaluateAsync($"() => bridge.applyConfigChanges(JSON.stringify({{ host: {{ CloseButton: '{value}' }} }}))");

    private async Task TaskkillAsync()
    {
        using var taskkill = Process.Start(new ProcessStartInfo("taskkill", $"/PID {app.ProcessId}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        await taskkill.WaitForExitAsync();
    }

    /// <summary>窓があるとき。聞く設定でも聞かずに終わる。</summary>
    [Fact]
    public async Task TaskkillEndsTheAppWithItsWindowOpen()
    {
        try
        {
            await SetCloseButtonAsync("Ask");

            await TaskkillAsync();

            Assert.Equal(0, await app.WaitForTheAppToExitAsync(ExitLimit));
            Assert.Contains(app.Output, line => line.Contains("Exiting the app", StringComparison.Ordinal));
        }
        finally
        {
            await app.RestartAsync();
        }
    }

    /// <summary>
    /// 画面の外に置いた窓（タスクバーに出さない）は、WinForms が作った見えない持ち主の窓にぶら下がっている。持ち主が WM_CLOSE で壊されると、窓は閉じる知らせ無しに壊れる。
    /// そのときも終わる（残すと、壊れた窓を持ったまま動き続ける）。taskkill の 1 通はアプリの見えない窓が受けるので、持ち主へ直に送って確かめる。
    /// </summary>
    [Fact]
    public async Task DestroyingTheWindowsHiddenOwnerEndsTheApp()
    {
        try
        {
            var owner = TopWindows.OwnerOf(Assert.Single(TopWindows.Of(app.ProcessId, "SpoitableToolKit")));
            Assert.NotEqual(IntPtr.Zero, owner);

            TopWindows.Close(owner);

            Assert.Equal(0, await app.WaitForTheAppToExitAsync(ExitLimit));
            Assert.Contains(app.Output, line => line.Contains("destroyed without being closed", StringComparison.Ordinal));
        }
        finally
        {
            await app.RestartAsync();
        }
    }

    /// <summary>しまっている間は窓が無いが、見えない窓が受けて終わる（無いと、しまったアプリは taskkill でもログオフでも閉じない）。</summary>
    [Fact]
    public async Task TaskkillEndsTheAppWhileTheWindowIsInTheTray()
    {
        try
        {
            await SetCloseButtonAsync("Tray");
            await Page.Locator("#winClose").ClickAsync();
            await app.WaitUntilTheWindowIsGoneAsync();

            await TaskkillAsync();

            Assert.Equal(0, await app.WaitForTheAppToExitAsync(ExitLimit));
        }
        finally
        {
            await app.RestartAsync();
        }
    }
}
