using System.Text.RegularExpressions;
using SpoitableToolKit.Abstractions.VrcLog;

namespace SpoitableToolKit.Host.VrcLog.Recognizers;

/// <summary>
/// 2 つで 1 つの係 — 失敗は <c>[AVProVideo]</c> 側に直リンクでしか出ないので、
/// <c>[Video Playback]</c> 側で見た対応表と、再解決の見張りを両方が読み書きする。
/// </summary>
internal sealed partial class VideoPlaybackRecognizer
{
    private bool _sawPlaybackError;

    /// <summary>
    /// 同じ URL の再解決を「前回の失敗」とみなす窓。VRChat のリトライ間隔は 5 秒前後。
    /// これより離れていれば、人が同じ動画をもう一度かけただけとみなす。
    /// </summary>
    private static readonly TimeSpan RetryWindow = TimeSpan.FromSeconds(60);

    // 再生失敗を元の URL に結びつけるための相関。
    // 失敗は [AVProVideo] 側に直リンクで出るので、[Video Playback] 側で見た対応表が要る。
    private string? _attemptingUrl;
    private string? _resolvedOriginal;
    private string? _resolvedDirect;
    private string? _openingUrl;

    // Unity Video Player の失敗はログに一切出ない（[AVProVideo] の行も Error 行も出ない）ため、
    // 「同じ URL が再解決された＝前回の再生は失敗した」という間接証拠で拾う。
    private DateTimeOffset _attemptResolvedAt;
    private bool _attemptResolved;
    private bool _attemptFailureReported;

    [GeneratedRegex(@"^URL '(?<url>[^']+)'(?: resolved to '(?<resolved>[^']*)')?")]
    private static partial Regex PlaybackUrlPattern();

    [GeneratedRegex(@"^Attempting to resolve URL '(?<url>[^']+)'")]
    private static partial Regex PlaybackAttemptPattern();

    [GeneratedRegex(@"^Opening (?<url>\S+)")]
    private static partial Regex AvProOpeningPattern();

    [GeneratedRegex(@"^Error:\s*(?<reason>.+)$")]
    private static partial Regex AvProErrorPattern();

    // "Using playback path: MF-MediaEngine-Hardware (1920x1080@0.00)"
    [GeneratedRegex(@"^Using playback path: (?<path>\S+)(?: \((?<w>\d+)x(?<h>\d+)@)?")]
    private static partial Regex AvProPlaybackPathPattern();

    public void ForgetPlaybackError() => _sawPlaybackError = false;

    public Func<VrcLogLine, VrcLogEventParams>? RecognizePlayback(string rest, DateTimeOffset timestamp)
    {
        // 旧来の形。手元の VRChat では出なくなっているが、バージョン差があるので残す。
        if (rest.StartsWith("ERROR", StringComparison.Ordinal))
        {
            _sawPlaybackError = true;
            return null;
        }

        if (PlaybackAttemptPattern().Match(rest) is { Success: true } attempt)
        {
            var attemptUrl = attempt.Groups["url"].Value;
            _sawPlaybackError = false;

            // 同じ URL をもう一度解決しに行った＝前回の再生が失敗した。
            // Unity Video Player はログに何も残さないので、これが唯一の手掛かりになる。
            var isRetry = _attemptResolved
                && !_attemptFailureReported
                && _attemptingUrl == attemptUrl
                && timestamp - _attemptResolvedAt <= RetryWindow;

            _attemptingUrl = attemptUrl;
            _attemptResolved = false;
            _attemptFailureReported = false;

            if (!isRetry) return null;

            return line => new VideoPlaybackFailed
            {
                Line = line,
                Url = attemptUrl,
                Reason = "The same URL was resolved again (the previous playback failed)",
            };
        }

        if (PlaybackUrlPattern().Match(rest) is { Success: true } url)
        {
            var original = url.Groups["url"].Value;

            if (_sawPlaybackError)
            {
                _sawPlaybackError = false;
                return line => new VideoPlaybackFailed { Line = line, Url = original };
            }

            if (url.Groups["resolved"].Success)
            {
                var resolved = url.Groups["resolved"].Value;

                // 失敗は [AVProVideo] 側に直リンクでしか出ないので、対応表をここで作っておく。
                _resolvedOriginal = original;
                _resolvedDirect = resolved;

                // 再解決による失敗判定の起点。ここから先に何も起きなければ失敗している。
                _attemptResolved = true;
                _attemptResolvedAt = timestamp;

                return line => new VideoResolved { Line = line, Url = original, ResolvedUrl = resolved };
            }

            return null;
        }

        _sawPlaybackError = false;
        return null;
    }

    /// <summary>
    /// 実際の再生失敗はここに出る。<c>[Video Playback]</c> 側は URL 解決までしか見ておらず、
    /// 解決できた直リンクが 403 を返す場合（nsig 由来）は成功として通過してしまう。
    /// </summary>
    public Func<VrcLogLine, VrcLogEventParams>? RecognizeAvPro(string rest)
    {
        if (AvProOpeningPattern().Match(rest) is { Success: true } opening)
        {
            _openingUrl = opening.Groups["url"].Value;
            return null;
        }

        // 実際に再生が始まった証拠。AVPro でしか出ない。
        if (AvProPlaybackPathPattern().Match(rest) is { Success: true } started)
        {
            var direct = _openingUrl;
            var original = direct is not null && direct == _resolvedDirect ? _resolvedOriginal : _attemptingUrl;
            if (original is null && direct is null) return null;

            // 成功したので、再解決による失敗推定を止める。
            _attemptResolved = false;
            _attemptFailureReported = false;

            var path = started.Groups["path"].Value;
            int? width = started.Groups["w"].Success ? int.Parse(started.Groups["w"].Value) : null;
            int? height = started.Groups["h"].Success ? int.Parse(started.Groups["h"].Value) : null;
            var reportedUrl = original ?? direct!;

            return line => new VideoPlaybackStarted
            {
                Line = line,
                Url = reportedUrl,
                ResolvedUrl = direct,
                PlaybackPath = path,
                Width = width,
                Height = height,
            };
        }

        if (AvProErrorPattern().Match(rest) is { Success: true } error)
        {
            var reason = error.Groups["reason"].Value.Trim();
            var direct = _openingUrl;

            // 直前に開いたものが、直前に解決したものと同じなら元の URL まで辿れる。
            var original = direct is not null && direct == _resolvedDirect
                ? _resolvedOriginal
                : _attemptingUrl;

            if (original is null && direct is null) return null;

            // ここで報告済みにしておく。再解決による二重報告を防ぐ。
            _attemptFailureReported = true;

            var reported = original ?? direct!;
            return line => new VideoPlaybackFailed
            {
                Line = line,
                Url = reported,
                ResolvedUrl = direct,
                Reason = reason,
            };
        }

        return null;
    }
}
