using SpoitableToolKit.Abstractions.Events.Video;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Abstractions.Shortcuts;
using SpoitableToolKit.Modules.VideoUrl;
using SpoitableToolKit.Tests.Fakes;
using Xunit;

namespace SpoitableToolKit.Tests.VideoUrl.Resolving;

/// <summary>再生失敗のときに解決を持ちかける宛先の見分け。解けるのは YouTube だけなので、それ以外で聞くと人の手を止めるだけになる。</summary>
public sealed class VideoUrlResolvableTests
{
    /// <summary>サブドメインは何でも通す。<c>music.</c> や <c>m.</c> で来るため。</summary>
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("http://youtu.be/dQw4w9WgXcQ")]
    public void YouTubeなら聞きに行く(string url) => Assert.True(VideoUrlModule.IsResolvable(url));

    /// <summary>名前が似ているだけの別ドメインは通さない（前方一致や <c>Contains</c> で書くと通ってしまう）。</summary>
    [Theory]
    [InlineData("https://notyoutube.com/watch?v=x")]
    [InlineData("https://youtube.com.example.test/watch?v=x")]
    [InlineData("https://myyoutu.be/x")]
    [InlineData("https://example.test/youtube.com/x")]
    public void 名前が似ているだけの別ドメインは聞かない(string url) =>
        Assert.False(VideoUrlModule.IsResolvable(url));

    [Theory]
    [InlineData("https://vimeo.com/123456")]
    [InlineData("https://example.test/movie.mp4")]
    [InlineData("https://www.twitch.tv/videos/123")]
    public void YouTube以外では聞かない(string url) => Assert.False(VideoUrlModule.IsResolvable(url));

    /// <summary>
    /// URL の形をしていないものも弾く。<b>ここを通すと yt-dlp の引数に流れる</b>
    /// （<c>-</c> で始まる文字列はオプションとして解釈される）。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("youtube.com/watch?v=x")]
    [InlineData("file:///C:/movie.mp4")]
    [InlineData("--version")]
    public void 読めない宛先は聞かない(string? url) => Assert.False(VideoUrlModule.IsResolvable(url));

    /// <summary>
    /// http / https 以外は、宛先が youtube でも通さない（URL がそのまま yt-dlp の引数へ流れる）。<c>spoitable://</c> はこのアプリ自身がパネルを配る scheme。
    /// host が youtube のままの形でないと、ドメインの判定だけで落ちて scheme を見る行を試験できない。
    /// </summary>
    [Theory]
    [InlineData("ftp://www.youtube.com/video.mp4")]
    [InlineData("spoitable://youtube.com/watch?v=x")]
    [InlineData("file://youtu.be/x")]
    public void httpでなければ宛先がYouTubeでも聞かない(string url) =>
        Assert.False(VideoUrlModule.IsResolvable(url));

    /// <summary>YouTube 以外の失敗では、そもそも聞きに行かない。見分けの試験だけでは結果を使っていなくても緑になるので、失敗を流してモーダルが出ていないことまで見る。</summary>
    [Fact]
    public void YouTube以外の失敗では聞きに行かない()
    {
        var (_, host) = Started();

        Fail(host, Failure("https://example.test/movie.mp4"), repeated: false);

        Assert.Empty(Asked(host));

        // **一覧には残る。** 弾くのは「聞くかどうか」だけで、何が落ちたかは分かる。
        Assert.NotEmpty(host.Panel.Pushed);
    }

    [Fact]
    public void YouTubeの失敗では聞きに行く()
    {
        var (_, host) = Started();

        Fail(host, Failure("https://music.youtube.com/watch?v=dQw4w9WgXcQ"), repeated: false);

        Assert.Single(Asked(host));
    }

    /// <summary>
    /// やり直しでは聞かない。VRChat は同じ URL で 4〜5 回試すので、
    /// <b>宛先を見る前にここで落ちている</b>こと。
    /// </summary>
    [Fact]
    public void やり直しでは聞かない()
    {
        var (_, host) = Started();

        Fail(host, Failure("https://youtu.be/dQw4w9WgXcQ"), repeated: true);

        Assert.Empty(Asked(host));
    }

    /// <summary>
    /// 手首の「最新を解いてコピー」（<c>videourl/resolve-latest</c>）は OnInit で登録され、押すと解きに行く。
    /// <b>解ける失敗がまだ無ければ、そう知らせるだけ</b>（yt-dlp を探しに行かない）。タイルの中身は解いている間の字（いまは空）。
    /// </summary>
    [Fact]
    public async Task 手首の最新を解くは解ける失敗が無ければそう知らせる()
    {
        var (_, host) = Started();
        Fail(host, Failure("https://example.test/movie.mp4"), repeated: false);

        var shortcut = Assert.Single(host.Shortcuts.Registered);
        Assert.Equal(("resolve-latest", "videourl.shortcut.resolveLatest"), (shortcut.Id, shortcut.Name));
        Assert.Equal("", shortcut.Status!.Text);

        await Assert.IsAssignableFrom<IClickableWristShortcut>(shortcut.Handler).OnTriggerAsync(CancellationToken.None);

        Assert.Equal([("videourl.shortcut.resolveLatest.none", (string?)null)], ((FakeNotifications)host.Notifications).Toasts);
        Assert.Empty(Asked(host));
    }

    /// <summary>
    /// 承諾させない（既定）。<b>承諾すると yt-dlp を探しに行く</b>ので、
    /// ここで見たいのは「聞いたかどうか」だけに留める。
    /// </summary>
    private static (VideoUrlModule Module, FakeModuleHost Host) Started()
    {
        var host = new FakeModuleHost();
        var module = new VideoUrlModule();

        Assert.True(module.OnInit(host));
        return (module, host);
    }

    private static List<NotifyRequest> Asked(FakeModuleHost host) =>
        ((FakeNotifications)host.Notifications).Asked;

    private static VideoPlaybackInfo Failure(string url) => new() { Url = url, AttemptCount = 1, Timestamp = DateTimeOffset.UtcNow };

    /// <summary>
    /// アプリと同じ道で配る（動画の口から）。<b>モジュールのメソッドを直接呼ばない</b> — 呼ぶと、OnInit で口に足し忘れても、
    /// 古い形のメソッドが残っていても緑になる。
    /// </summary>
    private static void Fail(FakeModuleHost host, VideoPlaybackInfo video, bool repeated) =>
        host.VideoHub.Fire<IVideoEventListener>(l => l.OnVideoPlaybackFailed(new VideoPlaybackFailedEventParams { Video = video, IsRepeatedFail = repeated }));

    /// <summary>大文字で来ても通る。これは <see cref="Uri"/> が host を小文字へ正規化しているからで、<c>OrdinalIgnoreCase</c> は観測できる振る舞いを持たない安全網。だから正規化されているという事実の方を試験に書く。</summary>
    [Fact]
    public void 大文字で来ても通る()
    {
        Assert.Equal("www.youtube.com", new Uri("https://WWW.YOUTUBE.COM/watch?v=x").Host);
        Assert.True(VideoUrlModule.IsResolvable("https://WWW.YOUTUBE.COM/watch?v=x"));
    }
}
