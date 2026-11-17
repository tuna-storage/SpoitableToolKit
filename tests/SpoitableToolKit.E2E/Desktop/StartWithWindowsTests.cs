using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Desktop;

/// <summary>
/// Windows の起動時の起動。設定が正で、登録をそれに合わせる（起動したときと、設定の画面で変えたとき）。登録はファイル（<see cref="RunKeyAppFixture"/>）。
/// 起動のときの書き直しは 1 回しか見られないので、変える試験と 1 本にまとめてある（別にすると、先に走った方の値を見るだけになる）。
/// </summary>
public sealed class StartWithWindowsTests(RunKeyAppFixture app) : IClassFixture<RunKeyAppFixture>
{
    private IPage Page => app.Page;

    private ILocator Switch => Page.Locator(".cfg-item[data-key='StartWithWindows'] input[type=checkbox]");

    private string? Registered() => File.Exists(app.RunKey) ? File.ReadAllText(app.RunKey).Trim() : null;

    [Fact]
    public async Task TheRegistrationFollowsTheSettingFromTheStartAndWhenItChanges()
    {
        // 前の置き場を指していた登録は、起動のときに今の exe へ書き直されている。
        Assert.Equal(RunKeyAppFixture.Expected, Registered(), StringComparer.OrdinalIgnoreCase);

        // 登録できる形なので、行が出ている（入）。
        await app.OpenSettings("startup");
        await Assertions.Expect(Switch).ToBeCheckedAsync();

        // 切って適用すると消える。
        await Switch.UncheckAsync();
        await Page.Locator("#cfgCommit").ClickAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).Not.ToBeVisibleAsync();
        await WaitForAsync(() => Registered() is null);

        // 入れ直すと、また書かれる。
        await Switch.CheckAsync();
        await Page.Locator("#cfgCommit").ClickAsync();
        await WaitForAsync(() => Registered() is { } value && string.Equals(value, RunKeyAppFixture.Expected, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WaitForAsync(Func<bool> done)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (done()) return;
            await Task.Delay(250);
        }

        Assert.True(done(), "登録が設定に合わなかった");
    }
}
