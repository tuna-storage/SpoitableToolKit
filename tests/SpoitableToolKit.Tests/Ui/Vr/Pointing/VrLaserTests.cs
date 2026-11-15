using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Ui.Vr.Overlays;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Pointing;
using SpoitableToolKit.Tests.Ui.Vr.Wrist;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Vr.Pointing;

public class VrLaserTests
{
    private const uint BoardDevice = 1;

    /// <summary>板の付いた機器。頭の前、少し下。回していない。</summary>
    private static readonly VrMatrix BoardDevicePose = VrMatrix.Translation(0, 1.2f, -0.5f);

    /// <summary>機器から見た板の置き場（少し上）。</summary>
    private static readonly VrMatrix Offset = VrMatrix.Translation(0, 0.05f, 0);

    private sealed class Rig
    {
        public LaserOverlays Overlays { get; } = new();

        public List<string> Sent { get; } = [];

        /// <summary>巻きを送った所（<c>x,y</c>）。<see cref="Sent"/> には巻きの量しか書かないので、別に取る。</summary>
        public List<string> PanAt { get; } = [];

        public bool Scrolls { get; init; }

        /// <summary>いま掴む手（cursor: grab）の上か。<b>周の途中でも変えられる</b>（掴んだあとにカーソルが変わる）。</summary>
        public bool Grab { get; set; }

        /// <summary>今の時刻（ミリ秒）。<b>長押しはこれで測る</b>。</summary>
        public long Now { get; set; }

        public VrLaser Laser { get; }

        public Rig()
        {
            Laser = new VrLaser(Overlays, NullLogger.Instance, "test.laser", () => Now);
            Laser.Start();
        }

        public VrLaserTarget Target => Board(7, BoardDevice, BoardDevicePose, Offset, Sent, Scrolls, () => Grab, PanAt);

        /// <summary>板 1 枚（横 20 cm・縦 10 cm、640×320）。<paramref name="device"/> が null なら空間に置いた板。</summary>
        public static VrLaserTarget Board(
            ulong handle, uint? device, VrMatrix devicePose, VrMatrix offset, List<string> sent, bool scrolls = false, Func<bool>? grabbable = null,
            List<string>? panAt = null) => new(
            handle, device, devicePose, offset, 0.2f, 0.1f, 640, 320,
            (kind, x, y, button, wheelX, wheelY) =>
            {
                sent.Add(
                    kind == VrPointerKind.Pan ? $"Pan {wheelX},{wheelY}"
                    : button == VrPointerButton.Right ? $"{kind} {x},{y} Right"
                    : $"{kind} {x},{y}");

                if (kind == VrPointerKind.Pan) panAt?.Add($"{x},{y}");
            },
            () => false, scrolls, "test board", Grabbable: grabbable);

        /// <summary>
        /// 板の中心から見て (x, y) を、手前 <paramref name="distance"/> から真っ直ぐ指す狙い。
        /// 既定は<b>画素 (320, 160) の中ほど</b>（中心ちょうどは画素の境目で、端数でどちらに落ちるか決まらない）。
        /// </summary>
        public static VrMatrix AimAt(float x = 0.0003f, float y = -0.0003f, float distance = 0.3f) =>
            BoardDevicePose.Then(Offset).Then(VrMatrix.Translation(x, y, distance));

        public VrLaserStep Tick(VrHandInput input, VrLaserTarget? target = null) =>
            Laser.Tick(VrHand.Right, input, [target ?? Target]);

        public VrLaserStep Point(VrMatrix? aim = null, bool click = false, bool grab = false) =>
            Tick(new VrHandInput(aim ?? AimAt(), click, grab));
    }

    /// <summary>点は板の付いた機器に付ける（絶対の位置に置くと、狙いと板の姿勢の取り方の違いで板の上で震える）。点は当たった所の 2 mm 手前。</summary>
    [Fact]
    public void TheDotHangsFromTheBoardsDeviceWhereItHits()
    {
        var rig = new Rig();

        rig.Point(Rig.AimAt(0.03f, -0.01f));

        var (dotDevice, dotOffset) = rig.Overlays.SpritesAt[rig.Overlays.Dot];

        Assert.Equal(BoardDevice, dotDevice);

        var (x, y, z) = dotOffset.Position;
        var (ex, ey, ez) = Offset.Then(VrMatrix.Translation(0.03f, -0.01f, VrLaser.DotLift)).Position;
        Assert.Equal(ex, x, 1e-5f);
        Assert.Equal(ey, y, 1e-5f);
        Assert.Equal(ez, z, 1e-5f);
    }

    [Fact]
    public void PointingShowsTheDotAndLeavingHidesIt()
    {
        var rig = new Rig();

        rig.Point();
        Assert.True(rig.Laser.Visible);
        Assert.True(rig.Overlays.SpritesShown[rig.Overlays.Dot]);

        rig.Tick(default);
        Assert.False(rig.Laser.Visible);
        Assert.False(rig.Overlays.SpritesShown[rig.Overlays.Dot]);
    }

    [Fact]
    public void WithoutTheSpritesPointingStillWorks()
    {
        var rig = new Rig();
        rig.Laser.Stop();
        rig.Overlays.SpritesFail = true;
        rig.Laser.Start();

        rig.Point();
        rig.Point(click: true);

        Assert.Equal(["Move 320,160", "Down 320,160"], rig.Sent);
        Assert.Empty(rig.Overlays.SpritesAt);
        Assert.False(rig.Laser.Visible);
    }

    /// <summary>入るのは板 + 1 cm、出るのは板 + 3 cm（縁を指して止めたときに、出たり消えたりを繰り返さない）。</summary>
    [Fact]
    public void ItKeepsPointingUntilTheWiderMargin()
    {
        var rig = new Rig();
        var twoOutside = Rig.AimAt(0.1f + 0.02f, 0);

        rig.Point(twoOutside);
        Assert.False(rig.Laser.Pointing, "2 cm 外から指し始めている");

        rig.Point();
        rig.Point(twoOutside);
        Assert.True(rig.Laser.Pointing, "指している途中の 2 cm 外で外れている");

        rig.Point(Rig.AimAt(0.1f + 0.04f, 0));
        Assert.False(rig.Laser.Pointing);
    }

    [Fact]
    public void StoppingLetsGoAndPutsTheSpritesAway()
    {
        var rig = new Rig();
        rig.Point();
        rig.Point(click: true);

        rig.Laser.Stop();

        Assert.Equal(["Move 320,160", "Down 320,160", "Up 320,160", "Leave 320,160"], rig.Sent);
        Assert.Equal(1, rig.Overlays.SpritesDestroyed);
    }

    /// <summary>動かなかった周は Move を送らない（同じ画素へ毎周送ると、CEF が毎周 hover を計算し直す）。</summary>
    [Fact]
    public void StandingStillSendsNoMoves()
    {
        var rig = new Rig();

        rig.Point();
        rig.Point();
        rig.Point();

        Assert.Equal(["Move 320,160"], rig.Sent);
    }

    [Fact]
    public void AGrabIsReportedOnceWhenPressedWhilePointing()
    {
        var rig = new Rig();

        Assert.False(rig.Point().GrabPressed);
        Assert.True(rig.Point(grab: true).GrabPressed);
        Assert.False(rig.Point(grab: true).GrabPressed);
    }

    [Fact]
    public void AGrabWhileNotPointingIsNotReported()
    {
        var rig = new Rig();

        var step = rig.Tick(new VrHandInput(Rig.AimAt(0.5f, 0), false, true));

        Assert.False(step.GrabPressed);
    }

    [Fact]
    public void ATapOnAScrollingBoardClicksWhereItStarted()
    {
        var rig = new Rig { Scrolls = true };

        rig.Point();
        rig.Point(click: true);
        Assert.Equal(["Move 320,160"], rig.Sent);

        rig.Point();
        Assert.Equal(["Move 320,160", "Down 320,160", "Up 320,160"], rig.Sent);
    }

    /// <summary>
    /// 引いたまま板の上で 1.2 cm 動かしたら巻く。ホイールの量は動いた画素 × 1.2 で、下へ動かすと正（上へ巻く = 中身が指に付いて下へ動く）。
    /// 巻いたら、離してもクリックにしない（巻き終わりの所のボタンが押される）。
    /// </summary>
    [Fact]
    public void MovingWhileHeldScrollsInsteadOfClicking()
    {
        var rig = new Rig { Scrolls = true };

        rig.Point();
        rig.Point(click: true);

        // 2 cm 下 = 64 画素（3200 画素 / m）→ 76.8 → 77。
        rig.Point(Rig.AimAt(y: -0.0203f), click: true);
        Assert.Equal(["Move 320,160", "Move 320,224", "Pan 0,77"], rig.Sent);

        // **巻いた所から数える**（引き始めた所からではない）: さらに 1 cm = 32 画素 → 38。
        rig.Point(Rig.AimAt(y: -0.0303f), click: true);
        Assert.Equal("Pan 0,38", rig.Sent.Last());

        rig.Point(Rig.AimAt(y: -0.0303f));
        Assert.DoesNotContain(rig.Sent, sent => sent.StartsWith("Down") || sent.StartsWith("Up"));
    }

    [Fact]
    public void ASmallWobbleWhileHeldStillClicks()
    {
        var rig = new Rig { Scrolls = true };

        rig.Point();
        rig.Point(click: true);
        rig.Point(Rig.AimAt(y: -0.0053f), click: true);
        rig.Point(Rig.AimAt(y: -0.0053f));

        Assert.Equal(["Move 320,160", "Move 320,176", "Down 320,160", "Up 320,160"], rig.Sent);
    }

    [Fact]
    public void LeavingWhileHeldOnAScrollingBoardDoesNotClick()
    {
        var rig = new Rig { Scrolls = true };

        rig.Point();
        rig.Point(click: true);
        rig.Tick(new VrHandInput(null, true, false));
        rig.Tick(new VrHandInput(null, false, false));

        Assert.Equal(["Move 320,160", "Leave 320,160"], rig.Sent);
    }

    [Fact]
    public void LeavingAndComingBackWhileHeldDoesNotClick()
    {
        var rig = new Rig { Scrolls = true };

        rig.Point();
        rig.Point(click: true);
        rig.Tick(new VrHandInput(null, true, false));
        rig.Point(click: true);
        rig.Point();

        Assert.DoesNotContain(rig.Sent, sent => sent.StartsWith("Down") || sent.StartsWith("Up"));
    }

    /// <summary>
    /// 巻く板でも、掴む手（cursor: grab）の上で引いたら巻かない: その場で押し、動かしたら動いた所を送り、離した所で離す（ページが自分でつまみを掴んで動かす）。
    /// 決めるのは引いた瞬間だけ — 掴んだあとにカーソルが変わっても巻きに切り替えない。動かさずに長く引いても右クリックにしない。
    /// </summary>
    [Fact]
    public void PullingOnAGrabHandDragsInsteadOfScrolling()
    {
        var rig = new Rig { Scrolls = true, Grab = true };

        rig.Point();
        rig.Point(click: true);
        Assert.Equal(["Move 320,160", "Down 320,160"], rig.Sent);

        rig.Grab = false;
        rig.Now += VrLaser.LongPressMilliseconds + 100;
        rig.Point(click: true);
        rig.Point(Rig.AimAt(y: -0.0203f), click: true);
        rig.Point(Rig.AimAt(y: -0.0203f));

        Assert.Equal(["Move 320,160", "Down 320,160", "Move 320,224", "Up 320,224"], rig.Sent);
    }

    [Fact]
    public void LeavingWhileHoldingAGrabHandLetsGo()
    {
        var rig = new Rig { Scrolls = true, Grab = true };

        rig.Point();
        rig.Point(click: true);
        rig.Tick(new VrHandInput(null, true, false));

        Assert.Equal(["Move 320,160", "Down 320,160", "Up 320,160", "Leave 320,160"], rig.Sent);
    }

    /// <summary>
    /// 巻く板で動かさずに 0.5 秒引き続けたら、引き始めた所で右クリックを送る（強めに 1 回震える）。
    /// 離しても何も送らない（メニューは点の下に開くので、クリックを送ると 1 行目が押される）。
    /// </summary>
    [Fact]
    public void HoldingStillOnAScrollingBoardRightClicksWhereItStarted()
    {
        var rig = new Rig { Scrolls = true };

        rig.Point();
        rig.Point(click: true);

        rig.Now = VrLaser.LongPressMilliseconds - 1;
        rig.Point(click: true);
        Assert.Equal(["Move 320,160"], rig.Sent);

        rig.Now = VrLaser.LongPressMilliseconds;
        rig.Point(click: true);
        Assert.Equal(["Move 320,160", "Down 320,160 Right", "Up 320,160 Right"], rig.Sent);
        Assert.Equal((VrLaser.LongPressPulseSeconds, VrLaser.LongPressPulseAmplitude), rig.Overlays.Pulses.Last());

        rig.Now = VrLaser.LongPressMilliseconds * 4;
        rig.Point(click: true);
        rig.Point();

        Assert.Equal(["Move 320,160", "Down 320,160 Right", "Up 320,160 Right"], rig.Sent);
    }

    /// <summary>長押しのあとは動かしても巻かない（巻くとメニューが閉じる）。離しても何も送らない。</summary>
    [Fact]
    public void MovingAfterALongPressDoesNotScroll()
    {
        var rig = new Rig { Scrolls = true };

        rig.Point();
        rig.Point(click: true);
        rig.Now = VrLaser.LongPressMilliseconds;
        rig.Point(click: true);

        rig.Point(Rig.AimAt(y: -0.0203f), click: true);
        rig.Point(Rig.AimAt(y: -0.0203f));

        Assert.DoesNotContain(rig.Sent, sent => sent.StartsWith("Pan"));
        Assert.Equal(["Down 320,160 Right", "Up 320,160 Right"], rig.Sent.Where(sent => sent.StartsWith("Down") || sent.StartsWith("Up")));
    }

    [Fact]
    public void ASmallWobbleStillCountsAsALongPress()
    {
        var rig = new Rig { Scrolls = true };

        rig.Point();
        rig.Point(click: true);
        rig.Now = VrLaser.LongPressMilliseconds;
        rig.Point(Rig.AimAt(y: -0.0053f), click: true);

        Assert.Equal(["Move 320,160", "Move 320,176", "Down 320,160 Right", "Up 320,160 Right"], rig.Sent);
    }

    [Fact]
    public void ScrollingFirstNeverTurnsIntoALongPress()
    {
        var rig = new Rig { Scrolls = true };

        rig.Point();
        rig.Point(click: true);
        rig.Point(Rig.AimAt(y: -0.0203f), click: true);

        rig.Now = VrLaser.LongPressMilliseconds * 4;
        rig.Point(Rig.AimAt(y: -0.0203f), click: true);
        rig.Point(Rig.AimAt(y: -0.0203f));

        Assert.DoesNotContain(rig.Sent, sent => sent.EndsWith("Right"));
    }

    /// <summary>離してから引き直せばまた長押しできる（時計は引き直した所から数える）。その間のタップはふつうのクリック。</summary>
    [Fact]
    public void EachPullIsTimedOnItsOwn()
    {
        var rig = new Rig { Scrolls = true };

        rig.Point();
        rig.Point(click: true);
        rig.Now = VrLaser.LongPressMilliseconds;
        rig.Point(click: true);
        rig.Point();

        rig.Now = VrLaser.LongPressMilliseconds * 3;
        rig.Point(click: true);
        rig.Now += VrLaser.LongPressMilliseconds - 1;
        rig.Point(click: true);
        rig.Point();

        Assert.Equal(["Down 320,160", "Up 320,160"], rig.Sent.TakeLast(2));
    }

    /// <summary>巻かない板（手首）には<b>長押しが無い</b>（引いたらすぐ押していて、メニューも無い）。</summary>
    [Fact]
    public void ABoardThatDoesNotScrollHasNoLongPress()
    {
        var rig = new Rig();

        rig.Point();
        rig.Point(click: true);
        rig.Now = VrLaser.LongPressMilliseconds * 4;
        rig.Point(click: true);
        rig.Point();

        Assert.Equal(["Move 320,160", "Down 320,160", "Up 320,160"], rig.Sent);
    }

    [Fact]
    public void ABoardThatDoesNotScrollPressesAtOnceAndNeverScrolls()
    {
        var rig = new Rig();

        rig.Point();
        rig.Point(click: true);
        Assert.Equal("Down 320,160", rig.Sent.Last());

        rig.Point(Rig.AimAt(y: -0.0203f), click: true);
        rig.Point(Rig.AimAt(y: -0.0203f));

        Assert.Equal(["Move 320,160", "Down 320,160", "Move 320,224", "Up 320,224"], rig.Sent);
    }

    /// <summary>1 周の間（90 Hz で 11 ms）。</summary>
    private const long Frame = 11;

    /// <summary>
    /// 引いて、1 周ごとに <paramref name="step"/> m ずつ下へ動かす（<paramref name="frames"/> 周）。最後の狙いを返す。
    /// 既定は 1 cm = 32 画素 / 11 ms = 約 2.9 画素 / ms（1 周目は振れの内なので、巻くのは 2 周目から）。
    /// </summary>
    private static VrMatrix Flick(Rig rig, int frames = 4, float step = 0.01f, long frame = Frame)
    {
        rig.Point();
        rig.Point(click: true);

        var aim = Rig.AimAt();

        for (var i = 1; i <= frames; i++)
        {
            rig.Now += frame;
            aim = Rig.AimAt(y: -0.0003f - step * i);
            rig.Point(aim, click: true);
        }

        return aim;
    }

    /// <summary>
    /// その場で離す（<b>離した周は動かさない</b> = 1 周止めてから離したのと同じ）。<b>ここから後に送ったものを数える</b>ための位置を返す。
    /// </summary>
    private static int LetGo(Rig rig, VrMatrix aim, long frame = Frame)
    {
        rig.Now += frame;
        rig.Point(aim);
        return rig.Sent.Count;
    }

    /// <summary>
    /// 引いて振って、<b>動かしながら離す</b>（離した周も同じだけ動かす。手で振ったときと同じ）。
    /// 既定は 3 周引いて 4 周目に離す: 巻くのは 2 周目から、離すのは下へ 4 cm（画素 (320, 288)）、速さは約 2.9 画素 / ms。
    /// 最後の狙いと、<b>離したあとに送ったものを数える</b>ための位置を返す（離した周の巻きは数えない）。
    /// </summary>
    private static (VrMatrix Aim, int From) FlickAndLetGo(Rig rig, int frames = 3, float step = 0.01f, long frame = Frame)
    {
        Flick(rig, frames, step, frame);

        rig.Now += frame;
        var aim = Rig.AimAt(y: -0.0003f - step * (frames + 1));
        rig.Point(aim);

        return (aim, rig.Sent.Count);
    }

    /// <summary>指したまま（引かずに）<paramref name="frames"/> 周回す。</summary>
    private static void Wait(Rig rig, VrMatrix aim, int frames, long frame = Frame)
    {
        for (var i = 0; i < frames; i++)
        {
            rig.Now += frame;
            rig.Point(aim);
        }
    }

    /// <summary><paramref name="from"/> から後に送った巻きの縦の量。</summary>
    private static List<int> PansSince(Rig rig, int from) =>
        [.. rig.Sent.Skip(from).Where(sent => sent.StartsWith("Pan ")).Select(sent => int.Parse(sent.Split(',')[1]))];

    /// <summary>
    /// 動かしながら離したら、同じ向きに流れ続けて、だんだん緩んで止まる。横には流れない。
    /// 流れ始めは離す直前の速さ（約 2.9 画素 / ms × 11 ms × 1.2 = 38）。
    /// </summary>
    [Fact]
    public void AFlickKeepsScrollingAfterLettingGoAndSlowsDownToAStop()
    {
        var rig = new Rig { Scrolls = true };
        var (aim, from) = FlickAndLetGo(rig);

        Wait(rig, aim, 300);

        var pans = PansSince(rig, from);

        Assert.True(pans.Count >= 10, $"only {pans.Count} glide steps");
        Assert.InRange(pans[0], 36, 40);
        Assert.All(pans, pan => Assert.True(pan > 0));
        Assert.True(pans[^1] < pans[0] / 4, $"did not slow down: {pans[0]} → {pans[^1]}");
        Assert.All(rig.Sent.Skip(from), sent => Assert.StartsWith("Pan 0,", sent));

        var stopped = rig.Sent.Count;
        Wait(rig, aim, 50);
        Assert.Equal(stopped, rig.Sent.Count);
    }

    /// <summary><b>止めてから離したら流れない</b>（読みたい所で止めて離したのに、そこから逃げると困る）。</summary>
    [Fact]
    public void StoppingBeforeLettingGoDoesNotGlide()
    {
        var rig = new Rig { Scrolls = true };
        var aim = Flick(rig);

        // 引いたまま、速さを取る時間（100 ms）より長く止める。
        for (var i = 0; i < 10; i++)
        {
            rig.Now += Frame;
            rig.Point(aim, click: true);
        }

        var from = LetGo(rig, aim);
        Wait(rig, aim, 30);

        Assert.Empty(PansSince(rig, from));
    }

    /// <summary>
    /// 8 ms より短い間の動きからは速さを取らない（同じ瞬間の動きを割ると速さが果てしなくなり、上限いっぱいで流れ出す。周が詰まって動きがまとめて来たとき）。
    /// 0 ms のときに流れを作ると、速さが割り切れず（NaN）何も送らないまま止まらないので、流れていないことまで見る。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MovesTooCloseTogetherInTimeDoNotGlide(int frame)
    {
        var rig = new Rig { Scrolls = true };
        var aim = Flick(rig, frame: frame);

        var from = LetGo(rig, aim, frame);
        Assert.Equal(0UL, rig.Laser.GlidingOn);

        Wait(rig, aim, 30);
        Assert.Empty(PansSince(rig, from));
    }

    /// <summary>
    /// 流れている板を押したら止まり、その 1 押しはクリックにしない（長く引いても右クリックにしない）。
    /// 中身はボタンや行だらけで、止めた拍子に下の物が押されると困る。次の 1 押しからはいつもどおり。
    /// </summary>
    [Fact]
    public void PressingTheGlidingBoardStopsItWithoutClickingWhatIsUnderneath()
    {
        var rig = new Rig { Scrolls = true };
        var (aim, _) = FlickAndLetGo(rig);
        Wait(rig, aim, 2);

        rig.Now += Frame;
        var pressed = rig.Sent.Count;
        rig.Point(aim, click: true);

        rig.Now += VrLaser.LongPressMilliseconds * 2;
        rig.Point(aim, click: true);
        rig.Now += Frame;
        rig.Point(aim);
        Wait(rig, aim, 30);

        Assert.Empty(rig.Sent.Skip(pressed));

        rig.Point(aim, click: true);
        rig.Point(aim);
        Assert.Equal(["Down 320,288", "Up 320,288"], rig.Sent.Skip(pressed));
    }

    [Fact]
    public void TheGlideCanBeCaughtAndThrownAgain()
    {
        var rig = new Rig { Scrolls = true };
        var aim = FlickAndLetGo(rig).Aim;
        Wait(rig, aim, 2);

        // 止めた所（下へ 4 cm）から、上へ 1 周 1 cm ずつ戻す。
        rig.Now += Frame;
        rig.Point(aim, click: true);

        for (var i = 1; i <= 4; i++)
        {
            rig.Now += Frame;
            aim = Rig.AimAt(y: -0.0403f + 0.01f * i);
            rig.Point(aim, click: true);
        }

        var from = LetGo(rig, aim);
        Wait(rig, aim, 10);

        var pans = PansSince(rig, from);
        Assert.NotEmpty(pans);
        Assert.All(pans, pan => Assert.True(pan < 0));
        Assert.DoesNotContain(rig.Sent, sent => sent.StartsWith("Down") || sent.StartsWith("Up"));
    }

    /// <summary>
    /// 巻きに入った次の周で離しても流れる（離した周の動きも巻いて、速さに入れる。数えないと速さが 1 周古くなり、巻き始めてすぐ離すと 1 点しか無くて流れない）。
    /// 離した周の 1 cm（32 画素 × 1.2 = 38）も巻く。
    /// </summary>
    [Fact]
    public void AFlickLetGoRightAfterItStartsScrollingStillGlides()
    {
        var rig = new Rig { Scrolls = true };

        rig.Point();
        rig.Point(click: true);
        rig.Now += Frame;
        rig.Point(Rig.AimAt(y: -0.0203f), click: true);

        rig.Now += Frame;
        var released = rig.Sent.Count;
        var aim = Rig.AimAt(y: -0.0303f);
        rig.Point(aim);

        Assert.Equal(["Move 320,256", "Pan 0,38"], rig.Sent.Skip(released));

        var from = rig.Sent.Count;
        Wait(rig, aim, 5);
        Assert.NotEmpty(PansSince(rig, from));
    }

    [Fact]
    public void AMoveOnlyOnTheReleaseFrameStillClicks()
    {
        var rig = new Rig { Scrolls = true };

        rig.Point();
        rig.Point(click: true);
        rig.Now += Frame;
        rig.Point(Rig.AimAt(y: -0.0203f));

        Assert.Equal(["Move 320,160", "Move 320,224", "Down 320,160", "Up 320,160"], rig.Sent);
    }

    /// <summary>
    /// 流れている板で掴む物（並べ替えのつまみ）の上を押しても掴まない — 止めるだけ（動いていた中身の、狙っていない物を掴む）。流れていなければ、いつもどおり掴む。
    /// </summary>
    [Fact]
    public void PressingAGrabHandleOnTheGlidingBoardOnlyStopsIt()
    {
        var rig = new Rig { Scrolls = true };
        var (aim, _) = FlickAndLetGo(rig);
        Wait(rig, aim, 2);

        rig.Grab = true;
        rig.Now += Frame;
        var pressed = rig.Sent.Count;
        rig.Point(aim, click: true);
        rig.Now += Frame;
        rig.Point(aim);
        Wait(rig, aim, 10);

        Assert.Empty(rig.Sent.Skip(pressed));

        rig.Point(aim, click: true);
        Assert.Equal("Down 320,288", rig.Sent.Last());
    }

    /// <summary>
    /// 巻いている途中で<b>板から外れても</b>、その速さで流れる（指が画面の外へ出ても中身は流れる）。
    /// 流すのは離した所へ（外れたあと、点はもう板の上に無い）。
    /// </summary>
    [Fact]
    public void LeavingTheBoardWhileFlickingStillGlides()
    {
        var rig = new Rig { Scrolls = true };
        Flick(rig);

        rig.Now += Frame;
        rig.Tick(new VrHandInput(null, true, false));
        var from = rig.Sent.Count;
        var placesFrom = rig.PanAt.Count;

        for (var i = 0; i < 10; i++)
        {
            rig.Now += Frame;
            rig.Tick(new VrHandInput(null, false, false));
        }

        Assert.NotEmpty(PansSince(rig, from));
        Assert.All(rig.PanAt.Skip(placesFrom), at => Assert.Equal("320,288", at));
    }

    [Fact]
    public void MovingOntoAnotherBoardWhileFlickingStillGlidesTheFirst()
    {
        var rig = new Rig { Scrolls = true };
        Flick(rig);

        List<string> otherSent = [];
        var otherOffset = Offset.Then(VrMatrix.Translation(0.5f, 0, 0));
        var other = Rig.Board(9, BoardDevice, BoardDevicePose, otherOffset, otherSent);
        var atOther = BoardDevicePose.Then(otherOffset).Then(VrMatrix.Translation(0.0003f, -0.0003f, 0.3f));

        rig.Now += Frame;
        rig.Laser.Tick(VrHand.Right, new VrHandInput(atOther, true, false), [rig.Target, other]);
        var from = rig.Sent.Count;
        var placesFrom = rig.PanAt.Count;

        for (var i = 0; i < 10; i++)
        {
            rig.Now += Frame;
            rig.Laser.Tick(VrHand.Right, new VrHandInput(atOther, false, false), [rig.Target, other]);
        }

        Assert.NotEmpty(PansSince(rig, from));
        Assert.All(rig.PanAt.Skip(placesFrom), at => Assert.Equal("320,288", at));
        Assert.DoesNotContain(otherSent, sent => sent.StartsWith("Pan "));
    }

    [Fact]
    public void TheGlideStopsWhenTheBoardCanNoLongerBePointedAt()
    {
        var rig = new Rig { Scrolls = true };
        var (aim, _) = FlickAndLetGo(rig);
        Wait(rig, aim, 1);

        rig.Now += Frame;
        rig.Laser.Tick(VrHand.Right, new VrHandInput(aim, false, false), []);
        var from = rig.Sent.Count;

        Wait(rig, aim, 30);

        Assert.Empty(PansSince(rig, from));
    }

    [Fact]
    public void TheGlideStopsWhenTheBoardStopsScrolling()
    {
        var rig = new Rig { Scrolls = true };
        var (aim, _) = FlickAndLetGo(rig);
        Wait(rig, aim, 1);

        rig.Now += Frame;
        var from = rig.Sent.Count;
        rig.Tick(new VrHandInput(aim, false, false), Rig.Board(7, BoardDevice, BoardDevicePose, Offset, rig.Sent, scrolls: false));

        Wait(rig, aim, 30);

        Assert.Empty(PansSince(rig, from));
    }

    [Fact]
    public void StoppingTheLaserEndsTheGlide()
    {
        var rig = new Rig { Scrolls = true };
        var (aim, _) = FlickAndLetGo(rig);
        Wait(rig, aim, 1);

        rig.Laser.Stop();
        rig.Laser.Start();
        var from = rig.Sent.Count;
        Wait(rig, aim, 30);

        Assert.Empty(PansSince(rig, from));
    }

    /// <summary>
    /// <b>速すぎる振りは上限の速さで流す</b>（<see cref="VrLaser.GlideMaxSpeed"/>）。流れ始めは 6 画素 / ms × 11 ms × 1.2 = 79、
    /// 流れる量は全部で「6 × 325 ms × 1.2」= 約 2340（周で刻むぶん少し多い）。
    /// </summary>
    [Fact]
    public void AVeryFastFlickGlidesNoFasterThanTheTopSpeed()
    {
        var rig = new Rig { Scrolls = true };

        // 4 ms ごとに 1.1 cm（約 8.8 画素 / ms）。
        var (aim, from) = FlickAndLetGo(rig, step: 0.011f, frame: 4);

        Wait(rig, aim, 300);

        var pans = PansSince(rig, from);
        var top = VrLaser.GlideMaxSpeed * VrLaser.GlideDecayMs * VrLaser.DragScrollGain;

        Assert.InRange(pans[0], 78, 80);
        Assert.InRange(pans.Sum(), top * 0.95f, top * 1.05f);
    }

    /// <summary>
    /// <b>流れる速さは周の速さで変わらない</b>（周ではなく時間で進める。ヘッドセットの 90 Hz でも 60 Hz でも、同じ時間で同じだけ流れる）。
    /// 離してから 176 ms（11 ms × 16 周 = 16 ms × 11 周）で比べる。
    /// </summary>
    [Fact]
    public void TheGlideMovesAsFarInTheSameTimeAtAnyFrameRate()
    {
        int After(int frames, long frame)
        {
            var rig = new Rig { Scrolls = true };
            var (aim, from) = FlickAndLetGo(rig);
            Wait(rig, aim, frames, frame);
            return PansSince(rig, from).Sum();
        }

        var fast = After(16, 11);
        var slow = After(11, 16);

        Assert.InRange(slow, fast * 0.97, fast * 1.03);
    }

    /// <summary>
    /// <b>周が詰まって間が空いても、一気に跳ばない</b>（1 周で進めるのは 50 ms まで）。詰まったあとの 1 周で
    /// 半秒ぶん流れると、読んでいた所が飛んで見える。
    /// </summary>
    [Fact]
    public void AStalledFrameDoesNotJumpTheGlideAhead()
    {
        var rig = new Rig { Scrolls = true };
        var (aim, _) = FlickAndLetGo(rig);
        Wait(rig, aim, 1);

        var from = rig.Sent.Count;
        Wait(rig, aim, 1, frame: 500);

        // 約 2.9 画素 / ms × 50 ms × 1.2 = 174（間の 500 ms ぶんなら 1700 を超える）。
        Assert.InRange(PansSince(rig, from).Single(), 1, 180);
    }

    /// <summary>
    /// <b>離す直前に少し止めると、遅く流れる</b>（止めていた周も速さに入る）。4 周止めて、動かさずに離すと（55 ms 止めた）、
    /// 直前 100 ms の平均は約 0.8 画素 / ms → 流れ始めは 0.8 × 11 × 1.2 = 11 前後（動かしながら離すと 38）。
    /// </summary>
    [Fact]
    public void APauseJustBeforeLettingGoSlowsTheGlide()
    {
        var rig = new Rig { Scrolls = true };
        var aim = Flick(rig);

        for (var i = 0; i < 4; i++)
        {
            rig.Now += Frame;
            rig.Point(aim, click: true);
        }

        var from = LetGo(rig, aim);
        Wait(rig, aim, 1);

        Assert.InRange(PansSince(rig, from).Single(), 9, 17);
    }

    /// <summary><b>板の外でトリガーを引いても止めない</b>（VRChat で物を掴んだ・板から外れた所で引いた）。止めるのは板の上で押したときだけ。</summary>
    [Fact]
    public void PullingTheTriggerOffTheBoardDoesNotStopTheGlide()
    {
        var rig = new Rig { Scrolls = true };
        var (aim, _) = FlickAndLetGo(rig);
        Wait(rig, aim, 1);

        rig.Now += Frame;
        rig.Tick(new VrHandInput(null, true, false));
        var from = rig.Sent.Count;

        for (var i = 0; i < 5; i++)
        {
            rig.Now += Frame;
            rig.Tick(new VrHandInput(null, true, false));
        }

        Assert.Equal(5, PansSince(rig, from).Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PressingAnotherBoardWithTheOtherHandStopsTheGlide(bool leftFlicks)
    {
        var overlays = new LaserOverlays();
        var now = 0L;
        var pointing = new VrPointing(overlays, NullLogger.Instance, () => now);
        pointing.Start();

        List<string> windowSent = [];
        List<string> wristSent = [];
        var wristOffset = Offset.Then(VrMatrix.Translation(0.5f, 0, 0));
        VrLaserTarget[] targets =
        [
            Rig.Board(7, BoardDevice, BoardDevicePose, Offset, windowSent, scrolls: true),
            Rig.Board(9, BoardDevice, BoardDevicePose, wristOffset, wristSent),
        ];
        var atWrist = BoardDevicePose.Then(wristOffset).Then(VrMatrix.Translation(0.0003f, -0.0003f, 0.3f));
        VrHandInput nothing = new(null, false, false);

        // 流す手（窓）と押す手（手首）。leftFlicks が false なら右手で流して左手で押す。
        void Tick(VrHandInput flicking, VrHandInput other)
        {
            now += Frame;
            pointing.Tick(leftFlicks ? new VrHands(flicking, other) : new VrHands(other, flicking), targets);
        }

        var flicker = leftFlicks ? VrHand.Left : VrHand.Right;

        // 窓を引いて振って離す。
        Tick(new(Rig.AimAt(), false, false), nothing);
        Tick(new(Rig.AimAt(), true, false), nothing);
        for (var i = 1; i <= 4; i++) Tick(new(Rig.AimAt(y: -0.0003f - 0.01f * i), true, false), nothing);
        var aim = Rig.AimAt(y: -0.0403f);
        Tick(new(aim, false, false), nothing);
        Tick(new(aim, false, false), nothing);
        Assert.Equal(7UL, pointing.For(flicker).GlidingOn);

        // 反対の手で、板の外でトリガーを引く（VRChat の物を掴む）→ 止めない。
        Tick(new(aim, false, false), new(null, true, false));
        Tick(new(aim, false, false), nothing);
        Assert.Equal(7UL, pointing.For(flicker).GlidingOn);

        // 反対の手で手首を押す → 止まる。手首には届く。
        Tick(new(aim, false, false), new(atWrist, false, false));
        Tick(new(aim, false, false), new(atWrist, true, false));
        var from = windowSent.Count;
        Tick(new(aim, false, false), new(atWrist, false, false));
        for (var i = 0; i < 30; i++) Tick(new(aim, false, false), nothing);

        Assert.Equal(0UL, pointing.For(flicker).GlidingOn);
        Assert.DoesNotContain(windowSent.Skip(from), sent => sent.StartsWith("Pan "));
        Assert.Contains("Down 320,160", wristSent);
    }

    /// <summary>
    /// <b>重なっていたら手前の板を指す</b>（手首の板の奥に窓がある、など）。手前の板が無くなったら奥の板へ移り、
    /// 手前の板には外れたことを送る（<c>:hover</c> が残らない）。答えにはどの板を指したかが入る。
    /// </summary>
    [Fact]
    public void TheNearestBoardIsPointedAtAndMovingOnLeavesTheOther()
    {
        var rig = new Rig();
        List<string> far = [];
        var behind = Rig.Board(9, BoardDevice, BoardDevicePose, Offset.Then(VrMatrix.Translation(0, 0, -0.2f)), far);

        var step = rig.Laser.Tick(VrHand.Right, new VrHandInput(Rig.AimAt(), false, false), [behind, rig.Target]);
        Assert.Equal(7UL, step.Target);
        Assert.Equal(["Move 320,160"], rig.Sent);
        Assert.Empty(far);

        step = rig.Laser.Tick(VrHand.Right, new VrHandInput(Rig.AimAt(), false, false), [behind]);
        Assert.Equal(9UL, step.Target);
        Assert.Equal(["Move 320,160", "Leave 320,160"], rig.Sent);
        Assert.Equal(["Move 320,160"], far);
    }

    /// <summary>
    /// 指している板の縁では出る側の余白（3 cm）だが、<b>ほかの板へ入るのは入る側の余白（1 cm）</b>で測る。
    /// 縁の近くに手前の板があっても、2 cm 外なら今の板を指し続ける（隣の板と行ったり来たりしない）。
    /// </summary>
    [Fact]
    public void AnotherBoardIsEnteredOnlyWithinTheNarrowMargin()
    {
        var rig = new Rig();
        List<string> near = [];

        // 手前 10 cm、右端が狙いの 2 cm 左に来る板。
        var nearer = Rig.Board(9, BoardDevice, BoardDevicePose, Offset.Then(VrMatrix.Translation(-0.12f, 0, 0.1f)), near);

        rig.Point();
        var step = rig.Laser.Tick(VrHand.Right, new VrHandInput(Rig.AimAt(), false, false), [rig.Target, nearer]);

        Assert.Equal(7UL, step.Target);
        Assert.Empty(near);
    }

    [Fact]
    public void PointingAtNothingSaysNoBoard()
    {
        var rig = new Rig();

        Assert.Equal(0UL, rig.Laser.Tick(VrHand.Right, new VrHandInput(Rig.AimAt(), false, false), []).Target);
    }

    /// <summary>
    /// <b>空間に置いた板（機器が無い）では、点も空間に置く。</b> 板も点も同じ流儀なので、板の上で震えない。
    /// </summary>
    [Fact]
    public void OnABoardInSpaceTheDotIsPlacedInSpaceToo()
    {
        var rig = new Rig();
        var inSpace = BoardDevicePose.Then(Offset);
        var target = Rig.Board(5, null, VrMatrix.Identity, inSpace, rig.Sent);

        rig.Tick(new VrHandInput(Rig.AimAt(0.03f, -0.01f), false, false), target);

        Assert.Empty(rig.Overlays.SpritesAt);

        var (x, y, z) = rig.Overlays.Placed[rig.Overlays.Dot].Position;
        var (ex, ey, ez) = inSpace.Then(VrMatrix.Translation(0.03f, -0.01f, VrLaser.DotLift)).Position;
        Assert.Equal(ex, x, 1e-5f);
        Assert.Equal(ey, y, 1e-5f);
        Assert.Equal(ez, z, 1e-5f);
    }

    private sealed class LaserOverlays : IVrOverlays
    {
        public bool SpritesFail { get; set; }

        public List<VrSprite> Sprites { get; } = [];

        public ulong Dot => 22;

        public Dictionary<ulong, (uint Device, VrMatrix Offset)> SpritesAt { get; } = [];

        public Dictionary<ulong, bool> SpritesShown { get; } = [];

        public int SpritesDestroyed { get; private set; }

        public ulong CreateSprite(string key, string name, VrSprite sprite, float widthInMeters)
        {
            if (SpritesFail) return 0;

            Sprites.Add(sprite);
            return Dot;
        }

        public void Attach(ulong handle, uint device, VrMatrix offset) => SpritesAt[handle] = (device, offset);

        public Dictionary<ulong, VrMatrix> Placed { get; } = [];

        public void PlaceAbsolute(ulong handle, VrMatrix pose) => Placed[handle] = pose;

        public void SetShown(ulong handle, bool shown) => SpritesShown[handle] = shown;

        public void SetAlpha(ulong handle, float alpha)
        {
        }

        public void Destroy(ulong handle) => SpritesDestroyed++;

        public List<(float Seconds, float Amplitude)> Pulses { get; } = [];

        public void Pulse(VrHand hand, float seconds, float amplitude) => Pulses.Add((seconds, amplitude));

        public (float U, float V)? Intersect(ulong handle, VrMatrix aim) => null;

        public bool IsAvailable() => true;

        public bool Start(out string? error)
        {
            error = null;
            return true;
        }

        public void Stop()
        {
        }

        public ulong CreateDashboard(string key, string name, float widthInMeters) => 1;

        public void UploadTexture(ulong handle, nint texture)
        {
        }

        public bool QuitRequested() => false;

        public void EnableInput(ulong handle, int width, int height)
        {
        }

        public IReadOnlyList<VrPointer> PollPointer(ulong handle) => [];

        public ulong CreateOverlay(string key, string name, float widthInMeters) => 7;

        public void SetWidth(ulong handle, float widthInMeters)
        {
        }

        public uint? ControllerFor(VrHand hand) => null;

        public IReadOnlyList<VrMatrix?> Poses() => [];

        public bool DashboardOpen() => false;

        public float? DisplayFrequency() => 90f;

        public VrHands ReadHands(bool captureAll) => default;
    }
}
