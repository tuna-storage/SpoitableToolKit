using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.VrcLog.Recognizers;

namespace SpoitableToolKit.Host.VrcLog;

/// <summary>
/// ファイル I/O は持たない。エントリはスタックトレースのような継続行を伴うことがあるため、
/// 「次のタイムスタンプ付き行が来る」か「バッチが終わる」まで確定しない。
/// <c>[API]</c> は 1 行で済むのでここで意味づける。
/// </summary>
internal sealed partial class VrcLogParser
{
    /// <summary>継続行を無限に飲み込まないための上限。長大なスタックトレースで破裂させない。</summary>
    private const int MaxContinuationChars = 8 * 1024;

    private VrcLogLine? _pendingLine;
    private StringBuilder? _pendingContinuation;
    private Func<VrcLogLine, VrcLogEventParams>? _pendingEvent;

    // 意味づけの係。状態（持ち越したワールド名・動画の相関）は係が持つので、parser と一緒に作り直す（VrcLogBus は parser ごと捨て直す）。
    private readonly BehaviourRecognizer _behaviour = new();
    private readonly VideoPlaybackRecognizer _video = new();

    // "2026.08.26 00:20:47 Debug      -  [Behaviour] OnPlayerJoined Name"
    [GeneratedRegex(@"^(?<ts>\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2})\s+(?<level>[A-Za-z]+)\s+-\s\s?(?<body>.*)$")]
    private static partial Regex EntryPattern();

    [GeneratedRegex(@"^\[(?<tag>[^\]]{1,64})\]\s?(?<rest>.*)$", RegexOptions.Singleline)]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"^Requesting (?<method>[A-Za-z]+) (?<endpoint>\S+)")]
    private static partial Regex ApiRequestPattern();

    public ParsedEntry? Feed(string rawLine, string sourceFile)
    {
        var match = EntryPattern().Match(rawLine);
        if (!match.Success)
        {
            // タイムスタンプが無い＝継続行（スタックトレース、区切り線、空行など）。
            AppendContinuation(rawLine);
            return null;
        }

        var completed = Flush();
        StartEntry(match, sourceFile);
        return completed;
    }

    public ParsedEntry? Flush()
    {
        if (_pendingLine is null) return null;

        var line = _pendingLine;
        if (_pendingContinuation is { Length: > 0 })
            line = line with { Message = line.Message + "\n" + _pendingContinuation.ToString().TrimEnd() };

        var entry = new ParsedEntry(line, _pendingEvent?.Invoke(line));

        _pendingLine = null;
        _pendingContinuation = null;
        _pendingEvent = null;
        return entry;
    }

    private void AppendContinuation(string rawLine)
    {
        if (_pendingLine is null) return;

        _pendingContinuation ??= new StringBuilder();
        if (_pendingContinuation.Length >= MaxContinuationChars) return;

        _pendingContinuation.Append(rawLine).Append('\n');
    }

    private void StartEntry(Match match, string sourceFile)
    {
        var body = match.Groups["body"].Value;

        string? tag = null;
        var rest = body;
        if (TagPattern().Match(body) is { Success: true } tagged)
        {
            tag = tagged.Groups["tag"].Value;
            rest = tagged.Groups["rest"].Value;
        }

        var timestamp = ParseTimestamp(match.Groups["ts"].Value);

        _pendingLine = new VrcLogLine
        {
            Timestamp = timestamp,
            Level = ParseLevel(match.Groups["level"].Value),
            Tag = tag,
            Message = body,
            SourceFile = sourceFile,
        };
        _pendingContinuation = null;
        _pendingEvent = Recognize(tag, rest, timestamp);
    }

    /// <summary>行の確定（継続行の連結）を待ってから作りたいので、イベント自体ではなくファクトリを返す。</summary>
    private Func<VrcLogLine, VrcLogEventParams>? Recognize(string? tag, string rest, DateTimeOffset timestamp)
    {
        switch (tag)
        {
            case "Behaviour":
                // [Behaviour] の行はどれも、旧来の [Video Playback] の ERROR を忘れさせる（その次の URL 行を失敗と結びつけない）。
                _video.ForgetPlaybackError();
                return _behaviour.Recognize(rest);

            case "Video Playback":
                return _video.RecognizePlayback(rest, timestamp);

            case "AVProVideo":
                return _video.RecognizeAvPro(rest);

            case "API":
                // "Requesting Get users/usr_x ..." だけを拾う。
                // "[123] Sending Get request to https://..." は同じ呼び出しの別表現なので二重に数えない。
                if (ApiRequestPattern().Match(rest) is { Success: true } api)
                {
                    var method = api.Groups["method"].Value;
                    var endpoint = api.Groups["endpoint"].Value;
                    return line => new ApiRequest { Line = line, Method = method, Endpoint = endpoint };
                }
                return null;

            default:
                return null;
        }
    }

    private static DateTimeOffset ParseTimestamp(string raw) =>
        DateTime.TryParseExact(raw, "yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out var parsed)
            ? new DateTimeOffset(parsed)
            : DateTimeOffset.Now;

    private static VrcLogLevel ParseLevel(string raw) => raw switch
    {
        "Debug" => VrcLogLevel.Debug,
        "Log" => VrcLogLevel.Log,
        "Warning" => VrcLogLevel.Warning,
        "Error" => VrcLogLevel.Error,
        "Exception" => VrcLogLevel.Exception,
        _ => VrcLogLevel.Unknown,
    };
}
