using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Desktop;

/// <summary>
/// 起動したとき、Windows の「スタートアップ アプリ」で切られていた。設定を切に写し（config.json も画面も）、登録と印を片付ける。
/// こちらで入れ直すと、登録を書き直して、切の印は残さない（残すと Run があっても起動しない）。
/// </summary>
public sealed class TurnedOffInWindowsTests(TurnedOffInWindowsAppFixture app) : IClassFixture<TurnedOffInWindowsAppFixture>
{
    private IPage Page => app.Page;

    private ILocator Switch => Page.Locator(".cfg-item[data-key='StartWithWindows'] input[type=checkbox]");

    [Fact]
    public async Task AStartupTurnedOffInWindowsTurnsTheSettingOffAtStartAndOnAgainClearsIt()
    {
        Assert.False(app.SavedStartWithWindows());
        Assert.Null(app.Registered());
        Assert.False(File.Exists(app.Approval));
        Assert.Contains(app.Output, line => line.Contains("turned the setting off to match", StringComparison.Ordinal));

        await app.OpenSettings("startup");
        await Assertions.Expect(Switch).Not.ToBeCheckedAsync();

        // 印だけが残っていた（Windows がまた書いた）としても、入れ直せば消えて登録が書かれる。
        File.WriteAllText(app.Approval, RunKeyAppFixture.DisabledMark);
        await Switch.CheckAsync();
        await Page.Locator("#cfgCommit").ClickAsync();
        await RunKeyAppFixture.WaitForAsync(
            () => string.Equals(app.Registered(), RunKeyAppFixture.Expected, StringComparison.OrdinalIgnoreCase) && !File.Exists(app.Approval),
            "入れ直しても登録が書かれない・切の印が残る");
        Assert.True(app.SavedStartWithWindows());
    }
}
