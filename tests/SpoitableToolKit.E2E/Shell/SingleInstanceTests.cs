using System.Diagnostics;
using Xunit;

namespace SpoitableToolKit.E2E.Shell;

/// <summary>2 つ目は終了コード 3 で終わり、1 つ目は「前に出て」を受け取る。1 つ目の窓は画面の外なので前には出ず（人の窓からフォーカスを奪わない）、アプリログに 1 行出る。</summary>
public sealed class SingleInstanceTests(AppFixture app) : IClassFixture<AppFixture>
{
    [Fact]
    public async Task ASecondStartWithTheSameDataFolderEndsAtOnceAndTheFirstHearsIt()
    {
        using var second = app.StartAnotherWithTheSameDataFolder();
        try
        {
            Assert.True(second.WaitForExit(30_000), "the second app with the same data folder kept running");
            Assert.Equal(3, second.ExitCode);
        }
        finally
        {
            if (!second.HasExited) second.Kill(entireProcessTree: true);
        }

        using (var first = Process.GetProcessById(app.ProcessId)) Assert.False(first.HasExited);

        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (app.Output.Any(line => line.Contains("started again with the same data folder", StringComparison.Ordinal))
                && app.Output.Any(line => line.StartsWith("[second]", StringComparison.Ordinal) && line.Contains("already running", StringComparison.Ordinal)))
                break;
            await Task.Delay(250);
        }

        Assert.Contains(app.Output, line => line.Contains("started again with the same data folder", StringComparison.Ordinal)
                                            && !line.StartsWith("[second]", StringComparison.Ordinal));
        Assert.Contains(app.Output, line => line.StartsWith("[second]", StringComparison.Ordinal) && line.Contains("already running", StringComparison.Ordinal));
    }
}
