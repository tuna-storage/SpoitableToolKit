using System.Text;
using SpoitableToolKit.Abstractions.VrcLog;

namespace SpoitableToolKit.Host.VrcLog;

/// <summary>
/// VRChat は古いログを <c>old_logs</c> へ回して最後は消すので、取り込み直す手段が要る。
/// パーサが実ログで何を拾えているかを確かめる手段も兼ねる。
/// </summary>
internal static class VrcLogReplay
{
    public static VrcLogReplaySummary Run(string path, Action<VrcLogEventParams>? onEvent = null, int sampleLimit = 3)
    {
        var parser = new VrcLogParser();
        var sourceFile = Path.GetFileName(path);

        var eventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var tagCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var samples = new List<VrcLogEventParams>();
        var lines = 0;
        var entries = 0;

        void Consume(ParsedEntry? entry)
        {
            if (entry is not { } parsed) return;

            entries++;
            var tag = parsed.Line.Tag ?? "(none)";
            tagCounts[tag] = tagCounts.GetValueOrDefault(tag) + 1;

            if (parsed.Event is not { } logEvent) return;

            eventCounts[logEvent.Kind] = eventCounts.GetValueOrDefault(logEvent.Kind) + 1;

            // 種別ごとに採る。先頭から詰めると件数の多い 1 種別で埋まり、
            // 稀にしか出ない種別（ワールド入室など）のパース結果を確認できない。
            if (samples.Count(sample => sample.Kind == logEvent.Kind) < sampleLimit)
                samples.Add(logEvent);

            onEvent?.Invoke(logEvent);
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 65536, FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (reader.ReadLine() is { } line)
        {
            lines++;
            Consume(parser.Feed(line, sourceFile));
        }

        Consume(parser.Flush());

        return new VrcLogReplaySummary(lines, entries, eventCounts, tagCounts, samples);
    }

    /// <summary>人が読む形のレポート。個人情報を含みうるので本文はそのまま出す（ローカル専用）。</summary>
    public static string Describe(VrcLogReplaySummary summary)
    {
        var report = new StringBuilder();
        report.AppendLine($"lines   : {summary.Lines}");
        report.AppendLine($"entries : {summary.Entries}");
        report.AppendLine();

        report.AppendLine("recognized events:");
        foreach (var (kind, count) in summary.EventCounts.OrderByDescending(pair => pair.Value))
            report.AppendLine($"  {count,6}  {kind}");

        report.AppendLine();
        report.AppendLine("tags (top 15):");
        foreach (var (tag, count) in summary.TagCounts.OrderByDescending(pair => pair.Value).Take(15))
            report.AppendLine($"  {count,6}  {tag}");

        report.AppendLine();
        report.AppendLine("samples:");
        foreach (var sample in summary.Samples)
        {
            var detail = sample switch
            {
                WorldJoined w => $"{w.WorldName} / {w.WorldId} / instance={w.InstanceName} access={w.AccessType} owner={w.OwnerId} region={w.Region}",
                PlayerJoined p => $"{p.DisplayName} (user={p.UserId})",
                PlayerLeft p => $"{p.DisplayName} (user={p.UserId})",
                VideoPlaybackFailed v => $"{v.Url} — {v.Reason}",
                VideoPlaybackStarted v => $"{v.Url} [{v.PlaybackPath} {v.Width}x{v.Height}]",
                VideoResolved v => $"{v.Url} -> {v.ResolvedUrl}",
                ApiRequest a => $"{a.Method} {a.Endpoint}",
                _ => sample.Line.Message,
            };
            report.AppendLine($"  {sample.Timestamp:yyyy/MM/dd HH:mm:ss}  {sample.Kind,-20} {detail}");
        }

        return report.ToString();
    }
}
