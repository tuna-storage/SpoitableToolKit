using System.Text.Json;
using SpoitableToolKit.Abstractions.Localization;

namespace SpoitableToolKit.Modules.VideoUrl.Resolving;

/// <summary>
/// yt-dlp の <c>-J</c> の出力（<c>formats</c>）から、<b>1 本で音まで出る</b>ものを選ぶ。VRChat のプレイヤーに渡せるのは URL 1 本で、
/// 映像と音声を自前で合成することはできない。映像だけの 1 本は音が出ないので選ばない。音声だけの 1 本は、音楽を流す用に選ぶ。
/// </summary>
internal static class StreamPicker
{
    /// <summary>出力の <c>formats</c>。無ければ空。</summary>
    public static List<JsonElement> Formats(JsonElement root) =>
        root.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array
            ? [.. formats.EnumerateArray()]
            : [];

    /// <summary>
    /// 映像と音声が同じセグメントに入っている HLS プレイリスト。<b>音付きで一番画質が出る選択肢</b>。
    /// <para>
    /// 実測: itag 96 のセグメントを ffprobe に掛けると <c>h264 1080p</c> と <c>aac</c> が
    /// 両方入っており、URL の形（<c>api/manifest/hls_playlist</c>）は VRChat 自身が再生できている
    /// ものと一致する。
    /// </para>
    /// </summary>
    public static void AddHlsMuxed(List<JsonElement> formats, List<ResolvedStream> streams, ILocalizer text)
    {
        var best = formats
            .Where(f => Codec(f, "vcodec") is not null && Codec(f, "acodec") is not null)
            .Where(f => JsonFields.Text(f, "protocol")?.StartsWith("m3u8", StringComparison.Ordinal) == true)
            .OrderByDescending(f => JsonFields.Number(f, "height"))
            .ThenByDescending(f => JsonFields.Number(f, "fps"))
            .FirstOrDefault();

        if (best.ValueKind != JsonValueKind.Object || JsonFields.Text(best, "url") is not { Length: > 0 } url) return;

        var height = JsonFields.Number(best, "height");
        streams.Add(new ResolvedStream(
            text.Format("videourl.stream.hls.label", height > 0 ? $"{height}p" : text["videourl.stream.best"]),
            "hls", url, text["videourl.stream.hls.note"]));
    }

    /// <summary>
    /// 映像と音声が 1 本になった mp4（itag 18）。YouTube が出すのは 360p までだが、
    /// プレイヤーを選ばない。
    /// </summary>
    public static void AddMuxed(List<JsonElement> formats, List<ResolvedStream> streams, ILocalizer text)
    {
        var muxed = formats
            .Where(f => Codec(f, "vcodec") is not null && Codec(f, "acodec") is not null)
            .Where(f => JsonFields.Text(f, "protocol")?.StartsWith("http", StringComparison.Ordinal) == true)
            .OrderByDescending(f => JsonFields.Number(f, "height"));

        foreach (var format in muxed)
        {
            if (JsonFields.Text(format, "url") is not { Length: > 0 } url) continue;
            if (streams.Any(s => s.Url == url)) continue;

            var height = JsonFields.Number(format, "height");
            streams.Add(new ResolvedStream(
                text.Format("videourl.stream.muxed.label", height > 0 ? $"{height}p" : text["videourl.stream.best"],
                    JsonFields.Text(format, "ext") ?? "mp4"),
                "muxed", url, text["videourl.stream.muxed.note"]));
        }
    }

    /// <summary>
    /// 音声だけの 1 本（映像は出ない）。入れ物（m4a・webm）ごとに 1 本ずつ選び、m4a を先に並べる。
    /// <para>
    /// 吹き替えのある動画では、同じ itag が言語の数だけ並ぶ（実測で 24 本）。元の言語は <c>language_preference</c> が 10 で、
    /// 吹き替えは -1 なので、それを先に見る。音量をならした版（<c>format_id</c> が <c>-drc</c> で終わる）は、
    /// 普通の版があればそちらを選ぶ。そのうえでビットレート（<c>abr</c>）の高いものを取る。
    /// </para>
    /// </summary>
    public static void AddAudioOnly(List<JsonElement> formats, List<ResolvedStream> streams, ILocalizer text)
    {
        var picked = formats
            .Where(f => Codec(f, "vcodec") is null && Codec(f, "acodec") is not null)
            .Where(f => JsonFields.Text(f, "protocol")?.StartsWith("http", StringComparison.Ordinal) == true)
            .Where(f => JsonFields.Text(f, "url") is { Length: > 0 })
            .GroupBy(f => JsonFields.Text(f, "ext") ?? "")
            .Select(sameExt => sameExt
                .OrderByDescending(f => JsonFields.Number(f, "language_preference"))
                .ThenBy(IsDrc)
                .ThenByDescending(f => JsonFields.Number(f, "abr"))
                .First())
            .OrderBy(f => JsonFields.Text(f, "ext") == "m4a" ? 0 : 1);

        foreach (var format in picked)
        {
            var bitrate = JsonFields.Number(format, "abr");
            streams.Add(new ResolvedStream(
                text.Format("videourl.stream.audio.label", bitrate > 0 ? $"{bitrate} kbps" : text["videourl.stream.audio.best"],
                    JsonFields.Text(format, "ext") is { Length: > 0 } ext ? ext : "?"),
                "audio", JsonFields.Text(format, "url")!, text["videourl.stream.audio.note"]));
        }
    }

    /// <summary>音量をならした版か。yt-dlp は <c>format_id</c> の後ろに <c>-drc</c> を付けて分ける。</summary>
    private static bool IsDrc(JsonElement format) =>
        JsonFields.Text(format, "format_id")?.EndsWith("-drc", StringComparison.Ordinal) == true;

    /// <summary>コーデック名。無い場合（<c>"none"</c> / null）は null を返す。</summary>
    private static string? Codec(JsonElement element, string name) =>
        JsonFields.Text(element, name) is { Length: > 0 } codec && codec != "none" ? codec : null;
}
