using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Desktop;

/// <summary>
/// Windows の起動時の起動。設定が正で、登録をそれに合わせる（起動したときと、設定の画面で変えたとき）。登録はファイル（<see cref="RunKeyAppFixture"/>）。
/// Windows の「スタートアップ アプリ」で切られたら、設定の画面を開いたときに設定へ写す。
/// 起動のときの書き直しは 1 回しか見られないので、変える試験と 1 本にまとめてある（別にすると、先に走った方の値を見るだけになる）。
/// </summary>
public sealed class StartWithWindowsTests(RunKeyAppFixture app) : IClassFixture<RunKeyAppFixture>
{
    private IPage Page => app.Page;

    private ILocator Switch => Page.Locator(".cfg-item[data-key='StartWithWindows'] input[type=checkbox]");

    private static bool Same(string? registered) => string.Equals(registered, RunKeyAppFixture.Expected, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task TheRegistrationFollowsTheSettingFromTheStartAndWhenItChanges()
    {
        // 前の置き場を指していた登録は、起動のときに今の exe へ書き直されている。
        Assert.True(Same(app.Registered()), $"起動のときに書き直されていない: {app.Registered()}");

        // 登録できる形なので、行が出ている（入）。
        await app.OpenSettings("startup");
        await Assertions.Expect(Switch).ToBeCheckedAsync();

        // 切って適用すると消える。
        await Switch.UncheckAsync();
        await Page.Locator("#cfgCommit").ClickAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).Not.ToBeVisibleAsync();
        await RunKeyAppFixture.WaitForAsync(() => app.Registered() is null, "切っても登録が消えない");

        // 入れ直すと、また書かれる。
        await Switch.CheckAsync();
        await Page.Locator("#cfgCommit").ClickAsync();
        await RunKeyAppFixture.WaitForAsync(() => Same(app.Registered()), "入れ直しても登録が書かれない");

        // Windows の「スタートアップ アプリ」で切られた。設定の画面を開き直すと、設定が切に写り、登録も印も片付く。
        File.WriteAllText(app.Approval, RunKeyAppFixture.DisabledMark);
        await app.OpenSettings("startup");
        await Assertions.Expect(Switch).Not.ToBeCheckedAsync();
        await RunKeyAppFixture.WaitForAsync(() => app.Registered() is null && !File.Exists(app.Approval), "Windows で切った起動が片付かない");
        Assert.False(app.SavedStartWithWindows());
        Assert.Contains(app.Output, line => line.Contains("turned the setting off to match", StringComparison.Ordinal));

        // 設定の画面を通さずに変えても、変えたその場で合わせる（画面が値を取り直すのを待たない）。
        // 設定の画面を離れてから、画面の外の道（適用の口を直に呼ぶ）で変える。設定の画面に居ると、知らせを受けた画面が値を取り直してしまう。
        await Page.EvaluateAsync("() => selectView('log')");
        await Page.EvaluateAsync("() => bridge.applyConfigChanges(JSON.stringify({ host: { StartWithWindows: true } }))");
        await RunKeyAppFixture.WaitForAsync(() => Same(app.Registered()), "設定の画面の外で入れても登録が書かれない");
        await Page.EvaluateAsync("() => bridge.applyConfigChanges(JSON.stringify({ host: { StartWithWindows: false } }))");
        await RunKeyAppFixture.WaitForAsync(() => app.Registered() is null, "設定の画面の外で切っても登録が消えない");
    }
}
