using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace SpoitableToolKit.Host.AppLog;

/// <summary>
/// アプリログの記録。SQLite 1 ファイル（<c>applog.db</c>）。起動をまたいで遡るためのもので、ファイル（<c>applog/</c>）は人に送る用としてそのまま残す。
/// <para>
/// ここは例外を投げないし、<c>ILogger</c> も呼ばない。ログの受け口の内側なので、呼ぶと自分へ戻ってくる
/// （<see cref="AppLogFile"/> と同じ約束。報告は <see cref="Console.Error"/> へ）。
/// 開けなければ <see cref="TryOpen"/> が <c>null</c> を返し、アプリは記録なしで動く。
/// </para>
/// <para>
/// 書くのは読み手 1 本のキューの先。<c>ILogger</c> はどのスレッドからでも呼ばれ（CEF の UI スレッドも出す）、そこで disk を待つと出した側が止まる。
/// 落ちる直前の数行はキューに残ったまま消えうるが、落ちたときに残る方はファイルが受け持つ（1 行ごとに flush している。<see cref="AppLogFile"/>）。
/// </para>
/// <para>
/// 同じデータディレクトリで 2 つ同時に起動すると、番号が同じ所から振られて片方の行が上書きされる。
/// 2 つ同時の起動は VRChat のログの記録も二重になる形なので、ここでは守らない。
/// </para>
/// </summary>
internal sealed class AppLogStore : IDisposable
{
    private const int BatchSize = 256;

    private readonly string _connectionString;
    private readonly Channel<object> _queue = Channel.CreateUnbounded<object>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _writer;
    private bool _readFailed;

    /// <summary>開いたときの最後の通し番号。この起動の番号は、ここの次から振る。</summary>
    public long LastSeq { get; }

    private AppLogStore(string connectionString)
    {
        _connectionString = connectionString;

        using var connection = Open();

        using (var schema = connection.CreateCommand())
        {
            // 時刻はエポックミリ秒と、出たときのオフセット（分）。ローカルの時刻を壊さずに戻すため（画面は「出た機械の時計」で読む）。
            // WAL にしておくと、書いている最中でも別の繋ぎから読める（遡りは画面のスレッドから読む）。
            schema.CommandText = """
                PRAGMA journal_mode=WAL;

                CREATE TABLE IF NOT EXISTS app_log (
                    seq       INTEGER PRIMARY KEY,
                    run       INTEGER NOT NULL,
                    at        INTEGER NOT NULL,
                    offset    INTEGER NOT NULL,
                    level     INTEGER NOT NULL,
                    category  TEXT    NOT NULL,
                    message   TEXT    NOT NULL,
                    exception TEXT,
                    thread    INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_app_log_at ON app_log (at);

                DROP TABLE IF EXISTS app_log_meta;
                """;
            schema.ExecuteNonQuery();
        }

        // PRAGMA と同じ命令に混ぜない（journal_mode は 1 行 "wal" を返し、ExecuteScalar がその値を数に直せず落ちる）。
        LastSeq = Scalar(connection, "SELECT MAX(seq) FROM app_log");

        _writer = Task.Run(WriteLoopAsync);
    }

    /// <summary>開く。開けなければ <c>null</c>（理由は標準エラーへ）。</summary>
    public static AppLogStore? TryOpen(string databasePath)
    {
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();

            return new AppLogStore(connectionString);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not open the app log database {databasePath}; the app log will not survive a restart: {ex}");
            return null;
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() is long value ? value : 0;
    }

    /// <summary>1 行積む。戻るまでに disk へは触らない。</summary>
    public void Write(AppLogEntry entry) => _queue.Writer.TryWrite(entry);

    /// <summary>新しい方から <paramref name="limit"/> 行を、古い順で（台帳と同じ並び）。</summary>
    /// <param name="since">渡すと、その時刻より後だけ（消すのはキューの先なので、読む側でも切る）。</param>
    public IReadOnlyList<AppLogEntry> Read(int limit, long? beforeSeq = null, DateTimeOffset? since = null)
    {
        if (limit <= 0) return [];

        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT seq, run, at, offset, level, category, message, exception, thread
                FROM app_log
                WHERE ($before IS NULL OR seq < $before)
                  AND ($since IS NULL OR at >= $since)
                ORDER BY seq DESC
                LIMIT $limit
                """;
            command.Parameters.AddWithValue("$before", (object?)beforeSeq ?? DBNull.Value);
            command.Parameters.AddWithValue("$since", (object?)since?.ToUnixTimeMilliseconds() ?? DBNull.Value);
            command.Parameters.AddWithValue("$limit", limit);

            var rows = new List<AppLogEntry>(Math.Min(limit, 1024));
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var offset = TimeSpan.FromMinutes(reader.GetInt32(3));
                var at = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2)).ToOffset(offset);

                rows.Add(new AppLogEntry(
                    at,
                    (LogLevel)reader.GetInt32(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetInt32(8),
                    reader.GetInt64(0))
                {
                    Run = reader.GetInt64(1),
                });
            }

            rows.Reverse();
            return rows;
        }
        catch (Exception ex)
        {
            // 読めなかったことは書き込みを止める理由にしない（読みは画面のスレッドからで、書き込みとは別の繋ぎ）。
            // 報告は 1 回だけ（遡るたびに標準エラーが埋まる）。
            if (!_readFailed)
            {
                _readFailed = true;
                Console.Error.WriteLine($"Could not read the app log database: {ex}");
            }

            return [];
        }
    }

    public void Prune(DateTimeOffset olderThan) => _queue.Writer.TryWrite(new PruneBefore(olderThan.ToUnixTimeMilliseconds()));

    public Task FlushAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _queue.Writer.TryWrite(new Flush(done)) ? done.Task : Task.CompletedTask;
    }

    private sealed record PruneBefore(long At);

    private sealed record Flush(TaskCompletionSource Done);

    private async Task WriteLoopAsync()
    {
        try
        {
            using var connection = Open();
            var batch = new List<object>(BatchSize);

            while (await _queue.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                batch.Clear();
                while (batch.Count < BatchSize && _queue.Reader.TryRead(out var item)) batch.Add(item);

                Apply(connection, batch);

                foreach (var flush in batch.OfType<Flush>()) flush.Done.TrySetResult();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"The app log database stopped working; the app log will not survive a restart: {ex}");
        }
        finally
        {
            // 待っている人を置き去りにしない（書けなくなったあとに積まれた分）。
            while (_queue.Reader.TryRead(out var item))
                if (item is Flush flush) flush.Done.TrySetResult();
        }
    }

    /// <summary>まとめて 1 回の transaction で。駄目なら捨てて先へ進む（溜めたまま伸ばすより損が小さい）。</summary>
    private static void Apply(SqliteConnection connection, List<object> batch)
    {
        try
        {
            using var transaction = connection.BeginTransaction();

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT OR REPLACE INTO app_log (seq, run, at, offset, level, category, message, exception, thread)
                VALUES ($seq, $run, $at, $offset, $level, $category, $message, $exception, $thread)
                """;
            var seq = insert.Parameters.Add("$seq", SqliteType.Integer);
            var run = insert.Parameters.Add("$run", SqliteType.Integer);
            var at = insert.Parameters.Add("$at", SqliteType.Integer);
            var offset = insert.Parameters.Add("$offset", SqliteType.Integer);
            var level = insert.Parameters.Add("$level", SqliteType.Integer);
            var category = insert.Parameters.Add("$category", SqliteType.Text);
            var message = insert.Parameters.Add("$message", SqliteType.Text);
            var exception = insert.Parameters.Add("$exception", SqliteType.Text);
            var thread = insert.Parameters.Add("$thread", SqliteType.Integer);

            foreach (var item in batch)
            {
                switch (item)
                {
                    case AppLogEntry entry:
                        seq.Value = entry.Seq;
                        run.Value = entry.Run;
                        at.Value = entry.Timestamp.ToUnixTimeMilliseconds();
                        offset.Value = (int)entry.Timestamp.Offset.TotalMinutes;
                        level.Value = (int)entry.Level;
                        category.Value = entry.Category;
                        message.Value = entry.Message;
                        exception.Value = (object?)entry.Exception ?? DBNull.Value;
                        thread.Value = entry.ThreadId;
                        insert.ExecuteNonQuery();
                        break;

                    case PruneBefore prune:
                        using (var command = connection.CreateCommand())
                        {
                            command.Transaction = transaction;
                            command.CommandText = "DELETE FROM app_log WHERE at < $before";
                            command.Parameters.AddWithValue("$before", prune.At);
                            command.ExecuteNonQuery();
                        }

                        break;
                }
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not record {batch.Count} app log item(s); dropping them: {ex.Message}");
        }
    }

    /// <summary>積んだ分を書き切ってから閉じる。待つのは 3 秒まで（閉じるのを止めない）。</summary>
    public void Dispose()
    {
        _queue.Writer.TryComplete();

        try
        {
            _writer.Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not drain the app log database: {ex.Message}");
        }

        // 繋ぎの溜めを手放す（残すとファイルが掴まれたままで、試験の後片付けで消せない）。
        SqliteConnection.ClearPool(new SqliteConnection(_connectionString));
    }
}
