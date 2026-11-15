using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Host.Notifications;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Notifications;

public sealed class BannerTests
{
    private static (NotificationManager Notify, Recording Sent) New(Logs? logs = null, int toastSeconds = 30)
    {
        var config = new HostConfig { ToastSeconds = toastSeconds };
        var sent = new Recording();

        return (new NotificationManager(config: config, broadcast: sent, loggerFactory: logs ?? new Logs()), sent);
    }

    [Fact]
    public void PushingSendsItToTheScreen()
    {
        var (notify, sent) = New();

        notify.PushBanner("hrs を読み込めませんでした", "BadImageFormatException");

        var banner = Assert.Single(sent.On("banner"));
        Assert.Equal("hrs を読み込めませんでした", Recording.Field(banner.Payload, "title"));
        Assert.Equal("BadImageFormatException", Recording.Field(banner.Payload, "body"));
    }

    [Fact]
    public async Task StaysUntilItIsClosed()
    {
        var (notify, sent) = New(toastSeconds: 1);

        notify.PushToast("すぐ消える");
        notify.PushBanner("読み込めませんでした");

        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Single(sent.On("toastClosed"));
        Assert.Empty(sent.On("bannerClosed"));
        Assert.Single(notify.LiveBanners());
    }

    /// <summary>モジュールの読み込みは画面より先に走り、その分の push は誰も受け取っていない。</summary>
    [Fact]
    public void LiveOnesCanBeListed()
    {
        var (notify, _) = New();

        notify.PushBanner("ひとつめ");
        notify.PushBanner("ふたつめ");

        Assert.Equal(2, notify.LiveBanners().Length);

        notify.CloseBanner(1);
        Assert.Single(notify.LiveBanners());
    }

    [Fact]
    public void ClosingTwiceSendsOnlyOne()
    {
        var (notify, sent) = New();

        notify.PushBanner("読み込めませんでした");
        notify.CloseBanner(1);
        notify.CloseBanner(1);

        Assert.Single(sent.On("bannerClosed"));
    }

    [Fact]
    public async Task AnActionRunsThenCloses()
    {
        var (notify, sent) = New();
        var ran = new TaskCompletionSource();

        notify.PushBanner("読み込めませんでした", actions: new NotifyAction("ログを見る", ran.SetResult));
        notify.InvokeBannerAction(1, 0);

        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(sent.On("bannerClosed"));
        Assert.Empty(notify.LiveBanners());
    }

    [Fact]
    public void AnActionIndexOutOfRangeDoesNothing()
    {
        var (notify, sent) = New();

        notify.PushBanner("読み込めませんでした", actions: new NotifyAction("ログを見る", () => { }));
        notify.InvokeBannerAction(1, 3);
        notify.InvokeBannerAction(999, 0);

        Assert.Empty(sent.On("bannerClosed"));
        Assert.Single(notify.LiveBanners());
    }

    [Fact]
    public async Task AThrowingActionIsLoggedNotFatal()
    {
        var logs = new Logs();
        var (notify, _) = New(logs);
        var reached = new TaskCompletionSource();

        notify.PushBanner("読み込めませんでした", actions: new NotifyAction("ログを見る", () =>
        {
            reached.SetResult();
            throw new InvalidOperationException("わざと");
        }));

        notify.InvokeBannerAction(1, 0);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));

        for (var attempt = 0; attempt < 50; attempt++)
        {
            lock (logs.Errors)
                if (logs.Errors.Count > 0) return;

            await Task.Delay(20);
        }

        Assert.Fail("押した先の例外がログに残っていない");
    }
}
