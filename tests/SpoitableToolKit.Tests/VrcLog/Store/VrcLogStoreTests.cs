using Microsoft.Data.Sqlite;
using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Events.VrcLog;
using SpoitableToolKit.Host.Ui.LogView;
using SpoitableToolKit.Host.VrcLog.Store;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.VrcLog.Store;

// 書けてから配る。保存に失敗して捨てた行が画面に出る（見えているのに記録に無い）ことの無いよう、ここで押さえる。
public sealed class VrcLogStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stk-store", Guid.NewGuid().ToString("N"));

    private string DbPath => Path.Combine(_root, "vrclog.db");

    public VrcLogStoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static VrcLogLine Line(string message, int minutes = 0) => new()
    {
        Timestamp = new DateTimeOffset(2026, 9, 2, 19, 39, 0, TimeSpan.FromHours(9)).AddMinutes(minutes),
        Level = VrcLogLevel.Log,
        Tag = "Behaviour",
        Message = message,
        SourceFile = "output_log_2026-09-02_19-39-01.txt",
    };

    private static PlayerJoined Joined(string name, int minutes = 0) => new()
    {
        Line = Line($"[Behaviour] OnPlayerJoined {name}", minutes),
        DisplayName = name,
        UserId = "usr_00000000-0000-0000-0000-000000000001",
    };

    private sealed class Ear(Action<StoredEvent>? events, Action<StoredLine>? lines) : IStoredLogEventListener
    {
        public void OnEventStored(StoredEvent stored) => events?.Invoke(stored);

        public void OnLineStored(StoredLine stored) => lines?.Invoke(stored);
    }

    private static void Listen(SqliteVrcLogStore store, Action<StoredEvent>? events = null, Action<StoredLine>? lines = null) =>
        store.Stored.AddListener<IStoredLogEventListener>(HostOwner.Instance, new Ear(events, lines));

    // 別コネクションで見る（書いた側の中だけで数えても意味がない）。
    private int CountEvents()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM log_events";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    // id は遡りの継ぎ目（WHERE id < …）と、追いつきの重複を弾く鍵を兼ねるので、増える方向であることまで見る。
    [Fact]
    public async Task AStoredLineIsHandedOutWithItsId()
    {
        await using var store = new SqliteVrcLogStore(DbPath, new Logs());
        var published = new List<StoredEvent>();
        Listen(store, events: published.Add);

        store.Append(Joined("あ"));
        store.Append(Joined("い", minutes: 1));
        store.Append(Joined("う", minutes: 2));
        await store.FlushAsync();

        Assert.Equal(["あ", "い", "う"], published.Select(p => ((PlayerJoined)p.Event).DisplayName));
        Assert.Equal(published.Select(p => p.Id).Order(), published.Select(p => p.Id));
        Assert.All(published, p => Assert.True(p.Id > 0));
    }

    // 書くのは裏のスレッドなので、待たずに畳むと、閉じた時点でキューに残っていた分が記録に残らない。
    [Fact]
    public async Task RowsStillQueuedWhenTheStoreIsClosedAreWritten()
    {
        const int rows = 20_000;
        var store = new SqliteVrcLogStore(DbPath, new Logs());
        for (var i = 0; i < rows; i++) store.Append(Joined($"#{i}"));

        await store.DisposeAsync();

        Assert.Equal(rows, CountEvents());
    }

    // 配ってから書く・トランザクションの中で配る形だと、巻き戻ったぶんまで画面に出る。「画面に出た行は必ず記録にある」はこの順番だけで成り立つ。
    [Fact]
    public async Task ItIsReadableAsSoonAsItIsHandedOut()
    {
        await using var store = new SqliteVrcLogStore(DbPath, new Logs());

        var visible = new List<bool>();
        Listen(store, events: _ => visible.Add(CountEvents() > 0));

        store.Append(Joined("あ"));
        await store.FlushAsync();

        Assert.Equal([true], visible);
    }

    // 配るのを書き込みのリトライの内側でやると、購読側の例外がリトライの catch に入り、コミット済みの行をもう一度書く。
    [Fact]
    public async Task AFailingSubscriberDoesNotCauseADuplicate()
    {
        var logs = new Logs();
        await using var store = new SqliteVrcLogStore(DbPath, logs);

        var calls = 0;
        Listen(store, events: _ =>
        {
            calls++;
            throw new InvalidOperationException("画面側が落ちた");
        });

        store.Append(Joined("あ"));
        await store.FlushAsync();

        Assert.Equal(1, CountEvents());
        Assert.Equal(1, calls);

        // 黙って握り潰さない。画面へ出せなかったこと自体は記録に残す。
        Assert.Contains(logs.Warnings, w => w.Contains("PlayerJoined"));
    }

    [Fact]
    public async Task AFailingSubscriberDoesNotStopTheOthers()
    {
        await using var store = new SqliteVrcLogStore(DbPath, new Logs());

        Listen(store, events: _ => throw new InvalidOperationException("boom"));
        var reached = new List<long>();
        Listen(store, events: stored => reached.Add(stored.Id));

        store.Append(Joined("あ"));
        await store.FlushAsync();

        Assert.Single(reached);
    }

    [Fact]
    public async Task ALineThatCouldNotBeStoredIsNotHandedOut()
    {
        var logs = new Logs();
        await using var store = new SqliteVrcLogStore(DbPath, logs);

        var published = new List<StoredEvent>();
        Listen(store, events: published.Add);

        store.Append(new Unserializable { Line = Line("壊れたイベント") });
        store.Append(Joined("あ"));
        await store.FlushAsync();

        Assert.Equal(1, CountEvents());
        var only = Assert.Single(published);
        Assert.IsType<PlayerJoined>(only.Event);
        Assert.NotEmpty(logs.Errors);
    }

    // 戻せないと、遡ったぶんだけ [Behaviour] OnPlayerJoined … のような生テキストが並ぶ。
    [Fact]
    public async Task AStoredLineRebuildsIntoTheSameRow()
    {
        await using var store = new SqliteVrcLogStore(DbPath, new Logs());

        var live = new WorldJoined
        {
            Line = Line("[Behaviour] Joining wrld_1:12345"),
            WorldId = "wrld_1",
            WorldName = "とあるワールド",
            InstanceName = "12345",
            AccessType = "friends",
            Region = "jp",
            RawInstance = "12345~friends(usr_1)~region(jp)",
        };

        store.Append(live);
        await store.FlushAsync();

        var row = Assert.Single(await store.QueryRecentAsync(10));
        var rebuilt = StoredEventCodec.Rebuild(row, new Logs().CreateLogger("t"));

        Assert.NotNull(rebuilt);
        Assert.Equal(live.Timestamp, rebuilt.Timestamp);

        // 見た目で突き合わせる（フィールドを 1 つずつ比べても、画面に出るのは組み立てた後なので）。
        Assert.Equal(
            LogRow.From(row.Id, live).ToString(),
            LogRow.From(row.Id, rebuilt).ToString());
    }

    // DB は実行ファイルより長生きする。種別の名前を変えれば古い行が読めなくなるし、新しい種別を書いた DB を古いビルドで開くこともある。
    [Fact]
    public void AnUnknownKindIsShownRaw()
    {
        var row = new StoredLogEvent
        {
            Id = 1,
            Timestamp = DateTimeOffset.UnixEpoch,
            Kind = "SomethingFromTheFuture",
            Payload = """{"Whatever":1}""",
            Message = "[Behaviour] 未来の行",
        };

        Assert.Null(StoredEventCodec.Rebuild(row, new Logs().CreateLogger("t")));
        Assert.Contains("未来の行", LogRow.Flat(row).ToString());
    }

    [Fact]
    public void ABrokenPayloadIsNotFatal()
    {
        var logs = new Logs();
        var row = new StoredLogEvent
        {
            Id = 1,
            Timestamp = DateTimeOffset.UnixEpoch,
            Kind = nameof(PlayerJoined),
            Payload = "{ これは JSON ではない",
            Message = "[Behaviour] OnPlayerJoined だれか",
        };

        Assert.Null(StoredEventCodec.Rebuild(row, logs.CreateLogger("t")));
        Assert.NotEmpty(logs.Warnings);
    }

    [Fact]
    public async Task PagingBackHasNoDuplicatesAndNoGaps()
    {
        await using var store = new SqliteVrcLogStore(DbPath, new Logs());

        for (var i = 0; i < 25; i++) store.Append(Joined($"user-{i:D2}", minutes: i));
        await store.FlushAsync();

        var seen = new List<long>();
        long? before = null;

        for (var page = 0; page < 10; page++)
        {
            var rows = await store.QueryEventsAsync(new EventPageQuery { Limit = 7, BeforeId = before });
            if (rows.Count == 0) break;

            seen.AddRange(rows.Select(row => row.Id));
            before = rows[^1].Id;
        }

        Assert.Equal(25, seen.Count);
        Assert.Equal(seen.Distinct().Count(), seen.Count);
        Assert.Equal(seen.OrderByDescending(id => id), seen);
    }

    // SQLite では TEXT は常に INTEGER より大きいので、strftime('%s', ts) >= 1788345563 のように片方を整数にすると常に真になり、絞り込みが黙って効かなくなる。
    // 両辺とも文字列で比べられていることを見る。
    [Fact]
    public async Task RowsCanBeNarrowedByTimeRange()
    {
        await using var store = new SqliteVrcLogStore(DbPath, new Logs());

        for (var i = 0; i < 10; i++) store.Append(Joined($"user-{i:D2}", minutes: i));
        await store.FlushAsync();

        var baseline = Line("x").Timestamp;

        var since = await store.QueryEventsAsync(new EventPageQuery { Limit = 100, From = baseline.AddMinutes(5) });
        Assert.Equal(5, since.Count);
        Assert.All(since, row => Assert.True(row.Timestamp >= baseline.AddMinutes(5)));

        var until = await store.QueryEventsAsync(new EventPageQuery { Limit = 100, To = baseline.AddMinutes(2) });
        Assert.Equal(3, until.Count);

        var window = await store.QueryEventsAsync(new EventPageQuery
        {
            Limit = 100,
            From = baseline.AddMinutes(3),
            To = baseline.AddMinutes(6),
        });
        Assert.Equal(4, window.Count);
    }

    // ts はオフセット付きの文字列なので、文字列のまま比べると揃っている間しか合わない（strftime は解釈する）。
    [Fact]
    public async Task TheRangeHoldsAcrossTimeZones()
    {
        await using var store = new SqliteVrcLogStore(DbPath, new Logs());

        store.Append(Joined("jst"));
        await store.FlushAsync();

        var jst = Line("x").Timestamp;
        var sameMomentInUtc = jst.ToUniversalTime();

        // 同じ瞬間なので、そこから先を求めれば入る。文字列比較だと "10:39" < "19:39" で外れる。
        var rows = await store.QueryEventsAsync(new EventPageQuery { Limit = 10, From = sameMomentInUtc });
        Assert.Single(rows);

        var after = await store.QueryEventsAsync(new EventPageQuery { Limit = 10, From = sameMomentInUtc.AddSeconds(1) });
        Assert.Empty(after);
    }

    // 除かないと、実データでは 92% が ApiRequest なので 100 件取っても見える行が数件にしかならない。
    [Fact]
    public async Task CollapsedKindsAreExcludedInSql()
    {
        await using var store = new SqliteVrcLogStore(DbPath, new Logs());

        for (var i = 0; i < 20; i++)
        {
            store.Append(new ApiRequest
            {
                Line = Line($"[API] Requesting Get thing/{i}", i),
                Method = "Get",
                Endpoint = $"thing/{i}",
            });
        }

        store.Append(Joined("見たい人", minutes: 21));
        await store.FlushAsync();

        var rows = await store.QueryEventsAsync(new EventPageQuery { Limit = 5, ExcludeKinds = [nameof(ApiRequest)] });

        var only = Assert.Single(rows);
        Assert.Equal(nameof(PlayerJoined), only.Kind);
    }

    // VRChat の時刻は秒までしか無く、流れた瞬間は同じ秒に何行も並ぶ。両端を閉じると、継ぎ目の 1 秒ぶんが 2 ページに出る。
    [Fact]
    public async Task TheRawLineSeamDoesNotRepeatTheSameSecond()
    {
        await using var store = new SqliteVrcLogStore(DbPath, new Logs());

        for (var i = 0; i < 3; i++) store.Append(RawLine($"境目 {i}", seconds: 10));
        for (var i = 0; i < 2; i++) store.Append(RawLine($"ひとつ前 {i}", seconds: 9));
        await store.FlushAsync();

        var boundary = Line("x").Timestamp.AddSeconds(10);

        var newer = await store.QueryLinesAsync(boundary, toExclusive: null, limit: 50);
        var older = await store.QueryLinesAsync(boundary.AddSeconds(-1), toExclusive: boundary, limit: 50);

        Assert.Equal(3, newer.Count);
        Assert.Equal(2, older.Count);

        var ids = newer.Concat(older).Select(line => line.Id).ToArray();
        Assert.Equal(5, ids.Distinct().Count());
    }

    // log_events とは別の通し番号である点まで見る。
    [Fact]
    public async Task RawLinesAreAlsoHandedOutWithIdsAfterStoring()
    {
        await using var store = new SqliteVrcLogStore(DbPath, new Logs());

        var lines = new List<StoredLine>();
        var events = new List<StoredEvent>();
        Listen(store, events: events.Add, lines: lines.Add);

        store.Append(Joined("だれか"));
        store.Append(RawLine("生の行", seconds: 1));
        await store.FlushAsync();

        var line = Assert.Single(lines);
        var logEvent = Assert.Single(events);

        Assert.Equal("生の行", line.Line.Message);
        Assert.True(line.Id > 0);

        // 別の表なので、たまたま同じ番号から始まる。同じ空間だと思って混ぜないこと。
        Assert.Equal(logEvent.Id, line.Id);
    }

    [Fact]
    public async Task WithStoringOnRawLinesComeBackWhenPagingToo()
    {
        await using var store = new SqliteVrcLogStore(DbPath, new Logs());

        for (var i = 0; i < 6; i++)
        {
            store.Append(Joined($"user-{i}", minutes: i));
            store.Append(RawLine($"生行 {i}", seconds: i * 60 + 30));
        }

        await store.FlushAsync();

        var page = await LogPage.BuildAsync(
            store,
            new LogPageRequest { Limit = 100, IncludeLines = true },
            maxRows: 5000,
            new Logs().CreateLogger("t"));

        var rows = Rows(page);

        Assert.Equal(6, rows.Count(row => Field(row, "type") is "event"));
        Assert.Equal(6, rows.Count(row => Field(row, "type") is "line"));

        var stamps = rows.Select(row => (string)Field(row, "ts")!).ToArray();
        Assert.Equal(stamps.OrderByDescending(x => x, StringComparer.Ordinal), stamps);
    }

    // log_events と log_lines は別の通し番号なので、1 本のカーソルではたどれない。呼び手が渡す上端（<see cref="LogPageRequest.LinesBefore"/>）を無視すると、前のページと同じ生行がもう一度出る。
    [Fact]
    public async Task PagingBackWithRawLinesHasNoDuplicatesAndNoGaps()
    {
        await using var store = new SqliteVrcLogStore(DbPath, new Logs());

        for (var i = 0; i < 12; i++)
        {
            store.Append(Joined($"user-{i:D2}", minutes: i));
            store.Append(RawLine($"生行 {i:D2}", seconds: i * 60 + 30));
        }

        await store.FlushAsync();

        var logger = new Logs().CreateLogger("t");
        var seenLines = new List<string>();
        long? beforeId = null;
        DateTimeOffset? linesBefore = null;

        for (var pageNumber = 0; pageNumber < 6; pageNumber++)
        {
            var page = await LogPage.BuildAsync(
                store,
                new LogPageRequest { Limit = 4, IncludeLines = true, BeforeId = beforeId, LinesBefore = linesBefore },
                maxRows: 5000,
                logger);

            var rows = Rows(page);
            if (rows.Length == 0) break;

            seenLines.AddRange(rows
                .Where(row => Field(row, "type") is "line")
                .Select(row => (string)Field(row, "message")!));

            var oldestId = Field(page, "oldestId");
            if (oldestId is null) break;

            beforeId = (long)oldestId;
            linesBefore = DateTimeOffset.Parse((string)Field(page, "oldestTs")!);
        }

        Assert.Equal(12, seenLines.Count);
        Assert.Equal(12, seenLines.Distinct().Count());
    }

    // 既定の JSON 設定は大文字小文字を区別するので、{"limit":3} は Limit に入らず既定の 500 のまま黙って動く。JS の camelCase がちゃんと入ることを見る。
    [Fact]
    public void ARequestIsReadAsCamelCaseAndFallsBackWhenBroken()
    {
        var logs = new Logs();

        var parsed = LogPage.Parse("""{"limit":3,"includeLines":true,"excludeKinds":["ApiRequest"]}""", logs.CreateLogger("t"));
        Assert.Equal(3, parsed.Limit);
        Assert.True(parsed.IncludeLines);
        Assert.Equal(["ApiRequest"], parsed.ExcludeKinds);

        var broken = LogPage.Parse("{ これは JSON ではない", logs.CreateLogger("t"));
        Assert.Equal(500, broken.Limit);
        Assert.NotEmpty(logs.Warnings);
    }

    /// <summary>匿名型のまま返るので、名前で引く。</summary>
    private static object? Field(object payload, string name) =>
        payload.GetType().GetProperty(name)?.GetValue(payload);

    private static object[] Rows(object page) => (object[])Field(page, "rows")!;

    private static VrcLogLine RawLine(string message, int seconds) => new()
    {
        Timestamp = new DateTimeOffset(2026, 9, 2, 19, 39, 0, TimeSpan.FromHours(9)).AddSeconds(seconds),
        Level = VrcLogLevel.Log,
        Tag = null,
        Message = message,
        SourceFile = "output_log_2026-09-02_19-39-01.txt",
    };

    private sealed record Unserializable : VrcLogEventParams
    {
        public string Boom => throw new InvalidOperationException("シリアライズできない");
    }
}
