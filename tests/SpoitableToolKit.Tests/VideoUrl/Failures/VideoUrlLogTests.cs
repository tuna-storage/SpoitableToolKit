using SpoitableToolKit.Abstractions.Events.Video;
using SpoitableToolKit.Modules.VideoUrl;
using SpoitableToolKit.Modules.VideoUrl.Config;
using SpoitableToolKit.Tests.Fakes;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.VideoUrl.Failures;

public sealed class VideoUrlLogTests
{
    private static VideoPlaybackInfo Failure(string url) => new()
    {
        Url = url,
        Reason = "Loading failed",
        AttemptCount = 1,
        Timestamp = new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.FromHours(9)),
    };

    private static (VideoUrlModule Module, Logs Logs) NewModule(bool ask)
    {
        var logs = new Logs();
        var module = new VideoUrlModule();
        ((VideoUrlConfig)module.Config!).AskOnFailure = ask;

        Assert.True(module.OnInit(FakeModuleHost.LoggingTo(logs)));
        return (module, logs);
    }

    [Fact]
    public void AFailureWeDoNotAskAboutSaysWhy()
    {
        var (off, offLogs) = NewModule(ask: false);
        off.OnVideoPlaybackFailed(new VideoPlaybackFailedEventParams { Video = Failure("https://www.youtube.com/watch?v=abc"), IsRepeatedFail = false });
        off.OnShutdown();

        Assert.Equal(
            ["Video: VRChat could not play https://www.youtube.com/watch?v=abc (Loading failed); not asking: turned off in the settings"],
            offLogs.Infos);

        var (elsewhere, elsewhereLogs) = NewModule(ask: true);
        elsewhere.OnVideoPlaybackFailed(new VideoPlaybackFailedEventParams { Video = Failure("https://example.invalid/v.mp4"), IsRepeatedFail = false });
        elsewhere.OnShutdown();

        Assert.Equal(
            ["Video: VRChat could not play https://example.invalid/v.mp4 (Loading failed); not asking: not a site we resolve"],
            elsewhereLogs.Infos);
    }

    /// <summary>VRChat は同じ URL で 4〜5 回やり直す。</summary>
    [Fact]
    public void RetriesAreNotLoggedAgain()
    {
        var (module, logs) = NewModule(ask: false);

        module.OnVideoPlaybackFailed(new VideoPlaybackFailedEventParams { Video = Failure("https://www.youtube.com/watch?v=abc"), IsRepeatedFail = false });
        module.OnVideoPlaybackFailed(new VideoPlaybackFailedEventParams { Video = Failure("https://www.youtube.com/watch?v=abc"), IsRepeatedFail = true });
        module.OnVideoPlaybackFailed(new VideoPlaybackFailedEventParams { Video = Failure("https://www.youtube.com/watch?v=abc"), IsRepeatedFail = true });
        module.OnShutdown();

        Assert.Single(logs.Infos);
    }
}
