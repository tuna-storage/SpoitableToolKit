using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Shell;

/// <summary>途中に fake は無い（別 ALC の videourl モジュールまで実物で繋がっている）。「はい」は押さない（yt-dlp と deno を取りに行って実際に解決を始めるため）。</summary>
public sealed class NotificationTests(AppFixture app) : IClassFixture<AppFixture>
{
    private IPage Page => app.Page;

    private const string Url = "https://www.youtube.com/watch?v=e2eFailure";

    /// <summary>他の試験の確認と混ざらないよう、本文で絞る。</summary>
    private ILocator Ask(string url) => Page.Locator("#asks .ask", new() { HasTextString = url });

    private Task<double> LifeWidth(ILocator ask) =>
        ask.Locator(".life").EvaluateAsync<double>("e => e.getBoundingClientRect().width");

    [Fact]
    public async Task DetectingAPlaybackFailureAsksAboutIt()
    {
        app.AppendLog(
            $"[Video Playback] Attempting to resolve URL '{Url}'",
            $"[Video Playback] URL '{Url}' resolved to 'https://e2e.invalid/direct'",
            "[AVProVideo] Opening https://e2e.invalid/direct (offset 0) with API MediaFoundation",
            "[AVProVideo] Error: Loading failed.  e2e");

        var ask = Ask(Url);
        await Assertions.Expect(ask).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await Assertions.Expect(ask.Locator(".title")).ToContainTextAsync("再生に失敗");

        await Assertions.Expect(ask.Locator(".yes")).ToContainTextAsync("コピー");

        await Assertions.Expect(ask.Locator(".optout")).ToBeVisibleAsync();

        var first = await LifeWidth(ask);
        await Task.Delay(500);
        var later = await LifeWidth(ask);
        Assert.True(later < first, $"残り時間の線が縮んでいない（{first} → {later}）");

        await ask.Locator(".no").ClickAsync();
        await Assertions.Expect(ask).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task ARepeatOfTheSameUrlIsNotAskedAboutAgain()
    {
        const string url = "https://www.youtube.com/watch?v=e2eRetry";

        // VRChat は失敗すると同じ URL でやり直す。聞かれるのは最初の 1 回だけ。
        for (var attempt = 0; attempt < 3; attempt++)
        {
            app.AppendLog(
                $"[Video Playback] Attempting to resolve URL '{url}'",
                $"[Video Playback] URL '{url}' resolved to 'https://e2e.invalid/{attempt}'",
                $"[AVProVideo] Opening https://e2e.invalid/{attempt} (offset 0) with API MediaFoundation",
                "[AVProVideo] Error: Loading failed.  e2e");
        }

        var asks = Ask(url);
        await Assertions.Expect(asks).ToHaveCountAsync(1, new() { Timeout = 20_000 });

        // 後から 2 枚目が来ないことを待って見る（確認は同時に何枚も並べられる）。
        await Task.Delay(1_500);
        await Assertions.Expect(asks).ToHaveCountAsync(1);

        await asks.Locator(".no").ClickAsync();
        await Assertions.Expect(asks).ToHaveCountAsync(0);
    }
}
