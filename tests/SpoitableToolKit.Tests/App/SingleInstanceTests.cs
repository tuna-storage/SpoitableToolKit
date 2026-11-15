using SpoitableToolKit.Host.App;
using Xunit;

namespace SpoitableToolKit.Tests.App;

/// <summary>
/// 2 つ目のアプリは別のプロセスだが、Mutex は同じスレッドなら何度でも取れるので、試験では別のスレッドで 2 つ目を真似る。
/// 取ったものは、取ったスレッドの中で離す（Mutex の決まり）。
/// </summary>
public sealed class SingleInstanceTests
{
    private static string Folder() => Path.Combine(Path.GetTempPath(), "stk-single-" + Guid.NewGuid().ToString("N"));

    private static T OnAnotherThread<T>(Func<T> run)
    {
        T result = default!;
        var thread = new Thread(() => result = run());
        thread.Start();
        thread.Join();
        return result;
    }

    /// <summary>取れたかと、前が離さずに終わっていたかを返す。取れたらその場で離す。</summary>
    private static (bool Got, bool Abrupt) TryOnAnotherThread(string folder, TimeSpan wait) => OnAnotherThread(() =>
    {
        using var single = SingleInstance.TryAcquire(folder, wait);
        return (single is not null, single?.PreviousEndedAbruptly ?? false);
    });

    [Fact]
    public void TheSameFolderWrittenDifferentlyGetsTheSameName()
    {
        var folder = Folder();

        Assert.Equal(SingleInstance.MutexNameFor(folder), SingleInstance.MutexNameFor(folder.ToUpperInvariant() + Path.DirectorySeparatorChar));
        Assert.NotEqual(SingleInstance.MutexNameFor(folder), SingleInstance.MutexNameFor(folder + "x"));
        Assert.StartsWith(@"Local\SpoitableToolKit.", SingleInstance.MutexNameFor(folder));
        Assert.Equal(SingleInstance.ShowEventNameFor(folder), SingleInstance.ShowEventNameFor(folder.ToLowerInvariant()));
        Assert.NotEqual(SingleInstance.ShowEventNameFor(folder), SingleInstance.ShowEventNameFor(folder + "x"));
        Assert.NotEqual(SingleInstance.MutexNameFor(folder), SingleInstance.ShowEventNameFor(folder));
    }

    [Fact]
    public void ASecondStartWithTheSameFolderIsRefusedButAnotherFolderIsNot()
    {
        var folder = Folder();
        using var first = SingleInstance.TryAcquire(folder, TimeSpan.Zero);

        Assert.NotNull(first);
        Assert.False(first.PreviousEndedAbruptly);
        Assert.Equal((false, false), TryOnAnotherThread(folder, TimeSpan.Zero));
        Assert.Equal((true, false), TryOnAnotherThread(Folder(), TimeSpan.Zero));
    }

    [Fact]
    public void AfterTheFirstLetsGoTheNextStartGetsIt()
    {
        var folder = Folder();
        var first = SingleInstance.TryAcquire(folder, TimeSpan.Zero);
        Assert.NotNull(first);

        first.Dispose();
        first.Dispose();

        Assert.Equal((true, false), TryOnAnotherThread(folder, TimeSpan.Zero));
    }

    // 前のアプリは記録を書き切ってから離す。待たないと、新しいアプリが即座に終わって何も残らない。
    [Fact]
    public void ARestartWaitsUntilThePreviousAppLetsGo()
    {
        var folder = Folder();
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var previous = new Thread(() =>
        {
            using var single = SingleInstance.TryAcquire(folder, TimeSpan.Zero);
            held.Set();
            release.Wait(TimeSpan.FromSeconds(30));
        });
        previous.Start();
        Assert.True(held.Wait(TimeSpan.FromSeconds(10)));

        var next = Task.Run(() => TryOnAnotherThread(folder, TimeSpan.FromSeconds(20)));
        Assert.False(next.Wait(300), "the restarted app went ahead while the previous one still held the lock");

        release.Set();
        Assert.True(next.Wait(TimeSpan.FromSeconds(20)));
        Assert.Equal((true, false), next.Result);
        previous.Join();
    }

    [Fact]
    public void APreviousAppThatEndedWithoutLettingGoDoesNotBlockTheNextStart()
    {
        var folder = Folder();

        // 取ったまま離さずにスレッドを終える。持ち物は最後まで掴んでおく（放すと GC がハンドルを閉じ、印ごと消えて「落ちていた」が分からなくなる）。
        var previous = OnAnotherThread(() => SingleInstance.TryAcquire(folder, TimeSpan.Zero));
        Assert.NotNull(previous);

        Assert.Equal((true, true), TryOnAnotherThread(folder, TimeSpan.Zero));
        GC.KeepAlive(previous);
    }

    // 1 つ目がまだ待っていなくても（窓ができる前でも）、印は立ったまま残っていて受け取れる。
    [Fact]
    public void AnotherStartAsksTheRunningOneToComeToTheFront()
    {
        var folder = Folder();
        using var first = SingleInstance.TryAcquire(folder, TimeSpan.Zero);
        Assert.NotNull(first);

        OnAnotherThread(() =>
        {
            SingleInstance.AskTheRunningOneToShow(Folder());
            SingleInstance.AskTheRunningOneToShow(folder);
            return true;
        });

        Assert.True(first.ShowRequests.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.False(first.ShowRequests.WaitOne(TimeSpan.FromMilliseconds(200)), "one request should come to the front once");
    }

    [Fact]
    public void OnlyARestartedAppIsMarkedAsARestart()
    {
        Assert.True(AppRestart.IsRestart(AppRestart.ArgumentsFor(["--x"], 1234)));
        Assert.False(AppRestart.IsRestart(["--x"]));
        Assert.False(AppRestart.IsRestart([]));
    }
}
