using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Ui.Shell;
using SpoitableToolKit.Host.VrcLog.Store;

namespace SpoitableToolKit.Host.Ui.LogView;

/// <summary>
/// 記録から画面 1 ページ分を組み立てる。<see cref="UiBridge"/> から出してあるのは、token と CefSharp の束縛が要る場所でしか試験できなくなるのを避けるため。
/// 遡りの継ぎ目は <c>log_events</c> の id で送り、生行はそのページが覆う時間帯で引く（2 つの表は別の通し番号なので、1 本のカーソルではたどれない）。
/// </summary>
internal static class LogPage
{
    private static readonly JsonSerializerOptions RequestOptions = new(JsonSerializerDefaults.Web);

    /// <summary>読めない注文は既定に倒す。</summary>
    public static LogPageRequest Parse(string requestJson, ILogger logger)
    {
        try
        {
            return JsonSerializer.Deserialize<LogPageRequest>(requestJson, RequestOptions) ?? new LogPageRequest();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Could not read the log page request");
            return new LogPageRequest();
        }
    }

    public static async Task<object> BuildAsync(
        SqliteVrcLogStore store, LogPageRequest request, int maxRows, ILogger logger)
    {
        var limit = Math.Clamp(request.Limit, 1, maxRows);

        // 1 件多く引いて、続きがあるかを見る。ちょうど limit 件だったかで判定すると、最後のページの次に空のページを 1 回取りに行く。
        var events = await store.QueryEventsAsync(new EventPageQuery
        {
            Limit = limit + 1,
            BeforeId = request.BeforeId,
            ExcludeKinds = request.ExcludeKinds,
            From = request.From,
            To = request.To,
        }).ConfigureAwait(false);

        var hasMore = events.Count > limit;
        if (hasMore) events = [.. events.Take(limit)];

        // 並べ替えの鍵は時刻。同じ秒に何行も並ぶので、そこは種類 → id で決める（表と生行のあいだの 1 秒以内の順序は復元できない）。
        var ordered = new List<(DateTimeOffset Ts, int Rank, long Id, object Row)>(events.Count);

        foreach (var row in events)
        {
            ordered.Add((
                row.Timestamp,
                0,
                row.Id,
                StoredEventCodec.Rebuild(row, logger) is { } logEvent
                    ? LogRow.From(row.Id, logEvent)
                    : LogRow.Flat(row)));
        }

        if (request.IncludeLines && events.Count > 0)
        {
            var lines = await store
                .QueryLinesAsync(events[^1].Timestamp, request.LinesBefore ?? request.To, limit)
                .ConfigureAwait(false);

            foreach (var line in lines)
                ordered.Add((line.Timestamp, 1, line.Id, LogRow.FromLine(line)));
        }

        ordered.Sort((a, b) =>
        {
            var byTime = b.Ts.CompareTo(a.Ts);
            if (byTime != 0) return byTime;

            var byRank = a.Rank.CompareTo(b.Rank);
            return byRank != 0 ? byRank : b.Id.CompareTo(a.Id);
        });

        return new
        {
            rows = ordered.Select(entry => entry.Row).ToArray(),
            oldestId = events.Count > 0 ? events[^1].Id : (long?)null,
            oldestTs = events.Count > 0 ? events[^1].Timestamp.ToString("O") : null,
            hasMore,
        };
    }
}
