using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.App.Startup;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.App.Startup;

/// <summary>
/// Windows の起動時の起動。設定が正で、登録（HKCU の Run）と Windows の「スタートアップ アプリ」の印をそれに合わせる。ここでは本物に触らない（<see cref="MemoryRunKey"/>）。
/// 例外は 1 つ: Windows の側で切られていたら、起動したとき・設定の画面を開いたときに、それを設定へ写す（<see cref="WindowsStartup.Reconcile"/>）。
/// 登録できない形（開発の bin・持ち運ぶ版）では、レジストリに一切触らない — 開発版は手元のインストール版と同じ設定・同じ HKCU で動く。
/// </summary>
public sealed class WindowsStartupTests
{
    private const string Command = "\"C:\\Users\\me\\AppData\\Local\\SpoitableToolKit.App\\current\\SpoitableToolKit.Host.exe\" --tray";

    private sealed class MemoryRunKey(string? value, StartupApproval approval = StartupApproval.Missing) : IRunKey
    {
        public string? Value { get; private set; } = value;

        public StartupApproval Approval { get; private set; } = approval;

        public List<string> Calls { get; } = [];

        public string Where => "memory";

        public string? Read()
        {
            Calls.Add("read");
            return Value;
        }

        public void Write(string command)
        {
            Calls.Add("write");
            Value = command;
        }

        public void Remove()
        {
            Calls.Add("remove");
            Value = null;
        }

        public StartupApproval ReadApproval()
        {
            Calls.Add("approval");
            return Approval;
        }

        public void RemoveApproval()
        {
            Calls.Add("remove approval");
            Approval = StartupApproval.Missing;
        }
    }

    private sealed class BrokenRunKey : IRunKey
    {
        public string Where => "broken";

        public string? Read() => throw new UnauthorizedAccessException("no");

        public void Write(string command) => throw new UnauthorizedAccessException("no");

        public void Remove() => throw new UnauthorizedAccessException("no");

        public StartupApproval ReadApproval() => throw new UnauthorizedAccessException("no");

        public void RemoveApproval() => throw new UnauthorizedAccessException("no");
    }

    /// <summary>設定を書く口の代わり。書いた値を設定に入れて、呼ばれた値を覚える（本物は SettingsApplier を通る）。</summary>
    private sealed class Setter(HostConfig config, bool succeeds = true)
    {
        public List<bool> Written { get; } = [];

        public bool Set(bool wanted)
        {
            Written.Add(wanted);
            if (succeeds) config.StartWithWindows = wanted;
            return succeeds;
        }
    }

    private static WindowsStartup Startup(IRunKey? key, bool wanted)
    {
        var config = new HostConfig { StartWithWindows = wanted };
        return new(key, Command, config, new Setter(config).Set, NullLogger.Instance);
    }

    [Fact]
    public void TurnedOnWithNothingRegisteredItIsWritten()
    {
        var key = new MemoryRunKey(null);

        Startup(key, wanted: true).Sync();

        Assert.Equal(Command, key.Value);
    }

    /// <summary>入れ直して置き場が変わった・前の版が別のものを書いた。設定が入なら、いまの exe へ書き直す。</summary>
    [Fact]
    public void TurnedOnPointingElsewhereItIsRewritten()
    {
        var key = new MemoryRunKey("\"D:\\old\\SpoitableToolKit.Host.exe\" --tray");

        Startup(key, wanted: true).Sync();

        Assert.Equal(Command, key.Value);
    }

    /// <summary>合っていれば書かない（起動のたびにレジストリを書き換えない）。パスの大文字小文字は見ない。Windows の側で入なら、その印にも触らない。</summary>
    [Fact]
    public void TurnedOnAndAlreadyRightNothingIsWritten()
    {
        var key = new MemoryRunKey(Command.ToUpperInvariant(), StartupApproval.Enabled);

        Startup(key, wanted: true).Sync();

        var calls = key.Calls;
        Assert.Equal(["read", "approval"], calls);
    }

    /// <summary>切なら Run の値も Windows の印も残さない（Run が無いのに印だけ、Run があるのに切の印だけ、の形にしない）。</summary>
    [Fact]
    public void TurnedOffItIsRemovedWithItsMark()
    {
        var key = new MemoryRunKey(Command, StartupApproval.Enabled);

        Startup(key, wanted: false).Sync();

        Assert.Null(key.Value);
        Assert.Equal(StartupApproval.Missing, key.Approval);
    }

    [Fact]
    public void TurnedOffWithNothingRegisteredNothingIsTouched()
    {
        var key = new MemoryRunKey(null);

        Startup(key, wanted: false).Sync();

        var calls = key.Calls;
        Assert.Equal(["read", "approval"], calls);
    }

    /// <summary>登録できない形では行も出さない。</summary>
    [Fact]
    public void WithoutAPlaceToRegisterItIsNotAvailable()
    {
        Assert.False(Startup(null, wanted: true).Available);
        Assert.True(Startup(new MemoryRunKey(null), wanted: true).Available);
    }

    /// <summary>読めない・書けない（権限・壊れたレジストリ）でも起動は止めない。</summary>
    [Fact]
    public void ARegistryThatCannotBeReadDoesNotStopTheApp()
    {
        var startup = Startup(new BrokenRunKey(), wanted: true);

        Assert.Null(Record.Exception(startup.Sync));
        Assert.Null(Record.Exception(startup.Reconcile));
    }

    /// <summary>設定の画面で変えたら、その場で合わせ直す。ほかの値を変えただけなら触らない。</summary>
    [Fact]
    public void ChangingTheSettingSyncsAndOtherChangesDoNot()
    {
        var key = new MemoryRunKey(null);
        var config = new HostConfig();
        var startup = new WindowsStartup(key, Command, config, new Setter(config).Set, NullLogger.Instance);

        config.StartInTray = true;
        startup.OnConfigChanged(ConfigTesting.Changed(config, nameof(HostConfig.StartInTray)));
        Assert.Empty(key.Calls);

        config.StartWithWindows = true;
        startup.OnConfigChanged(ConfigTesting.Changed(config, nameof(HostConfig.StartWithWindows)));
        Assert.Equal(Command, key.Value);

        config.StartWithWindows = false;
        startup.OnConfigChanged(ConfigTesting.Changed(config, nameof(HostConfig.StartWithWindows)));
        Assert.Null(key.Value);
    }

    /// <summary>
    /// Windows の「スタートアップ アプリ」で切られていた（起動したとき・設定の画面を開いたとき）。設定を切に写し、そのあとは設定が正の決まりで、
    /// Run の値も切の印も片付ける。
    /// </summary>
    [Fact]
    public void AStartupTurnedOffInWindowsTurnsTheSettingOff()
    {
        var key = new MemoryRunKey(Command, StartupApproval.Disabled);
        var config = new HostConfig { StartWithWindows = true };
        var setter = new Setter(config);

        new WindowsStartup(key, Command, config, setter.Set, NullLogger.Instance).Reconcile();

        Assert.Equal([false], setter.Written);
        Assert.False(config.StartWithWindows);
        Assert.Null(key.Value);
        Assert.Equal(StartupApproval.Missing, key.Approval);
    }

    /// <summary>Windows の側で入なら、設定はそのまま（書かない）。</summary>
    [Fact]
    public void AStartupAllowedInWindowsLeavesTheSettingAlone()
    {
        var key = new MemoryRunKey(Command, StartupApproval.Enabled);
        var config = new HostConfig { StartWithWindows = true };
        var setter = new Setter(config);

        new WindowsStartup(key, Command, config, setter.Set, NullLogger.Instance).Reconcile();

        Assert.Empty(setter.Written);
        Assert.True(config.StartWithWindows);
        Assert.Equal(Command, key.Value);
        Assert.Equal(StartupApproval.Enabled, key.Approval);
    }

    /// <summary>設定へ写せなかった（断られた）ら、何もしない。そのまま設定に合わせると、Windows で切った起動をこちらが入れ直してしまう。</summary>
    [Fact]
    public void WhenTheSettingCannotFollowWindowsNothingIsTurnedBackOn()
    {
        var key = new MemoryRunKey(Command, StartupApproval.Disabled);
        var config = new HostConfig { StartWithWindows = true };

        new WindowsStartup(key, Command, config, new Setter(config, succeeds: false).Set, NullLogger.Instance).Reconcile();

        Assert.Equal(Command, key.Value);
        Assert.Equal(StartupApproval.Disabled, key.Approval);
    }

    /// <summary>こちらで入れ直したら、Windows の切の印も消す（消さないと Run があっても起動しない）。設定を変えたその場では、印を設定に写さない。</summary>
    [Fact]
    public void TurningItBackOnInTheAppClearsTheWindowsTurnOff()
    {
        var key = new MemoryRunKey(null, StartupApproval.Disabled);
        var config = new HostConfig();
        var setter = new Setter(config);
        var startup = new WindowsStartup(key, Command, config, setter.Set, NullLogger.Instance);

        config.StartWithWindows = true;
        startup.OnConfigChanged(ConfigTesting.Changed(config, nameof(HostConfig.StartWithWindows)));

        Assert.Empty(setter.Written);
        Assert.Equal(Command, key.Value);
        Assert.Equal(StartupApproval.Missing, key.Approval);
    }

    /// <summary>設定が切で、Windows の印だけが残っていた（前に Run だけ消された）。写すものは無く、印を片付ける。</summary>
    [Fact]
    public void ALeftoverMarkIsClearedWhenTheSettingIsOff()
    {
        var key = new MemoryRunKey(null, StartupApproval.Disabled);
        var config = new HostConfig();
        var setter = new Setter(config);

        new WindowsStartup(key, Command, config, setter.Set, NullLogger.Instance).Reconcile();

        Assert.Empty(setter.Written);
        Assert.Equal(StartupApproval.Missing, key.Approval);
    }

    /// <summary>
    /// 試験・開発の起動（Velopack が入れた形でない）では本物のレジストリを使わない。e2e の向け先が無ければ登録できない（行も出さない）。
    /// e2e の向け先があれば、そのファイルを登録として使う。
    /// </summary>
    [Fact]
    public void OutsideTheInstalledAppOnlyTheE2eFileIsUsed()
    {
        var previous = Environment.GetEnvironmentVariable(WindowsStartup.FileVariable);
        var file = Path.Combine(Path.GetTempPath(), "stk-tests", Guid.NewGuid().ToString("N") + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        try
        {
            Environment.SetEnvironmentVariable(WindowsStartup.FileVariable, null);
            Assert.False(WindowsStartup.Create(new HostConfig { StartWithWindows = true }, _ => true, NullLogger.Instance).Available);

            Environment.SetEnvironmentVariable(WindowsStartup.FileVariable, file);
            var startup = WindowsStartup.Create(new HostConfig { StartWithWindows = true }, _ => true, NullLogger.Instance);
            startup.Sync();

            Assert.True(startup.Available);
            Assert.Equal(StartupRegistration.CommandFor(Environment.ProcessPath!), File.ReadAllText(file));
        }
        finally
        {
            Environment.SetEnvironmentVariable(WindowsStartup.FileVariable, previous);
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
