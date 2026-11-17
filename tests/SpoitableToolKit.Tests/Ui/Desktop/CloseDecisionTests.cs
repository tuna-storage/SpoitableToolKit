using SpoitableToolKit.Host.App.Startup;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Desktop;
using SpoitableToolKit.Host.Ui.Desktop.Closing;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Desktop;

/// <summary>窓が閉じられかけたときに何をするか。設定に従うのは利用者が閉じたときだけで、ログオフやタスクマネージャーは聞かずに終わる。</summary>
public sealed class CloseDecisionTests
{
    /// <summary>×・Alt+F4・タスクバーの「ウィンドウを閉じる」はどれも UserClosing。設定どおりに聞く・しまう・終了する。</summary>
    [Theory]
    [InlineData("Ask", "Ask")]
    [InlineData("Tray", "Tray")]
    [InlineData("Exit", "Exit")]
    public void TheCloseButtonFollowsTheSetting(string setting, string expected)
    {
        var step = CloseDecision.For(CloseReason.UserClosing, Enum.Parse<CloseButtonAction>(setting));

        Assert.Equal(Enum.Parse<CloseStep>(expected), step);
    }

    /// <summary>ログオフを止めない。タスクマネージャーと taskkill（WM_CLOSE）は終わらせたい人がしていることなので、しまう設定でも終わる。</summary>
    [Theory]
    [InlineData("WindowsShutDown", "Ask")]
    [InlineData("WindowsShutDown", "Tray")]
    [InlineData("TaskManagerClosing", "Ask")]
    [InlineData("TaskManagerClosing", "Tray")]
    [InlineData("ApplicationExitCall", "Tray")]
    [InlineData("None", "Ask")]
    public void OtherReasonsExitWithoutAsking(string reason, string setting)
    {
        var step = CloseDecision.For(Enum.Parse<CloseReason>(reason), Enum.Parse<CloseButtonAction>(setting));

        Assert.Equal(CloseStep.Exit, step);
    }

    /// <summary>聞けないときに終了まではしない（押した人が思っていたより強いことをしない）。設定は聞くのまま（ここは決めるだけで、書かない）。</summary>
    [Fact]
    public void WhenTheShellCannotAskTheWindowGoesToTheTray()
    {
        Assert.Equal(CloseStep.Tray, CloseDecision.WhenTheShellCannotAsk);
    }

    [Fact]
    public void ByDefaultTheFirstCloseAsks()
    {
        Assert.Equal(CloseButtonAction.Ask, new HostConfig().CloseButton);
    }

    /// <summary>Windows の起動時の起動は、設定に関係なくしまって始まる。</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void TheAppStartsInTheTrayWithTheArgumentOrTheSetting(bool argument, bool setting, bool expected)
    {
        string[] args = argument ? ["--x", WindowsStartup.TrayArgument] : ["--x"];

        Assert.Equal(expected, DesktopApp.StartsInTray(args, new HostConfig { StartInTray = setting }));
    }

    [Fact]
    public void ByDefaultTheAppOpensItsWindow()
    {
        Assert.False(new HostConfig().StartInTray);
        Assert.False(DesktopApp.StartsInTray([], new HostConfig()));
    }
}
