using SpoitableToolKit.Abstractions.Events.Video;
using SpoitableToolKit.Modules.VideoUrl.Failures;
using Xunit;

namespace SpoitableToolKit.Tests.VideoUrl.Failures;

public sealed class FailureLogTests
{
    private static VideoPlaybackInfo Failure(string url, string? reason = null, int minutes = 0) => new()
    {
        Url = url,
        Reason = reason,
        AttemptCount = 1,
        Timestamp = new DateTimeOffset(2026, 8, 28, 2, 0, 0, TimeSpan.FromHours(9)).AddMinutes(minutes),
    };

    [Fact]
    public void TheSameUrlStaysOneRowAndCountsUp()
    {
        var log = new FailureLog();

        log.Record(Failure("https://example.invalid/a"));
        log.Record(Failure("https://example.invalid/a"));
        var list = log.Record(Failure("https://example.invalid/a"));

        var row = Assert.Single(list);
        Assert.Equal(3, row.Failures);
    }

    [Fact]
    public void TheMostRecentFailureComesFirst()
    {
        var log = new FailureLog();

        log.Record(Failure("https://example.invalid/a", minutes: 0));
        log.Record(Failure("https://example.invalid/b", minutes: 1));

        var list = log.Record(Failure("https://example.invalid/a", minutes: 2));

        Assert.Equal(["https://example.invalid/a", "https://example.invalid/b"], list.Select(f => f.Url));
        Assert.Equal(2, list[0].Failures);
    }

    /// <summary>理由は AVPro のときしか取れない。同じ URL の 2 回目が Unity 側で無音でも、一度出した理由を消さない。</summary>
    [Fact]
    public void AReasonOnceReadSurvivesALaterEmptyOne()
    {
        var log = new FailureLog();

        log.Record(Failure("https://example.invalid/a", reason: "Loading failed."));
        var list = log.Record(Failure("https://example.invalid/a", reason: null));

        Assert.Equal("Loading failed.", list[0].Reason);
    }

    [Fact]
    public void ANewReasonReplacesTheOldOne()
    {
        var log = new FailureLog();

        log.Record(Failure("https://example.invalid/a", reason: "Loading failed."));
        var list = log.Record(Failure("https://example.invalid/a", reason: "File not found."));

        Assert.Equal("File not found.", list[0].Reason);
    }

    /// <summary>常駐アプリなので、放っておくと 1 セッション分伸び続ける。</summary>
    [Fact]
    public void OverTheCapTheOldestIsDropped()
    {
        var log = new FailureLog();

        for (var i = 0; i < FailureLog.Capacity + 10; i++)
            log.Record(Failure($"https://example.invalid/{i}", minutes: i));

        var list = log.Snapshot();

        Assert.Equal(FailureLog.Capacity, list.Length);
        Assert.Equal($"https://example.invalid/{FailureLog.Capacity + 9}", list[0].Url);

        Assert.DoesNotContain(list, f => f.Url == "https://example.invalid/0");
    }

    [Fact]
    public void RecordReturnsTheSameListAsReadingItLater()
    {
        var log = new FailureLog();

        var returned = log.Record(Failure("https://example.invalid/a"));

        Assert.Equal(returned.Select(f => f.Url), log.Snapshot().Select(f => f.Url));
    }
}
