using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Events.VrcLog;

namespace SpoitableToolKit.Host.VrcLog.Store;

internal sealed class SqliteVrcLogStore : IVrcLogStore, IAsyncDisposable
{
    /// <summary>
    /// 書けた行だけを、DB が採番した id を付けて配る口（<see cref="IStoredLogEventListener"/>。足せるのはアプリ自身だけ）。画面へ出すのはここからだけ。
    /// 書けてから流すので、見えているのに記録に無い行は作れない。
    /// 配るのはコミットが返ってから（トランザクションの中で配ると、巻き戻ったぶんまで画面に出る）。
    /// 生行（<see cref="IStoredLogEventListener.OnLineStored"/>）は <c>StoreAllLines</c> が入のときしか流れない（切のときは保存していないので、<c>logBus</c> から直に流す）。
    /// </summary>
    public IEventSource Stored => _stored.Source;

    private readonly EventHub _stored;
    private readonly string _connectionString;
    private readonly ILogger<SqliteVrcLogStore> _logger;
    private readonly VrcLogWriteService _writes;
    private readonly VrcLogQueryService _reads;

    public SqliteVrcLogStore(string databasePath, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<SqliteVrcLogStore>();
        _stored = EventHubs.HostOnly<IStoredLogEventListener>(_logger);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        // 表を作ってから書き始める（書く口は作った途端に書き込みのスレッドを起こす）。
        Initialize();
        _writes = new VrcLogWriteService(_connectionString, _stored, _logger);
        _reads = new VrcLogQueryService(_connectionString);
    }

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        // WAL にしておくと、書き込み中でも別コネクションから読める。
        command.CommandText = """
            PRAGMA journal_mode=WAL;

            CREATE TABLE IF NOT EXISTS log_events (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                ts          TEXT NOT NULL,
                kind        TEXT NOT NULL,
                payload     TEXT NOT NULL,
                message     TEXT NOT NULL,
                source_file TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_log_events_ts   ON log_events (ts);
            CREATE INDEX IF NOT EXISTS ix_log_events_kind ON log_events (kind, ts);

            CREATE TABLE IF NOT EXISTS log_lines (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                ts          TEXT NOT NULL,
                level       INTEGER NOT NULL,
                tag         TEXT,
                message     TEXT NOT NULL,
                source_file TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_log_lines_ts  ON log_lines (ts);
            CREATE INDEX IF NOT EXISTS ix_log_lines_tag ON log_lines (tag, ts);
            """;
        command.ExecuteNonQuery();
    }

    public void Append(VrcLogEventParams logEvent) => _writes.Append(logEvent);

    public void Append(VrcLogLine line) => _writes.Append(line);

    public Task FlushAsync(CancellationToken cancellationToken = default) => _writes.FlushAsync(cancellationToken);

    public Task<IReadOnlyList<StoredLogEvent>> QueryRecentAsync(
        int limit, string? kind = null, CancellationToken cancellationToken = default) =>
        _reads.QueryRecentAsync(limit, kind, cancellationToken);

    public Task<IReadOnlyList<StoredLogEvent>> QueryEventsAsync(
        EventPageQuery query, CancellationToken cancellationToken = default) =>
        _reads.QueryEventsAsync(query, cancellationToken);

    public Task<IReadOnlyList<StoredLogLine>> QueryLinesAsync(
        DateTimeOffset fromInclusive, DateTimeOffset? toExclusive, int limit,
        CancellationToken cancellationToken = default) =>
        _reads.QueryLinesAsync(fromInclusive, toExclusive, limit, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        // 書く口が残りを書き切ってから、接続の溜めを捨てる。
        await _writes.DisposeAsync().ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }
}
