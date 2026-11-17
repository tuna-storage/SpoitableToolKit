using SpoitableToolKit.Host.App;
using SpoitableToolKit.Host.App.Startup;
using Xunit;

namespace SpoitableToolKit.Tests.App.Startup;

/// <summary>何を登録するか・設定とどう合わせるか（置き場を知らない形）と、自動で起動した回の「再起動」。</summary>
public sealed class StartupRegistrationTests
{
    /// <summary>パスは空白を含みうる（利用者名・フォルダ）ので囲む。しまって始める印を付ける。</summary>
    [Fact]
    public void TheCommandQuotesThePathAndStartsInTheTray()
    {
        Assert.Equal("\"C:\\Users\\A B\\app\\SpoitableToolKit.Host.exe\" --tray",
            StartupRegistration.CommandFor("C:\\Users\\A B\\app\\SpoitableToolKit.Host.exe"));
    }

    [Theory]
    [InlineData(true, null, "Write")]
    [InlineData(true, "\"D:\\other.exe\" --tray", "Write")]
    [InlineData(true, "\"C:\\app.exe\"", "Write")]
    [InlineData(true, "\"C:\\app.exe\" --tray", "None")]
    [InlineData(true, "\"c:\\APP.exe\" --tray", "None")]
    [InlineData(false, "\"C:\\app.exe\" --tray", "Remove")]
    [InlineData(false, "\"D:\\other.exe\"", "Remove")]
    [InlineData(false, null, "None")]
    public void TheSettingDecides(bool wanted, string? registered, string expected)
    {
        var change = StartupRegistration.Plan(wanted, registered, "\"C:\\app.exe\" --tray");

        Assert.Equal(Enum.Parse<StartupChange>(expected), change);
    }

    /// <summary>自動で起動した回に窓から「再起動」すると、窓が消えてトレイに隠れたまま戻ってくる。しまって始める印は引き継がない。</summary>
    [Fact]
    public void TheRestartDoesNotCarryTheTrayArgument()
    {
        var next = AppRestart.ArgumentsFor(["--x", WindowsStartup.TrayArgument, "--y"], 42);

        Assert.Equal(["--x", "--y", AppRestart.WaitArgument, "42"], next);
    }
}
