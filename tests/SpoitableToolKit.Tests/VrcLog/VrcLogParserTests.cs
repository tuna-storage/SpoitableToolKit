using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.VrcLog;
using Xunit;

namespace SpoitableToolKit.Tests.VrcLog;

/// <summary>
/// VRChat のログから「失敗」がまともに読めない前提で組んである。
/// <c>[Video Playback] ERROR</c> は yt-dlp が解決に失敗したときだけ。実際の失敗は <c>[AVProVideo] Error:</c> に直リンクで出るので、元の URL に戻す。
/// Unity Video Player は失敗しても何も書かないので、同じ URL の再解決から推定する。
/// </summary>
public sealed class VrcLogParserTests
{
    private const string Original = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
    private const string Direct = "https://rr1---sn-example.googlevideo.com/videoplayback?itag=18";

    private static string At(string time, string level, string body) =>
        $"2026.08.27 {time} {level,-10} -  {body}";

    private static List<VrcLogEventParams> Parse(params string[] lines)
    {
        var parser = new VrcLogParser();
        List<VrcLogEventParams> events = [];

        foreach (var line in lines)
            if (parser.Feed(line, "test.log") is { Event: { } completed })
                events.Add(completed);

        if (parser.Flush() is { Event: { } last }) events.Add(last);
        return events;
    }

    [Fact]
    public void AnAVProErrorIsReportedAgainstTheOriginalUrl()
    {
        var events = Parse(
            At("12:00:00", "Debug", $"[Video Playback] Attempting to resolve URL '{Original}'"),
            At("12:00:01", "Debug", $"[Video Playback] URL '{Original}' resolved to '{Direct}'"),
            At("12:00:02", "Debug", $"[AVProVideo] Opening {Direct} (offset 0) with API MediaFoundation"),
            At("12:00:03", "Error", "[AVProVideo] Error: Loading failed.  File not found, codec not supported"));

        var failed = Assert.Single(events.OfType<VideoPlaybackFailed>());

        // videourl が解決し直すのに要るのは元の URL。直リンクを渡されても使えない。
        Assert.Equal(Original, failed.Url);
        Assert.Equal(Direct, failed.ResolvedUrl);
        Assert.StartsWith("Loading failed.", failed.Reason);
    }

    [Fact]
    public void AnAVProMisconfigurationIsNotAPlaybackFailure()
    {
        // "Error:" の接頭辞が無い行。Error レベルで出るが再生の失敗ではない。
        var events = Parse(
            At("12:00:00", "Debug", $"[Video Playback] URL '{Original}' resolved to '{Direct}'"),
            At("12:00:01", "Error", "[AVProVideo] No MediaReference specified"),
            At("12:00:02", "Error", "[AVProVideo] No file path specified"));

        Assert.Empty(events.OfType<VideoPlaybackFailed>());
    }

    [Fact]
    public void ReResolvingTheSameUrlCountsAsAFailure()
    {
        // Unity Video Player の経路。AVPro の行が一切出ないまま、5 秒後に同じ URL を解決し直す。
        var events = Parse(
            At("12:00:00", "Debug", $"[Video Playback] Attempting to resolve URL '{Original}'"),
            At("12:00:01", "Debug", $"[Video Playback] URL '{Original}' resolved to '{Direct}'"),
            At("12:00:06", "Debug", $"[Video Playback] Attempting to resolve URL '{Original}'"));

        var failed = Assert.Single(events.OfType<VideoPlaybackFailed>());
        Assert.Equal(Original, failed.Url);
        Assert.Null(failed.ResolvedUrl);
    }

    [Fact]
    public void AReResolveOutsideTheWindowIsNotAFailure()
    {
        // 60 秒を超えたら「見終わって次に同じ動画をかけ直した」の可能性が高い。
        var events = Parse(
            At("12:00:00", "Debug", $"[Video Playback] Attempting to resolve URL '{Original}'"),
            At("12:00:01", "Debug", $"[Video Playback] URL '{Original}' resolved to '{Direct}'"),
            At("12:01:30", "Debug", $"[Video Playback] Attempting to resolve URL '{Original}'"));

        Assert.Empty(events.OfType<VideoPlaybackFailed>());
    }

    [Fact]
    public void SwitchingToADifferentUrlIsNotAFailure()
    {
        var events = Parse(
            At("12:00:00", "Debug", $"[Video Playback] Attempting to resolve URL '{Original}'"),
            At("12:00:01", "Debug", $"[Video Playback] URL '{Original}' resolved to '{Direct}'"),
            At("12:00:05", "Debug", "[Video Playback] Attempting to resolve URL 'https://example.com/other'"));

        Assert.Empty(events.OfType<VideoPlaybackFailed>());
    }

    [Fact]
    public void AlreadyReportedByAVProItIsNotReportedAgainOnReResolve()
    {
        // VRChat は失敗するとリトライするので、この並びは実ログでごく普通に出る。
        var events = Parse(
            At("12:00:00", "Debug", $"[Video Playback] Attempting to resolve URL '{Original}'"),
            At("12:00:01", "Debug", $"[Video Playback] URL '{Original}' resolved to '{Direct}'"),
            At("12:00:02", "Debug", $"[AVProVideo] Opening {Direct} (offset 0) with API MediaFoundation"),
            At("12:00:03", "Error", "[AVProVideo] Error: Loading failed."),
            At("12:00:08", "Debug", $"[Video Playback] Attempting to resolve URL '{Original}'"));

        Assert.Single(events.OfType<VideoPlaybackFailed>());
    }

    [Fact]
    public void ASuccessfulPlaybackReportsNoFailureAndCarriesTheResolution()
    {
        var events = Parse(
            At("12:00:00", "Debug", $"[Video Playback] Attempting to resolve URL '{Original}'"),
            At("12:00:01", "Debug", $"[Video Playback] URL '{Original}' resolved to '{Direct}'"),
            At("12:00:02", "Debug", $"[AVProVideo] Opening {Direct} (offset 0) with API MediaFoundation"),
            At("12:00:03", "Debug", "[AVProVideo] Using playback path: MF-MediaEngine-Hardware (1920x1080@0.00)"),
            // 成功したあとの再解決は失敗ではない。
            At("12:00:20", "Debug", $"[Video Playback] Attempting to resolve URL '{Original}'"));

        var started = Assert.Single(events.OfType<VideoPlaybackStarted>());
        Assert.Equal(Original, started.Url);
        Assert.Equal(1920, started.Width);
        Assert.Equal(1080, started.Height);
        Assert.Empty(events.OfType<VideoPlaybackFailed>());
    }

    [Fact]
    public void TheLegacyErrorLinePairsWithTheNextUrlLine()
    {
        // yt-dlp 自体が落ちた形（非公開・年齢確認・削除済み）。解決できていないので
        // resolved to の中身は元 URL のまま来る。
        var events = Parse(
            At("12:00:00", "Warning", "[Video Playback] ERROR: [youtube] dQw4w9WgXcQ: Private video."),
            At("12:00:00", "Debug", $"[Video Playback] URL '{Original}' resolved to '{Original}'"));

        var failed = Assert.Single(events.OfType<VideoPlaybackFailed>());
        Assert.Equal(Original, failed.Url);

        // 解決自体が失敗しているので VideoResolved は出さない。
        Assert.Empty(events.OfType<VideoResolved>());
    }

    /// <summary>ERROR 行と URL 行の間に <c>[Behaviour]</c> の行（人の出入り・ワールドの移動）が挟まったら、もう結びつけない。間で何かが起きたあとの URL は別の再生のもの。</summary>
    [Fact]
    public void ABehaviourLineBetweenTheLegacyErrorAndTheUrlLineKeepsThemApart()
    {
        var events = Parse(
            At("12:00:00", "Warning", "[Video Playback] ERROR: [youtube] dQw4w9WgXcQ: Private video."),
            At("12:00:01", "Debug", "[Behaviour] OnPlayerJoined Someone"),
            At("12:00:02", "Debug", $"[Video Playback] URL '{Original}' resolved to '{Direct}'"));

        Assert.Empty(events.OfType<VideoPlaybackFailed>());
        Assert.Single(events.OfType<VideoResolved>());
    }

    [Fact]
    public void AnotherEventInTheLeaveLogIsNotReadAsALeave()
    {
        // "OnPlayerLeftRoom" は自分が部屋を出たときの行。プレイヤーの退室ではない。
        var events = Parse(
            At("12:00:00", "Debug", "[Behaviour] OnPlayerLeftRoom"),
            At("12:00:01", "Debug", "[Behaviour] OnPlayerLeft Someone (usr_00000000-1111-2222-3333-444444444444)"));

        var left = Assert.Single(events.OfType<PlayerLeft>());
        Assert.Equal("Someone", left.DisplayName);
    }

    [Fact]
    public void ALineWithoutATimestampIsJoinedToThePreviousEntry()
    {
        var events = Parse(
            At("12:00:00", "Error", "[Behaviour] An exception occurred"),
            "  at Foo.Bar()",
            "  at Baz.Qux()");

        var parser = new VrcLogParser();
        parser.Feed(At("12:00:00", "Error", "[Behaviour] An exception occurred"), "test.log");
        parser.Feed("  at Foo.Bar()", "test.log");
        var entry = parser.Flush();

        Assert.NotNull(entry);
        Assert.Contains("at Foo.Bar()", entry!.Value.Line.Message);
        Assert.Empty(events.OfType<VideoPlaybackFailed>());
    }
}
