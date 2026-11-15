using SpoitableToolKit.Abstractions.Events.Video;
using SpoitableToolKit.Abstractions.VrcLog;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Video;
using SpoitableToolKit.Tests.Fakes;
using Xunit;

namespace SpoitableToolKit.Tests.Video;

/// <summary>VRChat は失敗すると同じ URL で 4〜5 回やり直す。<see cref="VideoPlaybackFailedEventParams.IsRepeatedFail"/> が緩いと 1 本の動画で何度も聞かれる。</summary>
public sealed class VideoEventProviderTests
{
    private sealed class Recorder : IVideoEventListener
    {
        public List<(bool Repeated, VideoPlaybackInfo Info)> Failed { get; } = [];

        public List<VideoPlaybackInfo> Started { get; } = [];

        public void OnVideoPlaybackFailed(VideoPlaybackFailedEventParams e) => Failed.Add((e.IsRepeatedFail, e.Video));

        public void OnVideoPlaybackStarted(VideoPlaybackStartedEventParams e) => Started.Add(e.Video);
    }

    private const string Url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";

    private static VrcLogLine Line(DateTimeOffset at) => new()
    {
        Timestamp = at,
        Level = VrcLogLevel.Error,
        Message = "test",
        SourceFile = "test.log",
    };

    private static VideoPlaybackFailed Failure(DateTimeOffset at, string url = Url) =>
        new() { Line = Line(at), Url = url };

    private static readonly DateTimeOffset Noon = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private static (FakeVrcLogBus Bus, Recorder Listener, VideoEventProvider Provider) NewProvider()
    {
        var bus = new FakeVrcLogBus();
        var videos = VideoHub();
        var listener = new Recorder();
        videos.Source.AddListener<IVideoEventListener>(new FakeModule("p"), listener);
        return (bus, listener, new VideoEventProvider(bus, videos));
    }

    [Fact]
    public void AfterDisposingItNoLongerListensToTheLog()
    {
        var (bus, listener, provider) = NewProvider();

        provider.Dispose();
        bus.Emit(Failure(Noon));

        Assert.Empty(listener.Failed);
        Assert.Equal(0, bus.Hub.ListenerCount);
    }

    [Fact]
    public void TheSecondAndLaterTriesOfOneUrlCountAsRepeats()
    {
        var (bus, listener, provider) = NewProvider();
        using var _ = provider;

        bus.Emit(Failure(Noon));
        bus.Emit(Failure(Noon.AddSeconds(5)));
        bus.Emit(Failure(Noon.AddSeconds(11)));

        Assert.Equal([false, true, true], listener.Failed.Select(f => f.Repeated));
        Assert.Equal([1, 2, 3], listener.Failed.Select(f => f.Info.AttemptCount));
    }

    [Fact]
    public void ADifferentUrlIsNotDraggedIn()
    {
        var (bus, listener, provider) = NewProvider();
        using var _ = provider;

        bus.Emit(Failure(Noon));
        bus.Emit(Failure(Noon.AddSeconds(5), "https://example.com/other"));

        Assert.All(listener.Failed, f => Assert.False(f.Repeated));
    }

    [Fact]
    public void AGapRestartsTheCount()
    {
        // 5 分空いていれば「さっきの失敗の続き」ではなく、かけ直したとみなす。
        var (bus, listener, provider) = NewProvider();
        using var _ = provider;

        bus.Emit(Failure(Noon));
        bus.Emit(Failure(Noon.AddMinutes(6)));

        Assert.Equal([false, false], listener.Failed.Select(f => f.Repeated));
    }

    [Fact]
    public void ASuccessfulPlaybackForgetsTheCount()
    {
        // 同じ動画をあとでもう一度かけたときに「2 回目の失敗」扱いにしない。
        var (bus, listener, provider) = NewProvider();
        using var _ = provider;

        bus.Emit(Failure(Noon));
        bus.Emit(new VideoPlaybackStarted { Line = Line(Noon.AddSeconds(3)), Url = Url });
        bus.Emit(Failure(Noon.AddSeconds(30)));

        Assert.Equal([false, false], listener.Failed.Select(f => f.Repeated));
        Assert.Single(listener.Started);
    }

    [Fact]
    public void ChangingWorldsCarriesNothingOver()
    {
        var (bus, listener, provider) = NewProvider();
        using var _ = provider;

        bus.Emit(Failure(Noon));
        bus.Emit(new WorldJoined
        {
            Line = Line(Noon.AddSeconds(10)),
            WorldId = "wrld_00000000-1111-2222-3333-444444444444",
            InstanceName = "12345",
            RawInstance = "12345",
        });
        bus.Emit(Failure(Noon.AddSeconds(20)));

        Assert.Equal([false, false], listener.Failed.Select(f => f.Repeated));
    }

    [Fact]
    public void AThrowingListenerDoesNotStopDeliveryToTheOthers()
    {
        // モジュールは第三者が書きうる。1 つの例外で他を巻き添えにしない。
        var bus = new FakeVrcLogBus();
        var videos = VideoHub();
        var listener = new Recorder();

        videos.Source.AddListener<IVideoEventListener>(new FakeModule("broken"), new ThrowingListener());
        videos.Source.AddListener<IVideoEventListener>(new FakeModule("p"), listener);
        using var provider = new VideoEventProvider(bus, videos);

        bus.Emit(Failure(Noon));

        Assert.Single(listener.Failed);
    }

    /// <summary>アプリと同じ動画の口（owner はどれでも通す）。</summary>
    private static EventHub VideoHub() =>
        new EventHubs(_ => true, NullLoggerFactory.Instance).CreateHub(HostOwner.Instance, [typeof(IVideoEventListener)]);

    private sealed class ThrowingListener : IVideoEventListener
    {
        public void OnVideoPlaybackFailed(VideoPlaybackFailedEventParams e) =>
            throw new InvalidOperationException("モジュールが壊れている");
    }
}
