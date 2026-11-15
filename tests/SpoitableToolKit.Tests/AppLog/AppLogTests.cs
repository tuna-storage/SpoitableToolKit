using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.AppLog;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Events.AppLog;
using Xunit;

namespace SpoitableToolKit.Tests.AppLog;

/// <summary>
/// ソフトウェア自身のログ。<c>WinExe</c> でコンソールが無く <c>AddSimpleConsole</c> の出力は消えるので、落ちたときに残るのは追記ファイル（1 行ごとに flush）。
/// 起動をまたいで画面を読み直す記録（<c>applog.db</c>）は <see cref="AppLogStoreTests"/>。
/// </summary>
public sealed class AppLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "stk-applog-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>出た行を受け取る試験の受け手（足すのはアプリとして — 口はアプリだけを通す）。</summary>
    private sealed class Ear(Action<AppLogEntry> heard) : IAppLogEventListener
    {
        public void OnAppLogWritten(AppLogWrittenEventParams e) => heard(e.Entry);
    }

    private static void Listen(AppLogSink sink, Action<AppLogEntry> heard) =>
        sink.Written.AddListener<IAppLogEventListener>(HostOwner.Instance, new Ear(heard));

    private static AppLogEntry Entry(
        string message,
        LogLevel level = LogLevel.Information,
        string category = "SpoitableToolKit.Host.VrcLog.VrcLogBus",
        string? exception = null) =>
        new(DateTimeOffset.Parse("2026-08-31T00:58:37+09:00"), level, category, message, exception, 12);

    [Fact]
    public void FileNameFollowsThePattern()
    {
        using var file = new AppLogFile(_dir, keep: 5, DateTimeOffset.Parse("2026-08-31T00:58:37+09:00"));

        Assert.Equal("2026-08-31_00-58-37.log", Path.GetFileName(file.Path));
    }

    [Fact]
    public void TwoWithinTheSameSecondGetSeparateFiles()
    {
        var now = DateTimeOffset.Parse("2026-08-31T00:58:37+09:00");
        using var first = new AppLogFile(_dir, keep: 5, now);
        using var second = new AppLogFile(_dir, keep: 5, now);

        // 同じ名前にすると 2 つのプロセスが 1 つのファイルへ書いて行が混ざる。
        Assert.NotEqual(first.Path, second.Path);
        Assert.Equal("2026-08-31_00-58-37_2.log", Path.GetFileName(second.Path));
    }

    [Fact]
    public void TheSixthRunDropsTheOldestFile()
    {
        // **1 本ずつ閉じてから次を作る。** 前の起動のファイルは閉じているので、これが現実の形。
        // 開いたまま並べると Windows が消させてくれず、間引きが効かない
        // （そちらも握り潰して続ける作りにしてある — 消せないくらいで落ちる方が困る）。
        for (var i = 0; i < 6; i++)
        {
            using var file = new AppLogFile(_dir, keep: 5, DateTimeOffset.Parse($"2026-08-31T00:0{i}:00+09:00"));
            file.Write(Entry($"{i} 回目の起動"));
        }

        var remaining = Directory.GetFiles(_dir, "*.log").Select(Path.GetFileName).Order().ToArray();

        Assert.Equal(5, remaining.Length);
        Assert.DoesNotContain("2026-08-31_00-00-00.log", remaining);
        Assert.Contains("2026-08-31_00-05-00.log", remaining);
    }

    [Fact]
    public void LinesReachTheFileImmediately()
    {
        using var file = new AppLogFile(_dir, keep: 5, DateTimeOffset.Now);
        file.Write(Entry("tail を開始した"));

        // **Dispose せずに読む。** まとめて流す作りにすると、ここで空になる —
        // 追記ファイルを選んだ理由（強制終了しても残る）がそれで消える。
        var text = ReadWhileOpen(file.Path!);

        Assert.Contains("tail を開始した", text);
        Assert.Contains("INF", text);
        Assert.Contains("#12", text);
        Assert.Contains("SpoitableToolKit.Host.VrcLog.VrcLogBus", text);
        // 日時は画面と同じ形。**ミリ秒だけ足す**（競合を追うときにここの差が効く）。
        Assert.Contains("2026/08/31 00:58:37.000", text);
    }

    [Fact]
    public void ExceptionsAreKeptIndentedNotCollapsed()
    {
        using var file = new AppLogFile(_dir, keep: 5, DateTimeOffset.Now);
        file.Write(Entry("落ちた", LogLevel.Error, exception: "System.IO.IOException: だめ\n   at Foo.Bar()"));

        var lines = ReadWhileOpen(file.Path!).Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        Assert.Contains("    System.IO.IOException: だめ", lines);
        Assert.Contains("       at Foo.Bar()", lines);
    }

    [Fact]
    public void SurvivesAnUncreatableDirectory()
    {
        // ファイルを置き場の名前で作っておくと、ディレクトリが作れない。
        var blocked = Path.Combine(_dir, "blocked");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(blocked, "");

        using var file = new AppLogFile(blocked, keep: 5, DateTimeOffset.Now);
        file.Write(Entry("これは捨てられる"));

        // ログが書けないせいでアプリが落ちるのは本末転倒。黙って諦める。
        Assert.Null(file.Path);
    }

    [Fact]
    public void TheLedgerEvictsOldestAtTheCap()
    {
        using var sink = new AppLogSink();
        for (var i = 0; i < AppLogSink.Capacity + 10; i++) sink.Append(Entry($"行 {i}"));

        var recent = sink.Recent();

        Assert.Equal(AppLogSink.Capacity, recent.Count);
        Assert.Equal("行 10", recent[0].Message);
    }

    [Fact]
    public void SequenceNumbersStartAtOne()
    {
        using var sink = new AppLogSink();
        sink.Append(Entry("ひとつめ"));
        sink.Append(Entry("ふたつめ"));

        Assert.Equal([1, 2], sink.Recent().Select(e => e.Seq));
    }

    [Fact]
    public void TheLedgerCatchesUpOnLinesBeforeSubscribing()
    {
        using var sink = new AppLogSink();
        sink.Append(Entry("画面が開く前に出た"));

        var seen = new List<AppLogEntry>();
        Listen(sink, seen.Add);
        sink.Append(Entry("開いた後に出た"));

        // **起動でいちばん知りたい行ほど push では届いていない**（モジュールの読み込みも
        // CEF の起動も画面より先）。だから getStatus で台帳を渡している。
        Assert.Equal(["開いた後に出た"], seen.Select(e => e.Message));
        Assert.Equal(["画面が開く前に出た", "開いた後に出た"], sink.Recent().Select(e => e.Message));
    }

    [Fact]
    public void ASubscriberThatLogsDoesNotRecurse()
    {
        using var sink = new AppLogSink();
        var depth = 0;

        Listen(sink, _ =>
        {
            depth++;
            sink.Append(Entry("購読者の中から出したログ"));
        });

        sink.Append(Entry("きっかけ"));

        // 再入を止めていないとここで StackOverflow になる。
        Assert.Equal(1, depth);
        Assert.Equal(["きっかけ"], sink.Recent().Select(e => e.Message));
    }

    /// <summary>
    /// 1 人が投げても他へは届く。投げたことは<b>握り潰さずに標準エラーへ</b>（ILogger へ書くとこの受け口へ戻ってきて、入れ子の守りで捨てられる）。
    /// </summary>
    [Fact]
    public void AThrowingSubscriberDoesNotStopTheOthers()
    {
        var errors = new StringWriter();
        using var sink = new AppLogSink(errors: errors);
        var reached = false;

        Listen(sink, _ => throw new InvalidOperationException("わざと"));
        Listen(sink, _ => reached = true);
        sink.Append(Entry("きっかけ"));

        Assert.True(reached);
        var written = errors.ToString();
        Assert.Contains("[app log]", written, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException: わざと", written, StringComparison.Ordinal);
        Assert.Equal(["きっかけ"], sink.Recent().Select(e => e.Message));
    }

    /// <summary>受け口の中のことを書く先は、警告より上だけを書き、scope を求められても null を返さない（<c>using</c> で包む呼び手が落ちる）。</summary>
    [Fact]
    public void TheStandardErrorLoggerWritesWarningsOnlyAndHandsOutAScope()
    {
        var to = new StringWriter();
        var logger = new StandardErrorLogger(to);

        logger.LogInformation("ふつうのこと");
        logger.LogWarning("気になること");

        Assert.Equal("[app log] 気になること" + Environment.NewLine, to.ToString());
        using (logger.BeginScope("範囲"))
        {
        }
    }

    [Fact]
    public void TheShapeSentOverTheBridgeIsNotARecord()
    {
        using var sink = new AppLogSink();
        sink.Append(Entry("なにか"));

        // record は EqualityContract（System.Type）を持っていて CefSharp の bind 越しに渡せない。
        var row = Assert.Single(sink.ForBridge());
        Assert.Null(row.GetType().GetProperty("EqualityContract",
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public));
    }

    [Fact]
    public void GoingThroughTheLoggerKeepsCategoryLevelAndException()
    {
        using var sink = new AppLogSink();
        using var provider = new AppLogProvider(sink);

        var logger = provider.CreateLogger("SpoitableToolKit.Host.Modules.ModuleManager");
        logger.LogError(new InvalidOperationException("だめ"), "読み込みに失敗した {Id}", "hrs");

        var entry = Assert.Single(sink.Recent());

        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("SpoitableToolKit.Host.Modules.ModuleManager", entry.Category);
        Assert.Equal("ModuleManager", entry.ShortCategory);
        Assert.Equal("読み込みに失敗した hrs", entry.Message);
        Assert.Contains("だめ", entry.Exception);
        Assert.Equal(Environment.CurrentManagedThreadId, entry.ThreadId);
    }

    [Fact]
    public void BeginScopeReturnsSomethingReal()
    {
        using var sink = new AppLogSink();
        using var provider = new AppLogProvider(sink);

        var scope = provider.CreateLogger("なにか").BeginScope("スコープ");

        // using (…) { } だと null を返しても素通りする（C# では using (null) が合法）。
        // 実体を返すのは、呼び手が BeginScope(…).Dispose() と直に書けるようにするため（Microsoft のプロバイダも同じ）。
        Assert.NotNull(scope);
        scope.Dispose();
    }

    /// <summary>開いたまま読む。<b>flush されているかを見るのが目的</b>なので閉じてはいけない。</summary>
    private static string ReadWhileOpen(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
