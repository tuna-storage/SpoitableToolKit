using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Modules.Hrs.Avatar;
using SpoitableToolKit.Modules.Hrs.Avatar.Presets;
using SpoitableToolKit.Modules.Hrs.Config;
using SpoitableToolKit.Modules.Hrs.Events.Session;
using SpoitableToolKit.Tests.Fakes;
using Xunit;

namespace SpoitableToolKit.Tests.Hrs;

public sealed class HrsSmoothingTests
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

    private static (HeartRateSession Session, FakeOscBus Osc, Clock Clock) NewSession(HrsConfig? config = null)
    {
        var osc = new FakeOscBus();
        var clock = new Clock();
        return (new HeartRateSession(osc, config ?? new HrsConfig(), SessionEvents(), time: clock), osc, clock);
    }

    /// <summary>時計を進めながら <paramref name="length"/> だけ回し、手ごとに読んだ値を返す。</summary>
    private static List<T> Run<T>(HeartRateSession session, Clock clock, TimeSpan length, Func<T> read)
    {
        var start = clock.Now;
        List<T> seen = [];
        while (clock.Now - start < length)
        {
            var wait = session.Step();
            seen.Add(read());
            clock.Now += wait;
        }

        return seen;
    }

    /// <summary>行き過ぎず、1 秒でほぼ寄り切る（140 bpm の差でも 1 秒後には 1 bpm より近い）。50ms ずつでも 1 回で 1 秒でも同じ所へ行く。</summary>
    [Fact]
    public void ItGetsWithinOneBpmInASecondWithoutOvershooting()
    {
        float? value = 60f;
        List<float> seen = [];
        for (var i = 0; i < 20; i++)
        {
            value = HeartRateSmoothing.Approach(value, 200f, TimeSpan.FromMilliseconds(50), smooth: true);
            seen.Add(value.Value);
        }

        Assert.All(seen, v => Assert.InRange(v, 60f, 200f));
        Assert.Equal(seen.Order(), seen);
        Assert.InRange(seen[^1], 199f, 200f);

        var once = HeartRateSmoothing.Approach(60f, 200f, TimeSpan.FromSeconds(1), smooth: true);
        Assert.InRange(once, seen[^1] - 0.01f, seen[^1] + 0.01f);
    }

    [Fact]
    public void TheEdgesOfApproaching()
    {
        Assert.Equal(90f, HeartRateSmoothing.Approach(null, 90f, TimeSpan.FromMilliseconds(50), smooth: true));
        Assert.Equal(60f, HeartRateSmoothing.Approach(60f, 90f, TimeSpan.Zero, smooth: true));
        Assert.Equal(90f, HeartRateSmoothing.Approach(60f, 90f, TimeSpan.FromMilliseconds(50), smooth: false));
    }

    [Fact]
    public void ANewHeartRateGlidesInsteadOfJumping()
    {
        var (session, osc, clock) = NewSession();
        session.Report(60);
        session.Step();
        clock.Now += TimeSpan.FromMilliseconds(50);
        session.Report(120);

        var heartRates = Run(session, clock, TimeSpan.FromSeconds(1.2), () => (int)osc.Last(P + "HR")!);

        Assert.InRange(heartRates[0], 61, 119);
        Assert.Equal(heartRates.Order(), heartRates);
        Assert.All(heartRates, v => Assert.InRange(v, 60, 120));
        Assert.Equal(120, heartRates[^1]);

        // 割合は寄せている途中の小数のまま割る（120/255 を越えない）。
        Assert.InRange((float)osc.Last(P + "HRPercent")!, 119f / 255f, 120f / 255f);
    }

    [Fact]
    public void WithSmoothingOffANewHeartRateIsSentAsItIs()
    {
        var (session, osc, clock) = NewSession(new HrsConfig { SmoothValues = false });
        session.Report(60);
        session.Step();
        clock.Now += TimeSpan.FromMilliseconds(50);
        session.Report(120);

        session.Step();

        Assert.Equal(120, osc.Last(P + "HR"));
    }

    /// <summary>止まったあとの 1 つ目は 0 から寄せない（寄せると、戻るたびに数字が 0 から駆け上がる）。</summary>
    [Fact]
    public void TheFirstHeartRateAfterAStopIsNotRampedFromZero()
    {
        var (session, osc, clock) = NewSession();
        session.Report(60);
        session.Step();
        session.Lost();
        clock.Now += TimeSpan.FromMilliseconds(50);
        session.Step();
        Assert.Equal(0, osc.Last(P + "HR"));

        clock.Now += TimeSpan.FromMilliseconds(500);
        session.Report(90);
        session.Step();

        Assert.Equal(90, osc.Last(P + "HR"));
    }

    /// <summary>同じ瞬間に 2 回起きても値は動かない（寄せる量は手と手の間の時間で決める）。</summary>
    [Fact]
    public void TwoStepsAtTheSameInstantDoNotMoveTheValue()
    {
        var (session, osc, _) = NewSession();
        session.Report(60);
        session.Step();
        session.Report(120);

        session.Step();

        Assert.Equal(60, osc.Last(P + "HR"));
    }

    [Fact]
    public void TheAverageGlidesToo()
    {
        var (session, osc, clock) = NewSession(new HrsConfig { OscPreset = HrsOscPreset.VRCOSC });
        session.Report(60);
        session.Step();
        Assert.Equal(60, osc.Last(P + "VRCOSC/Heartrate/Average"));

        clock.Now += TimeSpan.FromMilliseconds(50);
        session.Report(100);
        session.Step();

        // 窓の平均は 80。50ms ではまだ寄り切らない。
        Assert.InRange((int)osc.Last(P + "VRCOSC/Heartrate/Average")!, 61, 79);
    }

    /// <summary>
    /// 拍は自分の時刻に立つ（50ms の刻みに丸めない）。72 bpm の周期は 833.3…ms で 50ms の倍数にならず、
    /// 刻みでしか起きないと、拍が最大 50ms 遅れて周期が揺れる。
    /// </summary>
    [Fact]
    public void TheBeatStartsOnItsOwnTimeNotOnTheFiftyMillisecondTick()
    {
        var (session, osc, clock) = NewSession();
        session.Report(72);
        var start = clock.Now;
        List<TimeSpan> ups = [];
        var was = false;

        while (clock.Now - start < TimeSpan.FromMilliseconds(1800))
        {
            var wait = session.Step();
            var beat = (bool)osc.Last(P + "isHRBeat")!;
            if (beat && !was) ups.Add(clock.Now - start);
            was = beat;
            clock.Now += wait;
        }

        var cycle = TimeSpan.FromMilliseconds(60_000d / 72);
        Assert.Equal([TimeSpan.Zero, cycle, cycle + cycle], ups);
    }

    /// <summary>
    /// 起きるのが毎回遅れても（Windows の待ちは数 ms 遅れて起きる）、<b>拍は予定の時刻から数えるので、周期に遅れが積もらない</b>。
    /// 起きた時刻から数えると、72 bpm が 71 bpm 相当まで遅くなる。
    /// </summary>
    [Fact]
    public void ALateWakeUpDoesNotStretchTheCycle()
    {
        var (session, osc, clock) = NewSession();
        session.Report(72);
        var start = clock.Now;
        List<TimeSpan> ups = [];
        var was = false;

        while (ups.Count < 10)
        {
            var wait = session.Step();
            var beat = (bool)osc.Last(P + "isHRBeat")!;
            if (beat && !was) ups.Add(clock.Now - start);
            was = beat;
            clock.Now += wait + TimeSpan.FromMilliseconds(7);
        }

        var cycle = TimeSpan.FromMilliseconds(60_000d / 72);
        for (var k = 0; k < ups.Count; k++)
            Assert.InRange(ups[k] - cycle * k, TimeSpan.Zero, TimeSpan.FromMilliseconds(15));
    }

    /// <summary>
    /// 半周期より大きく遅れて起きたとき（スリープ明けなど）は、取り返そうとして拍を詰めない。予定から数え続けると、
    /// 過ぎてしまった拍の分だけ起きた直後に拍が続けて立ち、拍が立ちっぱなしになる（立ち上がりの時刻だけ見ていると気付けない）。
    /// </summary>
    [Fact]
    public void ALongSleepDoesNotBunchTheBeats()
    {
        var (session, osc, clock) = NewSession();
        session.Report(60);
        session.Step();
        clock.Now += TimeSpan.FromSeconds(5);
        var start = clock.Now;
        List<double> ups = [], downs = [];
        var was = false;

        while (clock.Now - start < TimeSpan.FromMilliseconds(1050))
        {
            var wait = session.Step();
            var beat = (bool)osc.Last(P + "isHRBeat")!;
            var at = (clock.Now - start).TotalMilliseconds;
            if (beat && !was) ups.Add(at);
            if (!beat && was) downs.Add(at);
            was = beat;
            clock.Now += wait;
        }

        Assert.Equal([0d, 1000d], ups);
        Assert.Equal([100d], downs);
    }

    /// <summary>
    /// <b>時計が戻っても拍は止まらない</b>（手で直した・NTP の大きな補正）。戻る前の予定のままだと、戻った時間ぶん次の拍が立たず、
    /// 立っていた拍も下りない。
    /// </summary>
    [Fact]
    public void TheClockGoingBackDoesNotHoldTheBeat()
    {
        var (session, osc, clock) = NewSession();
        session.Report(60);
        session.Step();
        clock.Now -= TimeSpan.FromHours(1);
        var start = clock.Now;
        List<double> ups = [], downs = [];
        var was = true;

        while (clock.Now - start < TimeSpan.FromMilliseconds(2050))
        {
            var wait = session.Step();
            var beat = (bool)osc.Last(P + "isHRBeat")!;
            var at = (clock.Now - start).TotalMilliseconds;
            if (beat && !was) ups.Add(at);
            if (!beat && was) downs.Add(at);
            was = beat;
            clock.Now += wait;
        }

        Assert.Equal([100d, 1100d], downs);
        Assert.Equal([1000d, 2000d], ups);
    }

    /// <summary>
    /// 整数で送る値は<b>1 つに丸めた心拍から取る</b>。桁ごとに丸めると、79.6 が「十の位 7・一の位 0」（70 に見える）になったりする。
    /// </summary>
    [Fact]
    public void TheDigitsComeFromOneRoundedHeartRate()
    {
        var info = new HeartRateInfo(79.6f, 79.6f, false, 0);

        var vrchr = new FakeOscBus();
        VrchrSender.From(new HrsConfig().Parameters).Send(vrchr, info);
        Assert.Equal(80, vrchr.Last(P + "HR"));
        Assert.Equal(8, vrchr.Last(P + "tensHR"));
        Assert.Equal(0, vrchr.Last(P + "onesHR"));

        // 割合は丸める前の小数のまま割る（メーターがなめらかに動く）。
        Assert.Equal(79.6f / 255f, vrchr.Last(P + "HRPercent"));

        var vrcosc = new FakeOscBus();
        VrcoscSender.From(new HrsConfig().VrcoscParameters).Send(vrcosc, info);
        Assert.Equal(79.6f / 240f, vrcosc.Last(P + "VRCOSC/Heartrate/Normalised"));
        Assert.Equal(80, vrcosc.Last(P + "VRCOSC/Heartrate/Value"));
        Assert.Equal(80, vrcosc.Last(P + "VRCOSC/Heartrate/Average"));
        Assert.Equal(0.8f, vrcosc.Last(P + "VRCOSC/Heartrate/Tens"));
        Assert.Equal(0f, vrcosc.Last(P + "VRCOSC/Heartrate/Units"));
    }
}
