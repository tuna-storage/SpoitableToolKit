using Microsoft.Data.Sqlite;
using SpoitableToolKit.Abstractions.VrcLog;

namespace SpoitableToolKit.Host.VrcLog.Store;

/// <summary>読むたびに接続を開く（WAL なので、書く側が書いている間も読める）。</summary>
internal sealed class VrcLogQueryService(string connectionString)
{
    public async Task<IReadOnlyList<StoredLogEvent>> QueryRecentAsync(
        int limit, string? kind = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = kind is null
            ? "SELECT id, ts, kind, payload, message, source_file FROM log_events ORDER BY id DESC LIMIT $limit"
            : "SELECT id, ts, kind, payload, message, source_file FROM log_events WHERE kind = $kind ORDER BY id DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", limit);
        if (kind is not null)
            command.Parameters.AddWithValue("$kind", kind);

        var results = new List<StoredLogEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(ReadEvent(reader));

        return results;
    }

    /// <summary>
    /// 遡りの 1 ページ。新しい順で、<see cref="EventPageQuery.BeforeId"/> より古いものを返す。
    /// <para>
    /// 時刻の比較は <c>strftime('%s', …)</c> で両辺とも通す。
    /// <c>ts</c> はオフセット付きの文字列（<c>…+09:00</c>）なので、文字列のまま比べると
    /// オフセットが揃っている間しか正しくない。<c>strftime</c> はオフセットを解釈する（<c>+09:00</c> と <c>+00:00</c> の差は 32400 秒）。
    /// </para>
    /// <para>
    /// ⚠ 片方を整数にしないこと。SQLite では TEXT は常に INTEGER より大きいので、
    /// <c>strftime('%s', ts) &gt;= 1788345563</c> は常に真になり、絞り込みが黙って効かなくなる。
    /// 両辺とも <c>strftime</c> を通せば TEXT どうしで、10 桁で揃うため文字列比較がそのまま正しい。
    /// 範囲は利用者が明示で指定するものなので、索引が効かない分は許容する。
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<StoredLogEvent>> QueryEventsAsync(
        EventPageQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();

        var where = new List<string>();
        if (query.BeforeId is { } beforeId)
        {
            where.Add("id < $beforeId");
            command.Parameters.AddWithValue("$beforeId", beforeId);
        }

        if (query.From is { } from)
        {
            where.Add("strftime('%s', ts) >= strftime('%s', $from)");
            command.Parameters.AddWithValue("$from", from.ToString("O"));
        }

        if (query.To is { } to)
        {
            where.Add("strftime('%s', ts) <= strftime('%s', $to)");
            command.Parameters.AddWithValue("$to", to.ToString("O"));
        }

        for (var i = 0; i < query.ExcludeKinds.Count; i++)
            command.Parameters.AddWithValue($"$kind{i}", query.ExcludeKinds[i]);

        if (query.ExcludeKinds.Count > 0)
        {
            var names = string.Join(", ", Enumerable.Range(0, query.ExcludeKinds.Count).Select(i => $"$kind{i}"));
            where.Add($"kind NOT IN ({names})");
        }

        command.CommandText =
            "SELECT id, ts, kind, payload, message, source_file FROM log_events"
            + (where.Count == 0 ? "" : " WHERE " + string.Join(" AND ", where))
            + " ORDER BY id DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", Math.Max(1, query.Limit));

        var results = new List<StoredLogEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(ReadEvent(reader));

        return results;
    }

    /// <summary>
    /// ある時間帯の生行。イベントのページに合わせて引く。
    /// <para>
    /// <c>log_events</c> と <c>log_lines</c> は別の通し番号なので、1 本のカーソルではたどれない。
    /// イベントを id で送り、そのページが覆う時間帯の生行をぶら下げる。
    /// 生行は既定では保存していないので、これが要るのは <c>StoreAllLines</c> が入のときだけ。
    /// </para>
    /// <para>
    /// 境目は半開区間（<paramref name="fromInclusive"/> 以上、<paramref name="toExclusive"/> 未満）。
    /// VRChat の時刻は秒までしか無く、流れた瞬間は同じ秒に何行も並ぶので、両端を閉じると継ぎ目の 1 秒が 2 ページに出る。
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<StoredLogLine>> QueryLinesAsync(
        DateTimeOffset fromInclusive, DateTimeOffset? toExclusive, int limit,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, ts, level, tag, message FROM log_lines "
            + "WHERE strftime('%s', ts) >= strftime('%s', $from)"
            + (toExclusive is null ? "" : " AND strftime('%s', ts) < strftime('%s', $to)")
            + " ORDER BY id DESC LIMIT $limit";

        command.Parameters.AddWithValue("$from", fromInclusive.ToString("O"));
        if (toExclusive is { } to) command.Parameters.AddWithValue("$to", to.ToString("O"));
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));

        var results = new List<StoredLogLine>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new StoredLogLine
            {
                Id = reader.GetInt64(0),
                Timestamp = DateTimeOffset.Parse(reader.GetString(1)),
                Level = (VrcLogLevel)reader.GetInt32(2),
                Tag = reader.IsDBNull(3) ? null : reader.GetString(3),
                Message = reader.GetString(4),
            });
        }

        return results;
    }

    private static StoredLogEvent ReadEvent(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Timestamp = DateTimeOffset.Parse(reader.GetString(1)),
        Kind = reader.GetString(2),
        Payload = reader.GetString(3),
        Message = reader.GetString(4),
        SourceFile = reader.IsDBNull(5) ? null : reader.GetString(5),
    };
}
