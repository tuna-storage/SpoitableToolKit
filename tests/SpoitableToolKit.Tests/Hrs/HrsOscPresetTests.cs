using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Modules.Hrs.Avatar;
using SpoitableToolKit.Modules.Hrs.Config;
using SpoitableToolKit.Modules.Hrs.Events.Session;
using SpoitableToolKit.Tests.Fakes;
using Xunit;

namespace SpoitableToolKit.Tests.Hrs;

public sealed class HrsOscPresetTests
{
    private const string P = "/avatar/parameters/";

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 15, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static IEventHub SessionEvents() =>
        new EventHubs(_ => true, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
            .CreateHub(HostOwner.Instance, [typeof(IHeartRateSessionEventListener)]);

    private static (HeartRateSession Session, FakeOscBus Osc, Clock Clock) NewSession(HrsConfig config)
    {
        var osc = new FakeOscBus();
        var clock = new Clock();
        return (new HeartRateSession(osc, config, SessionEvents(), time: clock), osc, clock);
    }

    private static HrsConfig Vrcosc() => new() { OscPreset = HrsOscPreset.VRCOSC };

    /// <summary>VRCOSC の心拍モジュールを既定の設定のまま使うアバターに合わせる。<c>Normalised</c> は 0〜240 を 0〜1 に、古い形の桁は 10 で割った値。</summary>
    [Fact]
    public void TheVrcoscPresetSendsItsNineNames()
    {
        var (session, osc, _) = NewSession(Vrcosc());
        session.Report(72);

        session.Step();

        Assert.Equal(9, osc.Sent.Count);
        Assert.Equal(true, osc.Last(P + "VRCOSC/Heartrate/Connected"));
        Assert.Equal(72, osc.Last(P + "VRCOSC/Heartrate/Value"));
        Assert.Equal(72 / 240f, osc.Last(P + "VRCOSC/Heartrate/Normalised"));
        Assert.Equal(72, osc.Last(P + "VRCOSC/Heartrate/Average"));
        Assert.Equal(true, osc.Last(P + "VRCOSC/Heartrate/Beat"));
        Assert.Equal(true, osc.Last(P + "VRCOSC/Heartrate/Enabled"));
        Assert.Equal(0.2f, osc.Last(P + "VRCOSC/Heartrate/Units"));
        Assert.Equal(0.7f, osc.Last(P + "VRCOSC/Heartrate/Tens"));
        Assert.Equal(0f, osc.Last(P + "VRCOSC/Heartrate/Hundreds"));

        Assert.DoesNotContain(osc.Sent, s => s.Address == P + "HR");
    }

    [Fact]
    public void TheNormalisedValueStaysWithinZeroAndOne()
    {
        var (session, osc, _) = NewSession(Vrcosc());
        session.Report(250);

        session.Step();

        Assert.Equal(1f, osc.Last(P + "VRCOSC/Heartrate/Normalised"));
        Assert.Equal(0.2f, osc.Last(P + "VRCOSC/Heartrate/Hundreds"));
    }

    /// <summary>VRCOSC の <c>Beat</c> は拍ごとに true と false を入れ替える。拍を下ろす手（1 拍の 2 回目の送信）では変えない（変えると 1 拍が 2 拍に見える）。</summary>
    [Fact]
    public void TheVrcoscBeatFlipsOncePerBeat()
    {
        var (session, osc, clock) = NewSession(Vrcosc());
        session.Report(60);
        var start = clock.Now;
        List<(double At, bool Value)> changes = [];
        bool? was = null;

        while (clock.Now - start < TimeSpan.FromMilliseconds(2500))
        {
            var wait = session.Step();
            var beat = (bool)osc.Last(P + "VRCOSC/Heartrate/Beat")!;
            if (beat != was) changes.Add(((clock.Now - start).TotalMilliseconds, beat));
            was = beat;
            clock.Now += wait;
        }

        Assert.Equal([(0d, true), (1000d, false), (2000d, true)], changes);
    }

    /// <summary>止まったら <c>Beat</c> も false。戻ったあとの 1 拍目は true（止まる前の続きから数えると false のまま変わらず、1 拍目が見えない）。</summary>
    [Fact]
    public void StoppingTurnsEverythingOffAndTheFirstBeatBackShows()
    {
        var (session, osc, _) = NewSession(Vrcosc());
        session.Report(60);
        session.Step();
        Assert.Equal(true, osc.Last(P + "VRCOSC/Heartrate/Beat"));

        session.Report(0);
        session.Step();

        Assert.Equal(false, osc.Last(P + "VRCOSC/Heartrate/Connected"));
        Assert.Equal(false, osc.Last(P + "VRCOSC/Heartrate/Enabled"));
        Assert.Equal(false, osc.Last(P + "VRCOSC/Heartrate/Beat"));
        Assert.Equal(0, osc.Last(P + "VRCOSC/Heartrate/Value"));
        Assert.Equal(0, osc.Last(P + "VRCOSC/Heartrate/Average"));
        Assert.Equal(0f, osc.Last(P + "VRCOSC/Heartrate/Normalised"));
        Assert.Equal(0f, osc.Last(P + "VRCOSC/Heartrate/Units"));

        session.Report(70);
        session.Step();
        Assert.Equal(true, osc.Last(P + "VRCOSC/Heartrate/Beat"));
    }

    [Fact]
    public void VrcoscNamesComeFromTheSettings()
    {
        var config = Vrcosc();
        config.VrcoscParameters.Value = "MyHeartRate";
        var (session, osc, _) = NewSession(config);
        session.Report(72);

        session.Step();

        Assert.Equal(72, osc.Last(P + "MyHeartRate"));
        Assert.Equal(9, osc.Sent.Count);
    }

    [Fact]
    public void TheAverageIsOverTheLastTenSeconds()
    {
        var (session, osc, clock) = NewSession(Vrcosc());
        var average = P + "VRCOSC/Heartrate/Average";

        session.Report(60);
        clock.Now += TimeSpan.FromSeconds(5);
        session.Report(80);
        session.Step();
        Assert.Equal(70, osc.Last(average));

        clock.Now += TimeSpan.FromSeconds(6);
        session.Report(90);
        session.Step();
        Assert.Equal(85, osc.Last(average));
    }

    /// <summary>窓の中に 1 つも無いとき（まだ止まったとみなしていない）は、いまの心拍を平均として送る。0 にすると、止まっていないのに平均だけ 0 になる。</summary>
    [Fact]
    public void AnEmptyWindowSendsTheCurrentHeartRateAsTheAverage()
    {
        var (session, osc, clock) = NewSession(Vrcosc());
        session.Report(66);
        clock.Now += TimeSpan.FromSeconds(30);

        session.Step();

        Assert.Equal(66, osc.Last(P + "VRCOSC/Heartrate/Average"));
    }

    [Fact]
    public void TheAverageStartsAgainAfterAStop()
    {
        var (session, osc, _) = NewSession(Vrcosc());
        session.Report(120);
        session.Step();

        session.Lost();
        session.Step();
        session.Report(60);
        session.Step();

        Assert.Equal(60, osc.Last(P + "VRCOSC/Heartrate/Average"));
    }

    /// <summary>送らずに替えると、前の名前は最後の心拍を出したまま残る。</summary>
    [Fact]
    public void SwitchingThePresetSendsStoppedToTheOldNamesFirst()
    {
        var config = new HrsConfig();
        var (session, osc, _) = NewSession(config);
        session.Report(72);
        session.Step();
        Assert.Equal(true, osc.Last(P + "isHRConnected"));

        config.OscPreset = HrsOscPreset.VRCOSC;
        session.Step();

        Assert.Equal(false, osc.Last(P + "isHRConnected"));
        Assert.Equal(0, osc.Last(P + "HR"));
        Assert.Equal(true, osc.Last(P + "VRCOSC/Heartrate/Connected"));
        Assert.Equal(72, osc.Last(P + "VRCOSC/Heartrate/Value"));
    }

    [Fact]
    public void RenamingWhileLiveSendsStoppedToTheOldName()
    {
        var config = new HrsConfig();
        var (session, osc, _) = NewSession(config);
        session.Report(72);
        session.Step();

        config.Parameters.HeartRate = "HR2";
        session.Step();

        Assert.Equal(0, osc.Last(P + "HR"));
        Assert.Equal(72, osc.Last(P + "HR2"));
    }

    /// <summary>設定画面にも VRCHR の名前の欄を出す（送っている名前の欄がどこにも出ないと、直す手が無い）。</summary>
    [Fact]
    public void AnUnknownPresetSendsAsVrchr()
    {
        var config = new HrsConfig { OscPreset = (HrsOscPreset)99 };
        var (session, osc, _) = NewSession(config);
        session.Report(72);

        session.Step();

        Assert.Equal(72, osc.Last(P + "HR"));
        Assert.True(config.Parameters.IsEnabled);
        Assert.False(config.VrcoscParameters.IsEnabled);
    }

    private sealed class StopCounter : IHeartRateSessionEventListener
    {
        public int Count;

        public void OnStopped(HeartRateSessionStoppedEventParams e) => Count++;
    }

    /// <summary>配信へは 2 回目の「止まった」を配らない（配ると、同じ「止まった」が配信に 2 回出る）。</summary>
    [Fact]
    public void RenamingWhileStoppedTellsTheNewNameWithoutAnnouncingAgain()
    {
        var config = new HrsConfig();
        var hub = SessionEvents();
        var stops = new StopCounter();
        hub.Source.AddListener<IHeartRateSessionEventListener>(HostOwner.Instance, stops);
        var osc = new FakeOscBus();
        var session = new HeartRateSession(osc, config, hub, time: new Clock());

        session.Step();
        Assert.Equal(1, stops.Count);

        config.Parameters.IsActive = "myActive";
        session.Step();
        session.Step();

        Assert.Equal(false, osc.Last(P + "myActive"));
        Assert.Equal(1, stops.Count);
    }

    /// <summary>変えていない名前へ「止まった」を送ると、本当の値で上書きするまでの間、アバターに一瞬「繋がっていない」が見える。</summary>
    [Fact]
    public void RenamingOneNameLeavesTheOthersAlone()
    {
        var config = new HrsConfig();
        var (session, osc, _) = NewSession(config);
        session.Report(72);
        session.Step();
        osc.Sent.Clear();

        config.Parameters.HeartRate = "HR2";
        session.Step();

        Assert.DoesNotContain(osc.Sent, s => s.Address == P + "isHRConnected" && s.Args[0] is false);
        Assert.Equal(0, osc.Last(P + "HR"));
        Assert.Equal(72, osc.Last(P + "HR2"));
    }

    [Fact]
    public void OnlyTheChosenPresetsNamesAreShown()
    {
        var config = new HrsConfig();
        Assert.Equal(HrsOscPreset.VRCHR, config.OscPreset);
        Assert.True(config.Parameters.IsEnabled);
        Assert.False(config.VrcoscParameters.IsEnabled);

        config.OscPreset = HrsOscPreset.VRCOSC;
        Assert.False(config.Parameters.IsEnabled);
        Assert.True(config.VrcoscParameters.IsEnabled);
    }

    /// <summary>名前の途中の <c>/</c> は通す（VRCOSC の既定は <c>VRCOSC/Heartrate/Value</c> で 1 つの名前）。頭か終わりの <c>/</c> と <c>//</c> は空の段ができるので拒む。</summary>
    [Theory]
    [InlineData("VRCOSC/Heartrate/Value", null)]
    [InlineData("HR", null)]
    [InlineData("", "hrs.error.paramName.empty")]
    [InlineData("/HR", "hrs.error.paramName.slash")]
    [InlineData("HR/", "hrs.error.paramName.slash")]
    [InlineData("H//R", "hrs.error.paramName.slash")]
    public void ASlashIsFineInsideAName(string name, string? reason) =>
        Assert.Equal(reason, ParameterNames.ErrorOf(name));

    /// <summary>通らないと、読み込みのたびに「修復」されて同じ既定に戻る。</summary>
    [Fact]
    public void TheVrcoscDefaultsAreAllUsable() =>
        Assert.Empty(ParameterNames.ErrorsIn(new HrsConfig()));
}
