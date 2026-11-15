using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Host.Status;
using SpoitableToolKit.Tests.Fakes;
using Xunit;

namespace SpoitableToolKit.Tests.Status;

public sealed class LogReadingStatusTests
{
    /// <summary>作った時点で書いてある。監視が購読より先にファイルを見つけていると「変わった」の知らせは来ず、書かないと VRChat が止まっている間ずっと空のまま。</summary>
    [Fact]
    public void AFileFoundBeforeSubscribingIsShownAtOnce()
    {
        var reading = new LogReadingStatus(() => @"C:\logs\output_log_2026-11-09_21-00-00.txt", new FakeLocalizer());

        Assert.Equal("host.status.log.reading", reading.Status.Text);
        Assert.Equal("output_log_2026-11-09_21-00-00.txt", reading.Status.Note);
        Assert.Equal(StatusState.Ok, reading.Status.State);
    }

    /// <summary>ログが 1 つも無い人には「見つかりません」。知らせは来ないので作った時点で書く（空のままだと、読んでいないのか壊れているのか分からない）。</summary>
    [Fact]
    public void WithNoLogItSaysSoAtOnce()
    {
        var reading = new LogReadingStatus(() => null, new FakeLocalizer());

        Assert.Equal("host.status.log.none", reading.Status.Text);
        Assert.Null(reading.Status.Note);
        Assert.Equal(StatusState.Off, reading.Status.State);
    }

    /// <summary>VRChat が動いていなければ「VRChat は動いていません」。ログのファイルは残っているので、閉じたあとも「読んでいます」のままにならないようにする。起動したら書き直す。</summary>
    [Fact]
    public void WhenVRChatIsNotRunningTheTitleSaysSo()
    {
        var running = false;
        var reading = new LogReadingStatus(() => @"C:\logs\output_log_2026-11-12_12-00-00.txt", new FakeLocalizer(), () => running);

        Assert.Equal("host.status.vrchat.off", reading.Status.Text);
        Assert.Null(reading.Status.Note);
        Assert.Equal(StatusState.Off, reading.Status.State);

        running = true;
        reading.Show();

        Assert.Equal("host.status.log.reading", reading.Status.Text);
        Assert.Equal(StatusState.Ok, reading.Status.State);
    }

    [Fact]
    public void ShowFollowsTheCurrentFile()
    {
        string? file = null;
        var reading = new LogReadingStatus(() => file, new FakeLocalizer());

        file = @"C:\logs\output_log_b.txt";
        reading.Show();
        Assert.Equal("output_log_b.txt", reading.Status.Note);
        Assert.Equal(StatusState.Ok, reading.Status.State);

        file = null;
        reading.Show();
        Assert.Equal("host.status.log.none", reading.Status.Text);
        Assert.Equal(StatusState.Off, reading.Status.State);
    }
}
