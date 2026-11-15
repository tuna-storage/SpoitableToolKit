using SpoitableToolKit.Modules.Hrs.Devices.Ble;
using SpoitableToolKit.Modules.Hrs.Devices.Models;
using Xunit;

namespace SpoitableToolKit.Tests.Hrs.Recording;

public sealed class HrsLogTests
{
    private const ulong Belt = 0xA09E1A123456;

    [Fact]
    public void ADeviceIsNewOnlyTheFirstTime()
    {
        var discovery = new BleSightings();

        Assert.Equal(Sighting.New, discovery.See(Belt, "Polar H10 1234").What);
        Assert.Equal(Sighting.Known, discovery.See(Belt, "Polar H10 1234").What);
        Assert.Single(discovery.All);
    }

    /// <summary>
    /// 一度分かった名前は手放さない。能動スキャンは名前の無い広告と名前のある応答が交互に来るので、
    /// 最後の 1 通で上書きすると名前がアドレスへ戻る。
    /// </summary>
    [Fact]
    public void AKnownNameIsNotLostToANamelessAdvertisement()
    {
        var discovery = new BleSightings();

        discovery.See(Belt, "Polar H10 1234");
        var (device, what) = discovery.See(Belt, null);

        Assert.Equal(Sighting.Known, what);
        Assert.Equal("Polar H10 1234", device.Name);
        Assert.Equal("Polar H10 1234", Assert.Single(discovery.All).Name);
    }

    [Fact]
    public void ANameThatArrivesLaterIsNotedOnce()
    {
        var discovery = new BleSightings();

        var (first, firstWhat) = discovery.See(Belt, "  ");
        Assert.Equal(Sighting.New, firstWhat);
        Assert.Equal("A0:9E:1A:12:34:56", first.Name);

        Assert.Equal(Sighting.Named, discovery.See(Belt, "Polar H10 1234").What);
        Assert.Equal(Sighting.Known, discovery.See(Belt, "Polar H10 1234").What);
    }

    [Fact]
    public void AConnectedDeviceCanNameItself()
    {
        var discovery = new BleSightings();
        var (device, _) = discovery.See(Belt, null);

        Assert.Equal(device, discovery.Rename(device, ""));

        var renamed = discovery.Rename(device, " Polar H10 ");
        Assert.Equal("Polar H10", renamed.Name);
        Assert.Equal("Polar H10", Assert.Single(discovery.All).Name);
    }

    /// <summary>
    /// 名前は近くの誰でも好きに付けられる。改行を混ぜるとアプリのログの行を偽造できるので、
    /// 制御文字と行区切りは <c>?</c> にし、長すぎる名前は切る。
    /// </summary>
    [Fact]
    public void ANameCannotBreakTheLogLine()
    {
        var discovery = new BleSightings();

        var (seen, _) = discovery.See(Belt, "Polar\n2026/11/10 ERR forged\u2028x\t");
        Assert.Equal("Polar?2026/11/10 ERR forged?x", seen.Name);

        var renamed = discovery.Rename(seen, "Belt\r\nINF fake" + new string('x', 300));
        Assert.DoesNotContain('\n', renamed.Name);
        Assert.DoesNotContain('\r', renamed.Name);
        Assert.Equal(BleSightings.MaxName + 1, renamed.Name.Length);
        Assert.EndsWith("…", renamed.Name);
    }

    /// <summary>最初の 1 通は中身を書く（読めない機器を持ち込まれたときの手がかり）。</summary>
    [Fact]
    public void TheFirstAdvertisementIsNoted()
    {
        var watch = new PolarBroadcastWatch();

        Assert.Equal(BroadcastNote.First, watch.Feed(read: true));
        Assert.Equal(BroadcastNote.None, watch.Feed(read: true));
    }

    /// <summary>
    /// 続けて 5 通読めなかったら 1 回だけ「対応していない形かも」。
    /// 4 通では書かない — 名乗りの広告が挟まるたびに書くと、読める Polar でも騒ぐ。
    /// </summary>
    [Fact]
    public void FiveMissesInARowAreWarnedOnce()
    {
        var watch = new PolarBroadcastWatch();
        watch.Feed(read: false);   // 最初の 1 通（これも 1 回目の取りこぼし）

        for (var i = 2; i < PolarBroadcastWatch.UnsupportedAfter; i++)
            Assert.Equal(BroadcastNote.None, watch.Feed(read: false));

        Assert.Equal(BroadcastNote.Unsupported, watch.Feed(read: false));
        Assert.Equal(BroadcastNote.None, watch.Feed(read: false));
    }

    [Fact]
    public void AReadingStartsTheCountAgain()
    {
        var watch = new PolarBroadcastWatch();
        watch.Feed(read: true);

        for (var i = 1; i < PolarBroadcastWatch.UnsupportedAfter; i++) watch.Feed(read: false);
        watch.Feed(read: true);

        for (var i = 1; i < PolarBroadcastWatch.UnsupportedAfter; i++)
            Assert.Equal(BroadcastNote.None, watch.Feed(read: false));
    }

    [Fact]
    public void ANewWatchStartsFromTheFirstAdvertisement()
    {
        var watch = new PolarBroadcastWatch();
        for (var i = 0; i < PolarBroadcastWatch.UnsupportedAfter; i++) watch.Feed(read: false);

        watch.Reset();

        Assert.Equal(BroadcastNote.First, watch.Feed(read: false));
        for (var i = 2; i < PolarBroadcastWatch.UnsupportedAfter; i++) watch.Feed(read: false);
        Assert.Equal(BroadcastNote.Unsupported, watch.Feed(read: false));
    }

    /// <summary>中身は、1 バイトずつ添字付きの 16 進。</summary>
    [Fact]
    public void AdvertisementsAreWrittenAsIndexedHex()
    {
        Assert.Equal(
            "company 0x006B, 3 bytes: [0]0A [1]FF [2]48; company 0x004C, 0 bytes: ",
            WinRtHeartRateSource.Describe([(0x006B, [0x0A, 0xFF, 0x48]), (0x004C, [])]));
    }

    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    /// <summary>探し始めて 15 秒で 1 回、その後は 5 分ごと。黙っていると「探していない」のか「見つからない」のか分からない。</summary>
    [Fact]
    public void StillLookingIsSaidAfterAWhileThenNowAndThen()
    {
        var watch = new SearchWatch(TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(5));

        Assert.False(watch.StillLooking(T0));
        Assert.False(watch.StillLooking(T0.AddSeconds(14)));
        Assert.True(watch.StillLooking(T0.AddSeconds(15)));
        Assert.Equal(TimeSpan.FromSeconds(15), watch.Elapsed(T0.AddSeconds(15)));

        Assert.False(watch.StillLooking(T0.AddSeconds(20)));
        Assert.False(watch.StillLooking(T0.AddSeconds(15).AddMinutes(5).AddSeconds(-1)));
        Assert.True(watch.StillLooking(T0.AddSeconds(15).AddMinutes(5)));
    }

    [Fact]
    public void FindingItStartsTheWaitAgain()
    {
        var watch = new SearchWatch(TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(5));
        watch.StillLooking(T0);
        Assert.True(watch.StillLooking(T0.AddSeconds(15)));

        watch.Found();
        Assert.Equal(TimeSpan.Zero, watch.Elapsed(T0.AddMinutes(1)));

        Assert.False(watch.StillLooking(T0.AddMinutes(1)));
        Assert.False(watch.StillLooking(T0.AddMinutes(1).AddSeconds(14)));
        Assert.True(watch.StillLooking(T0.AddMinutes(1).AddSeconds(15)));
    }

    private static readonly MonitorIdentity Sense = new(Belt, "Polar Sense");

    /// <summary>
    /// 線が落ちたら、掴んだままでも「繋がっている」と言わない（言い続けると、電源を切った心拍計が画面では受信中のまま残る）。
    /// 戻ったら同じ相手にまた繋がっている。
    /// </summary>
    [Fact]
    public void ALinkThatGoesDownIsNotConnectedUntilItComesBack()
    {
        var watch = new GattLinkWatch();
        watch.Hold(Sense);
        Assert.Equal(Sense, watch.Connected);

        Assert.Equal(LinkChange.Lost, watch.Feed(up: false));
        Assert.Null(watch.Connected);
        Assert.Equal(Sense, watch.Held);

        Assert.Equal(LinkChange.Back, watch.Feed(up: true));
        Assert.Equal(Sense, watch.Connected);
    }

    /// <summary>同じ知らせが続けて来ても、繋がりが変わるのは 1 回（受ける側は変わるたびに数字を数え直す）。</summary>
    [Fact]
    public void TheSameLinkNewsTwiceChangesTheConnectionOnce()
    {
        var watch = new GattLinkWatch();
        watch.Hold(Sense);

        Assert.Equal(LinkChange.None, watch.Feed(up: true));

        Assert.Equal(LinkChange.Lost, watch.Feed(up: false));
        Assert.Equal(LinkChange.None, watch.Feed(up: false));
        Assert.Null(watch.Connected);

        Assert.Equal(LinkChange.Back, watch.Feed(up: true));
        Assert.Equal(LinkChange.None, watch.Feed(up: true));
        Assert.Equal(Sense, watch.Connected);
    }

    /// <summary>
    /// 通知を受け始める前の上がり下がりは、繋がりを変えない。繋ぎに行っている途中にも「上がった」は来るが、
    /// そこで繋がったと言うと、まだ心拍を受ける用意ができていない。
    /// </summary>
    [Fact]
    public void LinkNewsBeforeTheNotificationsStartChangesNothing()
    {
        var watch = new GattLinkWatch();

        Assert.Equal(LinkChange.None, watch.Feed(up: true));
        Assert.Equal(LinkChange.None, watch.Feed(up: false));
        Assert.Null(watch.Connected);
        Assert.Null(watch.Held);
    }

    [Fact]
    public void LinkNewsAfterLettingGoDoesNotBringTheConnectionBack()
    {
        var watch = new GattLinkWatch();
        watch.Hold(Sense);
        watch.Feed(up: false);

        watch.Drop();

        Assert.Equal(LinkChange.None, watch.Feed(up: true));
        Assert.Null(watch.Connected);
        Assert.Null(watch.Held);
    }

    [Fact]
    public void HoldingAgainStartsWithTheLinkUp()
    {
        var watch = new GattLinkWatch();
        watch.Hold(Sense);
        watch.Feed(up: false);
        watch.Drop();

        watch.Hold(Sense);

        Assert.Equal(Sense, watch.Connected);
        Assert.Equal(LinkChange.Lost, watch.Feed(up: false));
    }

    [Fact]
    public void TheTargetIsDescribedInEnglish()
    {
        Assert.Equal("any heart rate monitor", DeviceSelector.Any.Describe());
        Assert.Equal("a device named like 'Polar'", new DeviceSelector(null, "Polar").Describe());
        Assert.Equal("A0:9E:1A:12:34:56", new DeviceSelector(Belt, null).Describe());
        Assert.Equal("Polar H10 [A0:9E:1A:12:34:56]", new DeviceSelector(Belt, "Polar H10").Describe());
    }
}
