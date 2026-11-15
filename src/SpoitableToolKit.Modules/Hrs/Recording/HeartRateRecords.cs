using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace SpoitableToolKit.Modules.Hrs.Recording;

/// <summary>
/// 心拍の記録。SQLite 1 ファイル（<c>module-data/hrs/records.db</c>）。
/// <para>
/// DB なので、<b>届いた心拍をそのまま 1 件ずつ入れる</b>。
/// 間引かないので、記録が入口の周期そのままになる（BLE は約 1 秒、時計は約 5 秒）。
/// </para>
/// <para>
/// 書くのは<b>専用スレッド 1 本</b>。<see cref="Write"/> は BLE の通知コールバックから
/// 呼ばれる道にあるので、そこで disk I/O を待つとビートのループごと遅れる。
/// キューに積むだけにして、詰まっても心拍の側は止めない。
/// </para>
/// <para>
/// CSV は<b>欲しいときだけ</b>出す。常時 2 か所へ書くと、
/// どちらが正か分からないものが増える。
/// </para>
/// </summary>
internal sealed class HeartRateRecords : IAsyncDisposable
{
    /// <summary>1 トランザクションにまとめる上限。詰まったときだけ効く（ふつうは 1 件ずつ）。</summary>
    private const int BatchSize = 256;

    /// <summary>バッチを諦めるまでの試行回数。</summary>
    private const int MaxAttempts = 3;

    private readonly string _connectionString;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writer;

    private readonly Channel<object> _queue = Channel.CreateUnbounded<object>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });

    public HeartRateRecords(string databasePath, ILogger logger)
    {
        _logger = logger;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        Initialize();
        _writer = Task.Run(() => WriteLoopAsync(_cts.Token));
    }

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();

        // 時刻は UTC のエポックミリ秒。**数値で持つ**と範囲の切り出しが素直に書けるし、
        // 夏時間や表示形式に引きずられない（人へ見せる形にするのは CSV に出すときだけ）。
        command.CommandText = """
            PRAGMA journal_mode=WAL;

            CREATE TABLE IF NOT EXISTS heart_rate_samples (
                at  INTEGER NOT NULL,
                bpm INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_heart_rate_samples_at ON heart_rate_samples (at);
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 書き込みの直前に挟む。<b>試験からはここで書き込みスレッドを足止めする。</b>
    /// <para>
    /// 「書き出す前に溜まっているぶんを入れる」を確かめたいのに、速い機械では
    /// <b>待たない実装でも書き込みが先回りして通ってしまう</b>（3000 件積んでも勝てなかった）。
    /// 足止めできる場所が無いと、その試験は運で緑になる。
    /// </para>
    /// </summary>
    internal Func<CancellationToken, Task>? BeforeAppend;

    private readonly record struct SampleRow(long At, int Bpm);

    /// <summary>書き込みスレッドがここまで処理し終えたら完了するマーカー。</summary>
    private sealed record FlushMarker(TaskCompletionSource Completion);

    /// <summary>
    /// 1 件積む。<b>BLE のスレッドから呼ばれる。</b>戻るまでに disk へは触らない。
    /// </summary>
    public void Write(int bpm, DateTimeOffset at) =>
        _queue.Writer.TryWrite(new SampleRow(at.ToUnixTimeMilliseconds(), bpm));

    /// <summary>積んだものが全部入るまで待つ。畳むときと、CSV に出す直前に使う。</summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var marker = new FlushMarker(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        if (!_queue.Writer.TryWrite(marker)) return;

        await marker.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>どれだけ貯まっているか。パネルに出す。</summary>
    public async Task<RecordSummary> SummaryAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MIN(at), MAX(at) FROM heart_rate_samples";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return default;

        var count = reader.GetInt64(0);
        if (count == 0) return default;

        return new RecordSummary(
            count,
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2)));
    }

    /// <summary>
    /// 範囲を CSV に出す。<b>先に溜まっているぶんを入れてから読む</b> —
    /// 「いま」までを指定したのに直前の数拍が入っていないのは、出した人には事故に見える。
    /// <para>
    /// 列は <c>timestamp,bpm</c> で、時刻は<b>ローカル時刻の RFC 3339</b>。
    /// DB には UTC で入れてあるが、CSV を開くのは人なので手元の時計に合わせる。
    /// </para>
    /// </summary>
    /// <returns>書いた行数。</returns>
    public async Task<int> ExportAsync(
        DateTimeOffset from, DateTimeOffset to, string path, CancellationToken cancellationToken = default)
    {
        await FlushAsync(cancellationToken).ConfigureAwait(false);

        if (Path.GetDirectoryName(path) is { Length: > 0 } directory) Directory.CreateDirectory(directory);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT at, bpm FROM heart_rate_samples WHERE at BETWEEN $from AND $to ORDER BY at";
        command.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());

        // Excel が既定で読めるよう UTF-8。BOM は付けない（数字と ASCII の日時しか出ないので要らない）。
        await using var file = new StreamWriter(path, append: false, new UTF8Encoding(false));
        await file.WriteLineAsync("timestamp,bpm").ConfigureAwait(false);

        var rows = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var at = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)).ToLocalTime();

            await file.WriteLineAsync(
                    $"{at.ToString("O", CultureInfo.InvariantCulture)},{reader.GetInt32(1)}")
                .ConfigureAwait(false);

            rows++;
        }

        return rows;
    }

    /// <summary>
    /// 範囲の記録を古い順に読む（両端を含む）。統計タブが 1 日ぶんを描くのに使う。
    /// <para>
    /// <b>先に溜まっているぶんを入れてから読む</b>（<see cref="ExportAsync"/> と同じ理由。
    /// 今日を開いたのに直前の数拍が線に無いと、止まって見える）。
    /// </para>
    /// <para>
    /// <b>間引かない。</b> 実データでいちばん多い日が 86,341 件（1 秒に 1 件で丸 1 日）。
    /// 0 bpm（計れていない）も混ぜて返す — 線を切るか数に入れないかは描く側が決める。
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<RecordPoint>> ReadAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        await FlushAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT at, bpm FROM heart_rate_samples WHERE at BETWEEN $from AND $to ORDER BY at";
        command.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());

        var points = new List<RecordPoint>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            points.Add(new RecordPoint(reader.GetInt64(0), reader.GetInt32(1)));

        return points;
    }

    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var rows = new List<SampleRow>(BatchSize);
            var markers = new List<FlushMarker>();

            while (await _queue.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Clear();
                markers.Clear();

                while (rows.Count < BatchSize && _queue.Reader.TryRead(out var item))
                {
                    if (item is SampleRow row) rows.Add(row);
                    else if (item is FlushMarker marker) markers.Add(marker);
                }

                if (rows.Count > 0) await AppendAsync(connection, rows, cancellationToken).ConfigureAwait(false);

                // **待っている人は必ず起こす。** 書けなかったときも起こす（待ち続けるより、
                // 「入っていないかもしれない」まま先へ進めた方がいい）。
                foreach (var marker in markers) marker.Completion.TrySetResult();
            }
        }
        catch (OperationCanceledException)
        {
            // 畳まれた。
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot keep recording heart rate");
        }
        finally
        {
            // 畳まれたあとに Flush を待っている人が居たら起こす。
            while (_queue.Reader.TryRead(out var item))
                if (item is FlushMarker marker)
                    marker.Completion.TrySetResult();
        }
    }

    /// <summary>
    /// まとめて入れる。<b>駄目なら諦めて捨てる</b> —
    /// 心拍は 1 秒後にまた来るので、入らなかった行を抱えたままキューを伸ばす方が損。
    /// </summary>
    private async Task AppendAsync(
        SqliteConnection connection, List<SampleRow> rows, CancellationToken cancellationToken)
    {
        if (BeforeAppend is { } pause) await pause(cancellationToken).ConfigureAwait(false);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);

                await using var command = connection.CreateCommand();
                command.Transaction = transaction as SqliteTransaction;
                command.CommandText = "INSERT INTO heart_rate_samples (at, bpm) VALUES ($at, $bpm)";

                var at = command.Parameters.Add("$at", SqliteType.Integer);
                var bpm = command.Parameters.Add("$bpm", SqliteType.Integer);

                foreach (var row in rows)
                {
                    at.Value = row.At;
                    bpm.Value = row.Bpm;
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt == MaxAttempts)
                {
                    _logger.LogWarning(ex, "Could not record {Count} heart rate row(s); dropping them and moving on", rows.Count);
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 畳む。<b>残りを書いてから止める</b>（最後の数拍を落とすと、記録の終わりが実際より早く見える）。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();

        try
        {
            await _writer.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not drain the record writer");
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        _cts.Dispose();

        // 同じプロセスで開き直せるように、プールが握っているハンドルを離す
        // （試験は 1 プロセスで何度も作り直すので、離さないとファイルが消せない）。
        SqliteConnection.ClearPool(new SqliteConnection(_connectionString));
    }
}
