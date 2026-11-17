using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.App.Startup;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.App.Startup;

/// <summary>
/// Windows の起動時の起動。設定が正で、登録（HKCU の Run）をそれに合わせる。登録はここでは本物に触らない（<see cref="MemoryRunKey"/>）。
/// 登録できない形（開発の bin・持ち運ぶ版）では、レジストリに一切触らない — 開発版は手元のインストール版と同じ設定・同じ HKCU で動く。
/// </summary>
public sealed class WindowsStartupTests
{
    private const string Command = "\"C:\\Users\\me\\AppData\\Local\\SpoitableToolKit.App\\current\\SpoitableToolKit.Host.exe\" --tray";

    private sealed class MemoryRunKey(string? value) : IRunKey
    {
        public string? Value { get; private set; } = value;

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
    }

    private sealed class BrokenRunKey : IRunKey
    {
        public string Where => "broken";

        public string? Read() => throw new UnauthorizedAccessException("no");

        public void Write(string command) => throw new UnauthorizedAccessException("no");

        public void Remove() => throw new UnauthorizedAccessException("no");
    }

    private static WindowsStartup Startup(IRunKey? key, bool wanted) =>
        new(key, Command, new HostConfig { StartWithWindows = wanted }, NullLogger.Instance);

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

    /// <summary>合っていれば書かない（起動のたびにレジストリを書き換えない）。パスの大文字小文字は見ない。</summary>
    [Fact]
    public void TurnedOnAndAlreadyRightNothingIsWritten()
    {
        var key = new MemoryRunKey(Command.ToUpperInvariant());

        Startup(key, wanted: true).Sync();

        var calls = key.Calls;
        Assert.Equal(["read"], calls);
    }

    [Fact]
    public void TurnedOffItIsRemoved()
    {
        var key = new MemoryRunKey(Command);

        Startup(key, wanted: false).Sync();

        Assert.Null(key.Value);
        Assert.Contains("remove", key.Calls);
    }

    [Fact]
    public void TurnedOffWithNothingRegisteredNothingIsTouched()
    {
        var key = new MemoryRunKey(null);

        Startup(key, wanted: false).Sync();

        var calls = key.Calls;
        Assert.Equal(["read"], calls);
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

        var thrown = Record.Exception(startup.Sync);

        Assert.Null(thrown);
    }

    /// <summary>設定の画面で変えたら、その場で合わせ直す。ほかの値を変えただけなら触らない。</summary>
    [Fact]
    public void ChangingTheSettingSyncsAndOtherChangesDoNot()
    {
        var key = new MemoryRunKey(null);
        var config = new HostConfig();
        var startup = new WindowsStartup(key, Command, config, NullLogger.Instance);

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
            Assert.False(WindowsStartup.Create(new HostConfig { StartWithWindows = true }, NullLogger.Instance).Available);

            Environment.SetEnvironmentVariable(WindowsStartup.FileVariable, file);
            var startup = WindowsStartup.Create(new HostConfig { StartWithWindows = true }, NullLogger.Instance);
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
