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

    /// <summary>
    /// Windows の「スタートアップ アプリ」の印（ふつうは 12 バイト）。先頭のバイトが奇数なら切、偶数なら入。長さは見ない。値はあるのに空なら入、値が無ければ印なし。
    /// </summary>
    [Theory]
    [InlineData("03 00 00 00 00 40 2B 6A 12 34 DB 01", "Disabled")]
    [InlineData("03 00 00 00 00 00 00 00 00 00 00 00", "Disabled")]
    [InlineData("01 00 00 00 00 00 00 00 00 00 00 00", "Disabled")]
    [InlineData("07 00 00 00 00 40 2B 6A 12 34 DB 01", "Disabled")]
    [InlineData("02 00 00 00 00 00 00 00 00 00 00 00", "Enabled")]
    [InlineData("06 00 00 00 00 00 00 00 00 00 00 00", "Enabled")]
    [InlineData("03", "Disabled")]
    [InlineData("02", "Enabled")]
    [InlineData("", "Enabled")]
    [InlineData(null, "Missing")]
    public void TheWindowsMarkIsReadFromItsFirstByte(string? hex, string expected)
    {
        var mark = hex is null ? null : Convert.FromHexString(hex.Replace(" ", ""));

        Assert.Equal(Enum.Parse<StartupApproval>(expected), StartupRegistration.ApprovalOf(mark));
    }

    /// <summary>Windows で切られた起動を設定へ写すのは、設定が入で印が切のときだけ。</summary>
    [Theory]
    [InlineData(true, "Disabled", true)]
    [InlineData(true, "Enabled", false)]
    [InlineData(true, "Missing", false)]
    [InlineData(false, "Disabled", false)]
    [InlineData(false, "Enabled", false)]
    public void OnlyAnOnSettingTurnedOffInWindowsFollowsWindows(bool wanted, string approval, bool expected)
    {
        Assert.Equal(expected, StartupRegistration.TurnedOffInWindows(wanted, Enum.Parse<StartupApproval>(approval)));
    }

    /// <summary>入なら切の印だけを消す（入の印は Windows のもの。消さなくても起動する）。切ならどの印も残さない。</summary>
    [Theory]
    [InlineData(true, "Disabled", true)]
    [InlineData(true, "Enabled", false)]
    [InlineData(true, "Missing", false)]
    [InlineData(false, "Disabled", true)]
    [InlineData(false, "Enabled", true)]
    [InlineData(false, "Missing", false)]
    public void TheMarkIsClearedSoItAgreesWithTheSetting(bool wanted, string approval, bool expected)
    {
        Assert.Equal(expected, StartupRegistration.ClearsApproval(wanted, Enum.Parse<StartupApproval>(approval)));
    }

    /// <summary>e2e の向け先では、印は隣のファイルに 16 進で入っている（レジストリの値と同じ並び）。読めない中身は読めないものとして扱う（例外。起動は止めない側で受ける）。</summary>
    [Fact]
    public void TheE2eFileKeepsTheMarkNextToIt()
    {
        var path = Path.Combine(Path.GetTempPath(), "stk-tests", Guid.NewGuid().ToString("N") + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var key = new RunKeyFile(path);
        try
        {
            Assert.Equal(StartupApproval.Missing, key.ReadApproval());

            File.WriteAllText(key.ApprovalPath, "03 00 00 00 00 40 2B 6A 12 34 DB 01\r\n");
            Assert.Equal(StartupApproval.Disabled, key.ReadApproval());

            File.WriteAllText(key.ApprovalPath, "02 00 00 00 00 00 00 00 00 00 00 00");
            Assert.Equal(StartupApproval.Enabled, key.ReadApproval());

            File.WriteAllText(key.ApprovalPath, "not hex");
            Assert.Throws<IOException>(() => key.ReadApproval());

            key.RemoveApproval();
            Assert.False(File.Exists(key.ApprovalPath));
        }
        finally
        {
            if (File.Exists(key.ApprovalPath)) File.Delete(key.ApprovalPath);
        }
    }
}
