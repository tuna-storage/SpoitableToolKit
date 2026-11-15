using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.Ui.Shell;
using SpoitableToolKit.Host.VrcLog.Store;

namespace SpoitableToolKit.Host.Ui.LogView;

/// <summary>
/// ログ画面の 1 行を JS に渡す形へ。作る場所は 1 つだけにする: 流れてくる行（<see cref="UiPump"/>）と DB から遡った行（<see cref="UiBridge.GetRecentEvents"/>）は同じ一覧に並ぶので、2 箇所で組み立てると同じイベントが場所によって違う見た目になる。
/// 型をそのまま流さないのは、フィールド名の変更が黙って UI を壊すため。
/// </summary>
/// </summary>
internal static class LogRow
{
    /// <summary>行の種類。遡ると 1 本の配列にイベントと生行が混ざって届くので、受け取った側が描き分けられるように印を付ける。流れてくる行にも同じ印を付けて、経路によって形が違わないようにする。</summary>
    public const string Event = "event";

    public const string Line = "line";

    /// <summary>時刻は ISO のまま渡して、表示の形は JS が決める。遡ると何日も前の行が並ぶので、日付を出すかどうかは行ごとに変わる（日付が無いと、8/30 の 16:17 と今日の 16:17 が見分けられない）。</summary>
    public static object From(StoredEvent stored) => From(stored.Id, stored.Event);

    public static object From(long id, VrcLogEventParams logEvent)
    {
        var ts = logEvent.Timestamp.ToString("O");
        return logEvent switch
        {
            WorldJoined w => new
            {
                id,
                type = Event,
                kind = w.Kind,
                ts,
                title = w.WorldName ?? w.WorldId,
                detail = $"{w.InstanceName}{(w.AccessType is null ? "" : $" / {w.AccessType}")}{(w.Region is null ? "" : $" / {w.Region}")}",

                // 名前が取れると id が本文から消える（title が名前に倒れる）。同じワールドの別インスタンスを後から突き合わせられるよう、id は必ず残す。
                fields = Fields(
                    ("host.log.field.worldId", w.WorldId),
                    ("host.log.field.owner", w.OwnerId)),
            },

            // 表示名は変わる。id は変わらない。「あの人いつ来たっけ」を後から辿れるのは id の方。
            PlayerJoined p => new
            {
                id, type = Event, kind = p.Kind, ts, title = p.DisplayName, detail = "",
                fields = Fields(("host.log.field.userId", p.UserId)),
            },
            PlayerLeft p => new
            {
                id, type = Event, kind = p.Kind, ts, title = p.DisplayName, detail = "",
                fields = Fields(("host.log.field.userId", p.UserId)),
            },
            VideoPlaybackFailed v => new { id, type = Event, kind = v.Kind, ts, title = v.Url, detail = v.Reason ?? "" },
            VideoPlaybackStarted v => new
            {
                id,
                type = Event,
                kind = v.Kind,
                ts,
                title = v.Url,
                detail = v.Width is null ? v.PlaybackPath ?? "" : $"{v.PlaybackPath} {v.Width}x{v.Height}",
            },
            VideoResolved v => new { id, type = Event, kind = v.Kind, ts, title = v.Url, detail = "" },
            ApiRequest a => new { id, type = Event, kind = a.Kind, ts, title = $"{a.Method} {a.Endpoint}", detail = "" },
            _ => new { id, type = Event, kind = logEvent.Kind, ts, title = logEvent.Line.Message, detail = "" },
        };
    }

    /// <summary>
    /// 開いたときにだけ出す項目。種別ごとに数も並びも違うので、画面が種別を知らずに済むようここで並べ切ってから渡す。
    /// 空のものは落とす。<c>UserId</c> は VRChat のバージョンによっては行に無く、<c>OwnerId</c> は public インスタンスでは付かない — 空の項目が並ぶくらいなら行ごと無い方が読みやすい。
    /// </summary>
    private static object[] Fields(params (string Label, string? Value)[] pairs) =>
    [
        .. pairs.Where(pair => !string.IsNullOrEmpty(pair.Value))
            .Select(pair => new { label = pair.Label, value = pair.Value }),
    ];

    /// <summary>生行 1 件。種別は <c>raw</c> に揃える（イベントと同じチップで畳めるように）。id は <c>log_lines</c> のもので、<c>log_events</c> とは別の通し番号。</summary>
    public static object FromLine(StoredLine stored) => FromLine(
        stored.Id, stored.Line.Timestamp, stored.Line.Level.ToString(), stored.Line.Tag, stored.Line.Message);

    public static object FromLine(StoredLogLine row) =>
        FromLine(row.Id, row.Timestamp, row.Level.ToString(), row.Tag, row.Message);

    public static object FromLine(
        long? id, DateTimeOffset timestamp, string level, string? tag, string message) => new
    {
        id,
        type = Line,
        kind = "raw",
        ts = timestamp.ToString("O"),
        level,
        tag,

        // 長い行はここで切る。開いても全部は出ないが、1 行に数 KB 入ることがある。
        message = message.Length > 400 ? message[..400] + "…" : message,
    };

    public static object Flat(StoredLogEvent row) => new
    {
        id = row.Id,
        type = Event,
        kind = row.Kind,
        ts = row.Timestamp.ToString("O"),
        title = row.Message,
        detail = "",
    };
}
