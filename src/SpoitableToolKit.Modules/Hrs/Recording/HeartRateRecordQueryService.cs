using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Modules.Hrs.Panel;

namespace SpoitableToolKit.Modules.Hrs.Recording;

/// <summary>
/// 記録を読む口: 貯まっている量（<see cref="Summary"/>）と、範囲を読む（統計タブ）。書くのは <see cref="HeartBeatRecorder"/>、書き出しは <see cref="HeartRateExportService"/>。
/// </summary>
internal sealed class HeartRateRecordQueryService(Func<HeartRateRecords?> records, Func<ILogger> logger, Action pushStatus)
{
    /// <summary>貯まっている量。<b>毎拍 DB を数えない</b>ので、聞かれたときに裏で取り直す。</summary>
    public RecordSummary Summary { get; private set; }

    /// <summary>
    /// 貯まっている量を取り直す。<b>DB を数えるので裏で</b>（毎拍やる類の仕事ではない）。
    /// </summary>
    public void Refresh()
    {
        if (records() is not { } store) return;

        _ = Task.Run(async () =>
        {
            try
            {
                Summary = await store.SummaryAsync().ConfigureAwait(false);
                pushStatus();
            }
            catch (Exception ex)
            {
                logger().LogDebug(ex, "Could not read the record count");
            }
        });
    }

    /// <summary>
    /// 貯まっている量を、<b>数え終わるまで待って</b>取り直す（パネルを開いた瞬間の 1 回 — <see cref="HrsPanelStatus.GetAsync"/>）。
    /// 数えられなくても戻る（<b>画面が開かない方が困る</b>）。
    /// </summary>
    public async Task CountAsync()
    {
        if (records() is not { } store) return;

        try
        {
            Summary = await store.SummaryAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 数えられなくても現状は返す。**画面が開かない方が困る。**
            logger().LogDebug(ex, "Could not read the record count");
        }
    }

    /// <summary>
    /// 範囲を読む。<c>{"from":…,"to":…}</c>（UTC のエポックミリ秒）。読めない範囲・逆さの範囲は<b>空で返す</b>
    /// （投げるとパネルの統計タブが開かない。空なら「データなし」が出る）。
    /// <para>
    /// 返す形は <c>{"points":[[t,bpm],…]}</c>。1 日で 8.6 万件になりうるので、
    /// <c>{t,bpm}</c> の組より<b>名前の無い 2 つ組</b>の方が半分ほどで済む。
    /// </para>
    /// </summary>
    public async Task<string> ReadAsync(string payload)
    {
        const string empty = """{"points":[]}""";
        if (records() is not { } store || ReadRange(payload) is not { } range) return empty;
        var (from, to) = range;

        try
        {
            var points = await store.ReadAsync(from, to).ConfigureAwait(false);
            return JsonSerializer.Serialize(new HeartRateRecordPoints(points.Select(p => new[] { p.At, p.Bpm })), HrsPanelJson.Options);
        }
        catch (Exception ex)
        {
            logger().LogWarning(ex, "Could not read the heart rate records from {From} to {To}", from, to);
            return empty;
        }
    }

    /// <summary>
    /// <c>{"from":…,"to":…}</c>（UTC のエポックミリ秒）を読む。どちらかが無い・読めないなら null。
    /// </summary>
    public static (DateTimeOffset From, DateTimeOffset To)? ReadRange(string payload)
    {
        try
        {
            var root = JsonDocument.Parse(payload).RootElement;
            if (!root.TryGetProperty("from", out var from) || !root.TryGetProperty("to", out var to)) return null;

            // 逆さの範囲は弾かない（SQL の BETWEEN が何も返さないので、読んでも書き出しても空になる）。
            return (DateTimeOffset.FromUnixTimeMilliseconds(from.GetInt64()), DateTimeOffset.FromUnixTimeMilliseconds(to.GetInt64()));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
