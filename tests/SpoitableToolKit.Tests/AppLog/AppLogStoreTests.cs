using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.AppLog;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.AppLog;

/// <summary>
/// 起動をまたいで「アプリ」の面を読み直すための記録（<c>applog.db</c>）。
/// 起動し直しは「同じパスで開き直して、新しい受け口を作る」で作る（本物の起動と同じ順: 受け口 → 最初の行 → 設定を読んで <see cref="AppLogSink.Restore"/>）。
/// </summary>
public sealed class AppLogStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-10T13:00:00+09:00");

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "stk-applogdb-" + Guid.NewGuid().ToString("N")[..8]);

    public AppLogStoreTests() => Directory.CreateDirectory(_dir);

    private string Database => Path.Combine(_dir, "applog.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static AppLogEntry Entry(string message, DateTimeOffset? at = null) =>
        new(at ?? Now, LogLevel.Information, "SpoitableToolKit.Host.VrcLog.VrcLogBus", message, null, 7);

    private AppLogStore Open() => AppLogStore.TryOpen(Database) ?? throw new InvalidOperationException("開けなかった");

    private async Task RunAsync(long run, Action<AppLogSink> body)
    {
        var store = Open();
        using var sink = new AppLogSink(store: store, run: run);
        body(sink);
        await store.FlushAsync();
    }

    // PRAGMA journal_mode は 1 行（"wal"）を返すので、同じ命令の中で MAX(seq) を ExecuteScalar で読むと "wal" を拾って数に直せず、AppLogStore.TryOpen が黙って null になる。
    [Fact]
    public void AFreshDatabaseOpens()
    {
        using var store = AppLogStore.TryOpen(Database);

        Assert.NotNull(store);
        Assert.Equal(0, store.LastSeq);
    }

    // ログの受け口の内側なので、開けなくても投げない（投げるとアプリが立ち上がらない）。
    [Fact]
    public void AnUnopenablePathGivesNothingInsteadOfThrowing()
    {
        // ディレクトリはデータベースとして開けない。
        Assert.Null(AppLogStore.TryOpen(_dir));
    }

    [Fact]
    public async Task ARestartReadsThePreviousRunBackAndCarriesOnNumbering()
    {
        await RunAsync(111, sink =>
        {
            sink.Append(Entry("前の起動 1"));
            sink.Append(Entry("前の起動 2"));
        });

        using var next = new AppLogSink(store: Open(), run: 222);

        // 設定を読むより前に出た行（本物ではモジュールの読み込みより前）。前の起動の行の後ろに並ぶ。
        next.Append(Entry("この起動 1"));
        next.Restore(DateTimeOffset.MinValue);

        var recent = next.Recent();
        Assert.Equal(["前の起動 1", "前の起動 2", "この起動 1"], recent.Select(e => e.Message));

        // 番号は続きから。1 から振り直すと、画面の重複弾き（seq）に前の起動の行と見分けられない。
        Assert.Equal([1L, 2, 3], recent.Select(e => e.Seq));

        // 起動の印。画面はこれが変わる所に「ここで起動」を挟む。
        Assert.Equal([111L, 111, 222], recent.Select(e => e.Run));
    }

    [Fact]
    public async Task ARowComesBackAsItWasWritten()
    {
        var at = DateTimeOffset.Parse("2026-11-10T13:20:05.123+09:00");
        var written = new AppLogEntry(at, LogLevel.Warning, "SpoitableToolKit.Modules.Hrs.HrsModule", "繋がらない", "System.Exception: わざと", 42);

        await RunAsync(5, sink => sink.Append(written));

        using var store = Open();
        var row = Assert.Single(store.Read(10));

        Assert.Equal(at, row.Timestamp);
        Assert.Equal(TimeSpan.FromHours(9), row.Timestamp.Offset);
        Assert.Equal(LogLevel.Warning, row.Level);
        Assert.Equal("SpoitableToolKit.Modules.Hrs.HrsModule", row.Category);
        Assert.Equal("繋がらない", row.Message);
        Assert.Equal("System.Exception: わざと", row.Exception);
        Assert.Equal(42, row.ThreadId);
        Assert.Equal(1, row.Seq);
        Assert.Equal(5, row.Run);
    }

    [Fact]
    public async Task TheRestoredLedgerKeepsTheNewestRowsUpToItsCapacity()
    {
        await RunAsync(1, sink =>
        {
            for (var i = 0; i < AppLogSink.Capacity + 5; i++) sink.Append(Entry($"前 {i}"));
        });

        using var next = new AppLogSink(store: Open(), run: 2);
        next.Append(Entry("この起動"));
        next.Restore(DateTimeOffset.MinValue);

        var recent = next.Recent();
        Assert.Equal(AppLogSink.Capacity, recent.Count);
        Assert.Equal("前 6", recent[0].Message);
        Assert.Equal("この起動", recent[^1].Message);
    }

    // 消すのはキューの先なので、読む側でも切る。
    [Fact]
    public async Task TheRestoredLedgerLeavesOutRowsPastTheKeepPeriod()
    {
        await RunAsync(1, sink =>
        {
            sink.Append(Entry("40 日前", Now.AddDays(-40)));
            sink.Append(Entry("昨日", Now.AddDays(-1)));
        });

        using var next = new AppLogSink(store: Open(), run: 2);
        next.Restore(Now.AddDays(-30));

        Assert.Equal(["昨日"], next.Recent().Select(e => e.Message));
    }

    [Fact]
    public async Task RestoringTwiceDoesNotDoubleTheRows()
    {
        await RunAsync(1, sink => sink.Append(Entry("前の起動")));

        using var next = new AppLogSink(store: Open(), run: 2);
        next.Restore(DateTimeOffset.MinValue);
        next.Restore(DateTimeOffset.MinValue);

        Assert.Equal(["前の起動"], next.Recent().Select(e => e.Message));
    }

    // 前の版が残した「ここまで消した」の印（app_log_meta の cleared_through）は読まずに表ごと消す。画面の「クリア」は無いので、印より前の行も台帳と遡りに戻る。
    [Fact]
    public async Task AClearMarkFromAnEarlierVersionIsDroppedAndItsRowsComeBack()
    {
        await RunAsync(1, sink =>
        {
            sink.Append(Entry("印より前"));
            sink.Append(Entry("印のあと"));
        });

        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE app_log_meta (key TEXT PRIMARY KEY, value INTEGER NOT NULL);
                INSERT INTO app_log_meta (key, value) VALUES ('cleared_through', 1);
                """;
            command.ExecuteNonQuery();
        }

        using var next = new AppLogSink(store: Open(), run: 2);
        next.Restore(DateTimeOffset.MinValue);

        Assert.Equal(["印より前", "印のあと"], next.Recent().Select(e => e.Message));
        Assert.Equal(["印より前", "印のあと"], next.Older(long.MaxValue, 10).Rows.Select(e => e.Message));

        using var check = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString());
        check.Open();
        using var exists = check.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name = 'app_log_meta'";
        Assert.Equal(0L, (long)exists.ExecuteScalar()!);
    }

    // 下へ辿ると前の起動の行まで続けて出る。1 行多く読んで「まだあるか」を返す（ちょうど使い切ったときに、もう 1 回空振りの問い合わせをさせない）。
    [Fact]
    public async Task GoingBackWalksAcrossRuns()
    {
        await RunAsync(1, sink =>
        {
            for (var i = 1; i <= 5; i++) sink.Append(Entry($"前 {i}"));
        });

        var store = Open();
        using var next = new AppLogSink(store: store, run: 2);
        for (var i = 6; i <= 8; i++) next.Append(Entry($"今 {i}"));

        // この起動の行が記録へ入るのを待つ（遡りは記録から読む）。
        await store.FlushAsync();

        var (rows, more) = next.Older(beforeSeq: 7, limit: 3);
        Assert.Equal([4L, 5, 6], rows.Select(e => e.Seq));
        Assert.True(more);

        (rows, more) = next.Older(beforeSeq: 4, limit: 3);
        Assert.Equal([1L, 2, 3], rows.Select(e => e.Seq));
        Assert.False(more);
    }

    [Fact]
    public async Task PruningDropsRowsOlderThanTheCutoff()
    {
        await RunAsync(1, sink =>
        {
            sink.Append(Entry("40 日前", Now.AddDays(-40)));
            sink.Append(Entry("昨日", Now.AddDays(-1)));
            sink.Prune(Now.AddDays(-30));
        });

        using var store = Open();
        Assert.Equal(["昨日"], store.Read(10).Select(e => e.Message));
    }

    // 設定画面の「適用」は何を変えてもここへ来るので、関係ない設定の変更では消さない。
    [Fact]
    public async Task ChangingTheKeepDaysPrunesRightAway()
    {
        var config = new HostConfig { AppLogKeepDays = 30 };
        var store = Open();
        using var sink = new AppLogSink(store: store, run: 1);
        var retention = new AppLogRetention(config, sink, () => Now);

        sink.Append(Entry("10 日前", Now.AddDays(-10)));
        sink.Append(Entry("昨日", Now.AddDays(-1)));

        config.AppLogKeepDays = 5;
        retention.OnConfigChanged(ConfigTesting.Changed(config, nameof(HostConfig.Theme)));
        await store.FlushAsync();
        Assert.Equal(2, store.Read(10).Count);

        retention.OnConfigChanged(ConfigTesting.Changed(config, nameof(HostConfig.AppLogKeepDays)));
        await store.FlushAsync();
        Assert.Equal(["昨日"], store.Read(10).Select(e => e.Message));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(9999, 365)]
    [InlineData(30, 30)]
    public void TheKeepDaysStayInTheirRange(int written, int used)
    {
        var retention = new AppLogRetention(new HostConfig { AppLogKeepDays = written }, new AppLogSink(), () => Now);

        Assert.Equal(Now.AddDays(-used), retention.Since);
    }
}
