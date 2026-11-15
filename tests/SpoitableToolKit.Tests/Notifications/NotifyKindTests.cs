using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Host.Notifications;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Notifications;

public sealed class NotifyKindTests
{
    private static NotifyKind Custom(string path) => new("test.kind", NotifyTone.Rose, path);

    private static string? Icon(object payload) => (string?)Recording.Field(payload, "icon");

    [Fact]
    public void ARealPathIsSafe() =>
        Assert.True(NotifyKind.IsSafePath("M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18ZM12 11v5M12 7.5v.01"));

    /// <summary>同梱の種類が自分の検査で落ちると、全部の通知が情報の印になってしまう。</summary>
    [Fact]
    public void EveryBuiltInIconIsSafe()
    {
        foreach (var kind in new[] { NotifyKind.Info, NotifyKind.Done, NotifyKind.Warn, NotifyKind.Error, NotifyKind.Download })
            Assert.True(NotifyKind.IsSafePath(kind.IconPath), kind.Id);
    }

    /// <summary>足りない字があると、その命令を使った絵が弾かれる。</summary>
    [Theory]
    [InlineData("MmLlHhVvCcSsQqTtAaZz")]
    [InlineData("0123456789")]
    [InlineData(".,- ")]
    public void EveryAllowedCharacterIsAccepted(string path) =>
        Assert.True(NotifyKind.IsSafePath(path));

    // 悪い字は 1 つずつ入れる。"M0 0<script>" のような例は s・r・i・p でも落ちるので、`<` を通すように壊しても赤くならない。
    [Theory]
    [InlineData("M0 0<")]
    [InlineData("M0 0\"")]
    [InlineData("M0 0<script>")]
    [InlineData("M0 0\"onload=\"x")]
    [InlineData("M0 0x")]
    [InlineData("url(#a)")]
    [InlineData("M0 0'")]
    [InlineData("M0 0;")]
    [InlineData("M0 0>")]
    [InlineData("M0\n0")]
    public void AnythingOutsideThePathAlphabetIsRejected(string path) =>
        Assert.False(NotifyKind.IsSafePath(path));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingIsNotAPath(string? path) =>
        Assert.False(NotifyKind.IsSafePath(path));

    [Fact]
    public void TheLengthLimitIs2048()
    {
        Assert.True(NotifyKind.IsSafePath(new string('1', 2048)));
        Assert.False(NotifyKind.IsSafePath(new string('1', 2049)));
    }

    [Fact]
    public void AnUnsafeIconFallsBackToTheInfoIconOnTheScreen()
    {
        var payload = NotificationManager.KindPayload(Custom("M0 0\"><script>"));

        Assert.Equal(NotifyKind.Info.IconPath, Icon(payload));

        Assert.Equal("test.kind", Recording.Field(payload, "id"));
        Assert.Equal("rose", Recording.Field(payload, "tone"));
    }

    [Fact]
    public void ASafeCustomIconIsPassedAsIs()
    {
        var payload = NotificationManager.KindPayload(Custom("M19 14c1.5-1.5 3-3.2 3-5.5"));

        Assert.Equal("M19 14c1.5-1.5 3-3.2 3-5.5", Icon(payload));
    }

    [Fact]
    public async Task EverySurfaceThatShowsAKindChecksTheIcon()
    {
        var notify = new NotificationManager(new Recording(), new HostConfig(), NullLoggerFactory.Instance);
        var bad = Custom("<img>");

        notify.PushToast(bad, "toast");
        notify.PushBanner(bad, "banner");
        var asking = notify.PushModalAsync(new NotifyRequest { Title = "ask", Kind = bad });

        var kinds = new[]
        {
            Recording.Field(Assert.Single(notify.LiveToasts()), "kind")!,
            Recording.Field(Assert.Single(notify.LiveBanners()), "kind")!,
            Recording.Field(Assert.Single(notify.LiveAsks()), "kind")!,
        };
        foreach (var kind in kinds) Assert.Equal(NotifyKind.Info.IconPath, Icon(kind));

        foreach (var entry in notify.History())
            Assert.Equal(NotifyKind.Info.IconPath, Icon(Recording.Field(entry, "kind")!));

        notify.CancelAll();
        await asking;
    }
}
