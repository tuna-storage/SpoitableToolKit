using System.Diagnostics;
using Xunit;

namespace SpoitableToolKit.Tests.Updates;

public sealed class NightlyVersionTests
{
    private static string Script()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpoitableToolKit.slnx"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("no SpoitableToolKit.slnx above the tests"),
            ".github", "scripts", "nightly-version.ps1");
    }

    private static (int ExitCode, string Output) Run(params string[] arguments)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in (string[])["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Script(), .. arguments])
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("powershell did not start");
        var output = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output.Trim());
    }

    [Theory]
    [InlineData("42", "ABC1234DEF0", "2026-11-15T03:00:00Z", "2026.11.15-42.gabc1234")]
    [InlineData("42", "abc1234def0", "2026-11-15T16:30:00Z", "2026.11.16-42.gabc1234")]
    [InlineData("7", "0123456789", "2026-01-04T15:00:00Z", "2026.1.5-7.g0123456")]
    [InlineData("1", "deadbee", "2026-12-31T14:59:59Z", "2026.12.31-1.gdeadbee")]
    public void TheVersionIsTheDateTheRunAndTheHash(string run, string sha, string at, string expected)
    {
        var (exitCode, output) = Run("-RunNumber", run, "-Sha", sha, "-At", at);

        Assert.Equal(0, exitCode);
        Assert.Equal(expected, output);
    }

    [Theory]
    [InlineData("0", "abc1234")]
    [InlineData("3", "abc12")]
    [InlineData("3", "not-a-hash")]
    public void ARunNumberOrAHashThatCannotMakeAVersionStops(string run, string sha)
    {
        var (exitCode, _) = Run("-RunNumber", run, "-Sha", sha, "-At", "2026-11-15T03:00:00Z");

        Assert.NotEqual(0, exitCode);
    }
}
