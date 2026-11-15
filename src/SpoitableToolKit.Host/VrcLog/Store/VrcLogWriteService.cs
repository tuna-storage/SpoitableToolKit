using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Events.VrcLog;

namespace SpoitableToolKit.Host.VrcLog.Store;

/// <summary>
/// 全件ログだと 1 セッションで 15,000 行を超えるので、書き込みは Channel に積んで
/// 専用スレッドがトランザクションでまとめて流す。tail のループを I/O で止めないため。
/// 失敗しても即座には捨てない。バッチ単位でリトライし、それでも駄目なら 1 行ずつ試す。
/// 壊れた 1 行のせいで同じバッチの残り全部を失わないため。
/// 書けた行だけを配る（<see cref="SqliteVrcLogStore.Stored"/>）。配るのはコミットが返ってから・リトライの外・待っている側を解放する前。
/// </summary>
internal sealed class VrcLogWriteService : IAsyncDisposable
{
    private const int BatchSize = 512;

    private const int MaxAttempts = 4;

    /// <summary>
    /// payload から <see cref="VrcLogEventParams.Line"/> を落とす。行本文は message カラムに持つので重複させない。
    /// <c>[JsonIgnore]</c> を Abstractions 側に付けると <c>required</c> と衝突するうえ、
    /// API 契約にシリアライズの都合が混ざるので、ここで解決する。
    /// </summary>
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                static typeInfo =>
                {
                    if (!typeof(VrcLogEventParams).IsAssignableFrom(typeInfo.Type)) return;

                    var line = typeInfo.Properties
                        .FirstOrDefault(property => property.Name == nameof(VrcLogEventParams.Line));
                    if (line is not null) typeInfo.Properties.Remove(line);
                },
            },
        },
    };

    private readonly string _connectionString;
    private readonly EventHub _stored;
    private readonly ILogger _logger;
    private readonly Channel<object> _queue = Channel.CreateUnbounded<object>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });
    private readonly Task _writer;
    private readonly CancellationTokenSource _cts = new();

    public VrcLogWriteService(string connectionString, EventHub stored, ILogger logger)
    {
        _connectionString = connectionString;
        _stored = stored;
        _logger = logger;
        _writer = Task.Run(() => WriteLoopAsync(_cts.Token));
    }

    /// <summary>
    /// 書き込み待ちのイベント 1 件。元のイベントも一緒に持つ。
    /// 書けたあとに画面へ流すのは JSON ではなく型のほうで、種別ごとに整えた見た目はそちらからしか作れない（<see cref="SpoitableToolKit.Host.Ui.LogView.LogRow"/>）。
    /// </summary>
    private readonly record struct EventRow(
        VrcLogEventParams Event, string Ts, string Kind, string Payload, string Message, string Source);

    /// <summary>書き込み待ちの生行。イベントと同じで、元の行も一緒に持つ。</summary>
    private readonly record struct LineRow(
        VrcLogLine Line, string Ts, int Level, string? Tag, string Message, string Source);

    /// <summary>1 トランザクションで書けたもの。コミットが返ってからしか入らない。</summary>
    private sealed class Committed
    {
        public List<StoredEvent> Events { get; } = [];

        public List<StoredLine> Lines { get; } = [];

        public bool Any => Events.Count > 0 || Lines.Count > 0;

        public void Clear()
        {
            Events.Clear();
            Lines.Clear();
        }
    }

    private sealed record FlushMarker(TaskCompletionSource Completion);

    public void Append(VrcLogEventParams logEvent)
    {
        // シリアライズは呼び出し側でやる。ここで失敗するものは何度リトライしても失敗するので、
        // 書き込みキューに載せる前に切り離して、DB 起因の失敗（＝リトライする価値がある）と混ぜない。
        string payload;
        try
        {
            payload = JsonSerializer.Serialize(logEvent, logEvent.GetType(), PayloadOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot serialize {Kind}; not stored", logEvent.Kind);
            return;
        }

        _queue.Writer.TryWrite(new EventRow(
            logEvent,
            logEvent.Timestamp.ToString("O"),
            logEvent.Kind,
            payload,
            logEvent.Line.Message,
            logEvent.Line.SourceFile));
    }

    public void Append(VrcLogLine line) => _queue.Writer.TryWrite(new LineRow(
        line,
        line.Timestamp.ToString("O"),
        (int)line.Level,
        line.Tag,
        line.Message,
        line.SourceFile));

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var marker = new FlushMarker(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        if (!_queue.Writer.TryWrite(marker)) return;

        await marker.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var rows = new List<object>(BatchSize);
            var markers = new List<FlushMarker>();
            var committed = new Committed();

            while (await _queue.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Clear();
                markers.Clear();
                committed.Clear();

                while (rows.Count < BatchSize && _queue.Reader.TryRead(out var item))
                {
                    if (item is FlushMarker marker) markers.Add(marker);
                    else rows.Add(item);
                }

                try
                {
                    if (rows.Count > 0)
                        await WriteWithRetryAsync(connection, rows, committed, cancellationToken).ConfigureAwait(false);

                    // **リトライの外で配る。** 中で配ると、購読側が投げた例外がリトライの catch に入り、
                    // コミット済みの行をもう一度書く（同じ行が 2 件になる）。
                    //
                    // **待っている側を解放する前に配る。** 逆にすると FlushAsync が返った時点では
                    // まだ配られておらず、「書けているのに画面には出ていない」瞬間が挟まる。
                    // Publish は自分で全部握り潰すので、ここに置いても下の解放は必ず走る。
                    Publish(committed);
                }
                finally
                {
                    // 書けたかどうかに関わらず待っている側は解放する。失敗で永久に待たせない。
                    foreach (var marker in markers) marker.Completion.TrySetResult();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // ここで死ぬと FlushAsync が永久に待つ。必ず記録する。
            _logger.LogError(ex, "Log writer stopped unexpectedly");
        }
        finally
        {
            // ループを抜けたあとにキューへ残ったマーカーを解放する。
            while (_queue.Reader.TryRead(out var item))
                if (item is FlushMarker orphan)
                    orphan.Completion.TrySetResult();
        }
    }

    /// <summary>
    /// 書けた行を 1 行ずつ配る。<b>1 人が落ちても残りへは配る</b>し（口が受け手ごとに受け止めて、どの行だったかと一緒に記録する）、
    /// 書き込み側へは投げ返さない（投げ返すとリトライへ戻り、コミット済みの行を二重に書く）。
    /// サーフェスは今後増えるので、デスクトップ窓が落ちたせいで VR オーバーレイが止まる、という形にしない。
    /// </summary>
    private void Publish(Committed committed)
    {
        if (!committed.Any) return;

        foreach (var stored in committed.Events)
            _stored.Fire<IStoredLogEventListener>(l => l.OnEventStored(stored), () => $"{stored.Event.Kind}(id={stored.Id})");

        foreach (var stored in committed.Lines)
            _stored.Fire<IStoredLogEventListener>(l => l.OnLineStored(stored), () => $"raw(id={stored.Id})");
    }

    /// <summary>
    /// バッチで書く。失敗したら間を空けて数回やり直し、それでも駄目なら 1 行ずつに落とす。
    /// SQLite のロック競合や一時的な I/O エラーは待てば通ることが多く、
    /// 通らない行があってもそれは 1 行の問題なので、残りを巻き込ませない。
    /// </summary>
    private async Task WriteWithRetryAsync(
        SqliteConnection connection, List<object> rows, Committed committed, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                WriteBatch(connection, rows, committed);
                return;
            }
            catch (Exception ex)
            {
                if (attempt == MaxAttempts)
                {
                    _logger.LogWarning(
                        "Batch of {Count} rows failed {Attempts}x ({Message}); retrying row by row",
                        rows.Count, MaxAttempts, ex.Message);
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50 * (1 << (attempt - 1))), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var single = new List<object>(1);
        var dropped = 0;
        foreach (var row in rows)
        {
            single.Clear();
            single.Add(row);
            try
            {
                // 1 行ずつでも、書けた分だけが committed に足される。捨てた行は画面にも出ない。
                WriteBatch(connection, single, committed);
            }
            catch (Exception ex)
            {
                dropped++;
                _logger.LogWarning("Dropped 1 log row: {Message}", ex.Message);
            }
        }

        if (dropped > 0)
            _logger.LogWarning("Recovered {Recovered}/{Total} rows, dropped {Dropped}",
                rows.Count - dropped, rows.Count, dropped);
    }

    /// <summary>
    /// 1 トランザクション分を書く。書けた行は <paramref name="committed"/> へ足す。
    /// <b>足すのはコミットが返ってから</b> — 途中で足すと、巻き戻った行まで画面へ出る。
    /// </summary>
    private static void WriteBatch(SqliteConnection connection, List<object> rows, Committed committed)
    {
        using var transaction = connection.BeginTransaction();

        using var insertEvent = connection.CreateCommand();
        insertEvent.Transaction = transaction;

        // **RETURNING id** で採番された id をその場で受け取る（同梱 SQLite は 3.53、必要なのは 3.35+）。
        // last_insert_rowid() を別に引くと 1 行につき往復が 2 回になるうえ、
        // 「どの行の id か」を自分で対応づけることになる。
        insertEvent.CommandText =
            "INSERT INTO log_events (ts, kind, payload, message, source_file) " +
            "VALUES ($ts, $kind, $payload, $message, $source) RETURNING id";
        var eventTs = insertEvent.Parameters.Add("$ts", SqliteType.Text);
        var eventKind = insertEvent.Parameters.Add("$kind", SqliteType.Text);
        var eventPayload = insertEvent.Parameters.Add("$payload", SqliteType.Text);
        var eventMessage = insertEvent.Parameters.Add("$message", SqliteType.Text);
        var eventSource = insertEvent.Parameters.Add("$source", SqliteType.Text);

        using var insertLine = connection.CreateCommand();
        insertLine.Transaction = transaction;
        insertLine.CommandText =
            "INSERT INTO log_lines (ts, level, tag, message, source_file) " +
            "VALUES ($ts, $level, $tag, $message, $source) RETURNING id";
        var lineTs = insertLine.Parameters.Add("$ts", SqliteType.Text);
        var lineLevel = insertLine.Parameters.Add("$level", SqliteType.Integer);
        var lineTag = insertLine.Parameters.Add("$tag", SqliteType.Text);
        var lineMessage = insertLine.Parameters.Add("$message", SqliteType.Text);
        var lineSource = insertLine.Parameters.Add("$source", SqliteType.Text);

        // コミットするまでは「書けた」とは言えないので、いったんここに溜める。
        var pendingEvents = new List<StoredEvent>(rows.Count);
        var pendingLines = new List<StoredLine>(rows.Count);

        foreach (var row in rows)
        {
            switch (row)
            {
                case EventRow e:
                    eventTs.Value = e.Ts;
                    eventKind.Value = e.Kind;
                    eventPayload.Value = e.Payload;
                    eventMessage.Value = e.Message;
                    eventSource.Value = e.Source;
                    pendingEvents.Add(new StoredEvent((long)insertEvent.ExecuteScalar()!, e.Event));
                    break;

                case LineRow l:
                    lineTs.Value = l.Ts;
                    lineLevel.Value = l.Level;
                    lineTag.Value = (object?)l.Tag ?? DBNull.Value;
                    lineMessage.Value = l.Message;
                    lineSource.Value = l.Source;
                    pendingLines.Add(new StoredLine((long)insertLine.ExecuteScalar()!, l.Line));
                    break;
            }
        }

        transaction.Commit();
        committed.Events.AddRange(pendingEvents);
        committed.Lines.AddRange(pendingLines);
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();

        // キューに残っている分を書き切ってから畳む。停止時にイベントを落とさない。
        try
        {
            await _writer.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Log writer did not drain in time; remaining rows are lost");
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
    }
}
