using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Vr;
using SpoitableToolKit.Host.Ui.Vr.Overlays;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Pointing;
using SpoitableToolKit.Host.Ui.Vr.Window;
using SpoitableToolKit.Host.Ui.Vr.Wrist;
using SpoitableToolKit.Tests.Ui.Vr.Wrist;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Vr.Window;

/// <summary>
/// ヘッドセット無しで書ける。姿勢は偽物の OpenVR が返し、周は <see cref="VrScene"/> で回す（VrHost と同じ）。
/// 置き方は 2 つ。前半は空間に置く窓（目の前）、後半の「手首に付ける」の節が手首の板の上に付けた窓。偽物の設定の既定は空間に置く方。
/// 実機でどう見えるか（出る所・大きさの手応え）は、ここでは見られない。
/// </summary>
public class VrWindowHostTests
{
    private const uint LeftController = 1;
    private const uint RightController = 2;

    /// <summary>頭。原点から 1.6m 上で、-Z を見ている。</summary>
    private static readonly VrMatrix Head = VrMatrix.Translation(0, 1.6f, 0);

    /// <summary>左のコントローラー（手首）から見た手首の板の置き場（少し上・少し手前）。</summary>
    private static readonly VrMatrix WristOffset = VrMatrix.Translation(0, 0.04f, -0.05f);

    private const float WristBoardHeight = 0.09f;

    private sealed class Rig
    {
        public FakeOverlays Overlays { get; } = new();

        public FakeSettings Settings { get; } = new();

        /// <summary>窓に出す画面が変わった順（null は閉じた）。</summary>
        public List<string?> Showing { get; } = [];

        /// <summary>手首へ知らせた画面の順（null は閉じた）。サイドバーでの切り替えも入る。</summary>
        public List<string?> Views { get; } = [];

        public List<string> Sent { get; } = [];

        public bool WristShown { get; set; } = true;

        public VrHand WristHand { get; set; } = VrHand.Left;

        /// <summary>browser の寝起き（手首と一緒に隠れた・出た）の順。</summary>
        public List<bool> Awake { get; } = [];

        /// <summary>手首の板の透明度（フェードの途中なら 1 より小さい）。</summary>
        public float WristAlpha { get; set; } = 1f;

        /// <summary>手首の板が出ているか（指してよいか）。消えていく途中は false。</summary>
        public bool WristLive { get; set; } = true;

        /// <summary>手首の板の今（偽物の手首）。付けている手のコントローラーの今の姿勢に付いている。</summary>
        public WristBoardState? WristBoard() =>
            WristShown && Overlays.Pose[Controller(WristHand)] is { } pose
                ? new WristBoardState(WristHand, Controller(WristHand), pose, WristOffset, WristBoardHeight, WristAlpha, WristLive)
                : null;

        public VrFrame Frame { get; set; } = new(99, 1);

        /// <summary>今の時刻（ミリ秒）。レーザーの長押しはこれで測る（本物の時計だと、遅い機械でタップが長押しに化ける）。</summary>
        public long Now { get; set; }

        /// <summary>Layout Mode か（偽物の手首。持ち主は手首）。窓を掴めるのはこの間だけ、その間は中身を押せない。</summary>
        public bool Layout { get; set; }

        public bool Clickable { get; set; }

        public bool Grabbable { get; set; }

        public VrWindowHost Window { get; }

        public VrScene Scene { get; }

        public Rig()
        {
            Window = new VrWindowHost(
                Overlays, Settings, use => use(Frame),
                (kind, x, y, button, wheelX, wheelY) => Sent.Add(
                    kind == VrPointerKind.Pan ? $"Pan {wheelX},{wheelY}"
                    : button == VrPointerButton.Right ? $"{kind} {x},{y} Right"
                    : $"{kind} {x},{y}"),
                Showing.Add, Awake.Add, WristBoard, () => Layout, NullLogger.Instance, () => Clickable, Views.Add, () => Grabbable);

            Scene = new VrScene(Overlays, NullLogger.Instance, [Window], () => Now, () => Layout);
        }

        public void Start() => Scene.Start();

        public void Tick() => Scene.Tick();

        public void Ticks(int count)
        {
            for (var i = 0; i < count; i++) Tick();
        }

        public void Shortcut(string view = "hrs")
        {
            Window.Toggle(view);
            Tick();
        }

        public void StartOpen(string view = "hrs")
        {
            Start();
            Shortcut(view);
        }

        /// <summary>
        /// 窓の画素 (<paramref name="px"/>, <paramref name="py"/>) の真ん中を、表 40 cm 手前から真っ直ぐ指す狙い。
        /// </summary>
        public VrMatrix AimAt(int px = 640, int py = 360)
        {
            var x = Window.WidthInMeters * ((px + 0.5f) / VrWindowHost.Width - 0.5f);
            var y = Window.HeightInMeters * (0.5f - (py + 0.5f) / VrWindowHost.Height);

            return Window.Pose.Then(VrMatrix.Translation(x, y, 0.4f));
        }

        public void Point(VrHand hand, int px = 640, int py = 360) => Overlays.Hand(hand, input => input with { Aim = AimAt(px, py) });

        public void PointAway(VrHand hand) => Overlays.Hand(hand, input => input with { Aim = null });

        public void Trigger(VrHand hand, bool down) => Overlays.Hand(hand, input => input with { Click = down });

        public void Grip(VrHand hand, bool down) => Overlays.Hand(hand, input => input with { Grab = down });

        /// <summary>
        /// 指してグリップ（1 周）。掴み始めるのはこの周の終わり。コントローラーは狙いと同じ所に置く。
        /// 掴めるのは Layout Mode の間だけなので、先に入る（手首の 🔓 を押したところ）。
        /// </summary>
        public void Grab(VrHand hand = VrHand.Right)
        {
            Layout = true;
            Point(hand);
            Overlays.Pose[Controller(hand)] = AimAt();
            Grip(hand, true);
            Tick();
        }

        public void MoveHand(VrHand hand, (float X, float Y, float Z) by)
        {
            var current = Overlays.Pose[Controller(hand)]!.Value;
            Overlays.Pose[Controller(hand)] = VrMatrix.Translation(by.X, by.Y, by.Z).Then(current);
        }

        public static uint Controller(VrHand hand) => hand == VrHand.Left ? LeftController : RightController;
    }

    [Fact]
    public void NothingShowsUntilAShortcutIsPressed()
    {
        var rig = new Rig();
        rig.Start();
        rig.Ticks(3);

        Assert.True(rig.Window.Running);
        Assert.Equal([VrWindowHost.Key], rig.Overlays.Created);
        Assert.False(rig.Window.Shown);
        Assert.Empty(rig.Overlays.ShownChanges);
        Assert.Empty(rig.Showing);
        Assert.False(rig.Overlays.InputEnabled);
    }

    /// <summary>「目の前」なら、ショートカットで目の前の空間に、顔の方を向いて開く。手首の板が出ていても手首には付けない。</summary>
    [Fact]
    public void AShortcutOpensTheWindowInFront()
    {
        var rig = new Rig();

        rig.StartOpen("hrs");

        Assert.True(rig.Window.Shown);
        Assert.True(rig.Window.Visible);
        Assert.False(rig.Window.OnWrist);
        Assert.Equal("hrs", rig.Window.View);
        Assert.Equal(["hrs"], rig.Showing);
        Assert.Equal([true], rig.Overlays.ShownChanges);
        Assert.Empty(rig.Overlays.Attached);

        var expected = VrWindowPlacement.InFront(Head);
        AssertNear(expected, rig.Window.Pose);
        AssertNear(expected, rig.Overlays.Placed.Last());
    }

    [Fact]
    public void TheSameShortcutAgainCloses()
    {
        var rig = new Rig();
        rig.StartOpen("hrs");

        rig.Shortcut("hrs");

        Assert.False(rig.Window.Shown);
        Assert.Null(rig.Window.View);
        Assert.Equal(["hrs", null], rig.Showing);
        Assert.Equal([true, false], rig.Overlays.ShownChanges);
    }

    [Fact]
    public void AnotherShortcutSwitchesWithoutMoving()
    {
        var rig = new Rig();
        rig.StartOpen("hrs");
        var placed = rig.Window.Pose;

        rig.Overlays.Pose[0] = VrMatrix.Translation(1, 1.6f, 1);
        rig.Shortcut("videourl");

        Assert.True(rig.Window.Shown);
        Assert.Equal("videourl", rig.Window.View);
        Assert.Equal(["hrs", "videourl"], rig.Showing);
        Assert.Equal(placed, rig.Window.Pose);
        Assert.Single(rig.Overlays.Placed);
    }

    /// <summary>窓のサイドバーで画面を替えたら、その画面のショートカットで閉じ、前の画面のショートカットなら切り替える（C# が覚えている画面が古いままだと逆になる）。</summary>
    [Fact]
    public void AfterTheSidebarSwitchesTheShortcutsFollow()
    {
        var rig = new Rig();
        rig.StartOpen("hrs");

        rig.Window.Viewing("log");
        rig.Shortcut("hrs");
        Assert.True(rig.Window.Shown);
        Assert.Equal("hrs", rig.Window.View);

        rig.Window.Viewing("log");
        rig.Shortcut("log");
        Assert.False(rig.Window.Shown);
    }

    [Fact]
    public void AViewingReportWhileClosedIsIgnored()
    {
        var rig = new Rig();
        rig.Start();

        rig.Window.Viewing("log");
        rig.Tick();

        Assert.Null(rig.Window.View);
        Assert.False(rig.Window.Shown);
    }

    [Fact]
    public void TheCrossCloses()
    {
        var rig = new Rig();
        rig.StartOpen();

        rig.Window.Close();
        rig.Tick();

        Assert.False(rig.Window.Shown);
        Assert.Equal(["hrs", null], rig.Showing);
    }

    /// <summary>窓の画面が変わったら、そのたびに 1 回だけ手首へ知らせる（ショートカットの枠をすぐ塗り直す）。同じ画面の報せ・閉じている間の古い報せでは知らせない。</summary>
    [Fact]
    public void TheWristHearsEachChangeOfScreenOnce()
    {
        var rig = new Rig();
        rig.Start();
        rig.Tick();
        Assert.Empty(rig.Views);

        rig.Shortcut("hrs");
        Assert.Equal(["hrs"], rig.Views);

        rig.Shortcut("videourl");
        Assert.Equal(["hrs", "videourl"], rig.Views);

        // サイドバー。browser を起こす口（Showing）は呼ばないが、手首には知らせる。
        rig.Window.Viewing("log");
        Assert.Equal(["hrs", "videourl", "log"], rig.Views);
        Assert.Equal(["hrs", "videourl"], rig.Showing);

        rig.Window.Viewing("log");
        rig.Tick();
        Assert.Equal(["hrs", "videourl", "log"], rig.Views);

        rig.Shortcut("log");
        Assert.Equal(["hrs", "videourl", "log", null], rig.Views);

        rig.Window.Viewing("hrs");
        rig.Tick();
        Assert.Equal(["hrs", "videourl", "log", null], rig.Views);
    }

    [Fact]
    public void TheWristHearsWhenTheWindowGoesAway()
    {
        var cross = new Rig();
        cross.StartOpen("hrs");
        cross.Window.Close();
        cross.Tick();
        Assert.Equal(["hrs", null], cross.Views);

        var stopped = new Rig();
        stopped.StartOpen("hrs");
        stopped.Window.Stop();
        stopped.Window.Stop();
        Assert.Equal(["hrs", null], stopped.Views);
    }

    /// <summary>手首と一緒に隠れただけなら知らせない（また見たら同じ画面で出るので、枠も残す）。同じ周に押し直したら、知らせるのは片付いた先の 1 回だけ。</summary>
    [Fact]
    public void HidingWithTheWristOrPressingTwiceInOneTickTellsTheWristNothingExtra()
    {
        var rig = OnWrist();
        rig.StartOpen("hrs");

        rig.WristShown = false;
        rig.Tick();
        rig.WristShown = true;
        rig.Tick();
        Assert.Equal(["hrs"], rig.Views);

        rig.Window.Toggle("videourl");
        rig.Window.Toggle("log");
        rig.Tick();
        Assert.Equal(["hrs", "log"], rig.Views);
    }

    /// <summary>繋がる前に押されたショートカットは捨てる（預かると、SteamVR を起動した瞬間に窓が開く）。</summary>
    [Fact]
    public void AShortcutBeforeConnectingIsDropped()
    {
        var rig = new Rig();

        rig.Window.Toggle("hrs");
        rig.Start();
        rig.Ticks(2);

        Assert.False(rig.Window.Shown);
    }

    /// <summary>頭が追えていなければ開かない（置き場が決まらない）。頼みは捨てる — 追えた瞬間に勝手に開かない。</summary>
    [Fact]
    public void WithoutTheHeadsetItDoesNotOpen()
    {
        var rig = new Rig();
        rig.Start();
        rig.Overlays.Pose[0] = null;

        rig.Shortcut();
        Assert.False(rig.Window.Shown);

        rig.Overlays.Pose[0] = Head;
        rig.Tick();
        Assert.False(rig.Window.Shown);
    }

    /// <summary>閉じて開き直したら、今の目の前に出し直す（空間に置いた窓の置き場は覚えない）。</summary>
    [Fact]
    public void ReopeningPlacesItAgain()
    {
        var rig = new Rig();
        rig.StartOpen();
        rig.Shortcut();

        var turned = Head.Then(VrMatrix.RotationY(90));
        rig.Overlays.Pose[0] = turned;
        rig.Shortcut();

        AssertNear(VrWindowPlacement.InFront(turned), rig.Window.Pose);
    }

    [Fact]
    public void ThePictureIsHandedOnlyWhileShown()
    {
        var rig = new Rig();
        rig.Start();
        rig.Ticks(2);
        Assert.Equal(0, rig.Window.Uploads);

        rig.Shortcut();
        Assert.Equal(1, rig.Window.Uploads);
    }

    /// <summary>どちらの手でも指せる（手首は付けた手の反対だけ）。</summary>
    [Fact]
    public void EitherHandCanPoint()
    {
        var rig = new Rig();
        rig.StartOpen();

        rig.Point(VrHand.Left, 100, 100);
        rig.Tick();
        Assert.Equal(["Move 100,100"], rig.Sent);

        rig.PointAway(VrHand.Left);
        rig.Point(VrHand.Right, 200, 200);
        rig.Tick();
        Assert.Equal(["Move 100,100", "Leave 100,100", "Move 200,200"], rig.Sent);
    }

    /// <summary>
    /// 1 枚の窓を指せるのは 1 本の手だけ（先に指した手）。CEF のマウスは 1 つなので、両手の Move が混ざるとカーソルが跳ぶ。
    /// 先の手が外れたら、もう一方の手が指せる。
    /// </summary>
    [Fact]
    public void OnlyTheFirstHandPointsAtTheWindow()
    {
        var rig = new Rig();
        rig.StartOpen();

        rig.Point(VrHand.Left, 100, 100);
        rig.Tick();
        rig.Point(VrHand.Right, 200, 200);
        rig.Tick();
        Assert.Equal(["Move 100,100"], rig.Sent);

        rig.PointAway(VrHand.Left);
        rig.Ticks(2);
        Assert.Equal(["Move 100,100", "Leave 100,100", "Move 200,200"], rig.Sent);
    }

    [Fact]
    public void DraggingWithTheTriggerScrollsTheWindow()
    {
        var rig = new Rig();
        rig.StartOpen();
        rig.Point(VrHand.Right, 640, 360);
        rig.Tick();

        rig.Trigger(VrHand.Right, true);
        rig.Tick();
        rig.Point(VrHand.Right, 640, 460);
        rig.Tick();
        rig.Trigger(VrHand.Right, false);
        rig.Tick();

        Assert.Contains(rig.Sent, sent => sent.StartsWith("Pan 0,"));
        Assert.DoesNotContain(rig.Sent, sent => sent.StartsWith("Down"));
    }

    /// <summary>窓は動かさずに引き続けると右クリック（いつものメニューで貼り付ける）。時計は窓の持ち主ではなく、場面（<see cref="VrScene"/>）からレーザーへ渡る。</summary>
    [Fact]
    public void HoldingTheTriggerStillOnTheWindowRightClicks()
    {
        var rig = new Rig();
        rig.StartOpen();
        rig.Point(VrHand.Right, 640, 360);
        rig.Tick();

        rig.Trigger(VrHand.Right, true);
        rig.Tick();
        rig.Now = VrLaser.LongPressMilliseconds;
        rig.Tick();
        rig.Trigger(VrHand.Right, false);
        rig.Tick();

        Assert.Equal(["Move 640,360", "Down 640,360 Right", "Up 640,360 Right"], rig.Sent);
    }

    /// <summary>掴む手（cursor: grab）の上で引いたら巻かない。押す・動かす・離すをそのまま送り、ページが自分で掴んで動かす（設定のモーダルのつまみ）。</summary>
    [Fact]
    public void PullingOnAGrabHandInTheWindowDragsInsteadOfScrolling()
    {
        var rig = new Rig { Grabbable = true };
        rig.StartOpen();
        rig.Point(VrHand.Right, 640, 360);
        rig.Tick();

        rig.Trigger(VrHand.Right, true);
        rig.Tick();
        rig.Point(VrHand.Right, 640, 460);
        rig.Tick();
        rig.Trigger(VrHand.Right, false);
        rig.Tick();

        Assert.Equal(["Move 640,360", "Down 640,360", "Move 640,460", "Up 640,460"], rig.Sent);
    }

    /// <summary>Layout Mode の外では掴まない（普段のグリップは VRChat のもの）。</summary>
    [Fact]
    public void OutsideLayoutModeTheGripIsLeftToVrChat()
    {
        var rig = new Rig();
        rig.StartOpen();
        var before = rig.Window.Pose;

        rig.Point(VrHand.Right);
        rig.Overlays.Pose[Rig.Controller(VrHand.Right)] = rig.AimAt();
        rig.Grip(VrHand.Right, true);
        rig.Tick();
        rig.MoveHand(VrHand.Right, (0.2f, 0.1f, 0f));
        rig.Tick();

        Assert.False(rig.Window.Dragging);
        Assert.Equal(before, rig.Window.Pose);
    }

    /// <summary>掴んでいる間に Layout Mode が終わったら、そこで置く（そのグリップは VRChat にも届いているので、物を掴む操作と窓の移動が一緒に起きる）。</summary>
    [Fact]
    public void EndingLayoutModeWhileHoldingPutsTheWindowDown()
    {
        var rig = new Rig();
        rig.StartOpen();
        rig.Grab();
        rig.MoveHand(VrHand.Right, (0.1f, 0f, 0f));
        rig.Tick();
        Assert.True(rig.Window.Dragging);

        rig.Layout = false;
        rig.Tick();
        var putDown = rig.Window.Pose;
        rig.MoveHand(VrHand.Right, (0.2f, 0.1f, 0f));
        rig.Tick();

        Assert.False(rig.Window.Dragging);
        Assert.Equal(putDown, rig.Window.Pose);
    }

    /// <summary>Layout Mode の間は中身を押せない（窓のどこでも掴めるように）。点は動く（どこを掴むかが見える）。</summary>
    [Fact]
    public void InLayoutModeAPressOnTheWindowIsNotSent()
    {
        var rig = new Rig();
        rig.StartOpen();
        rig.Layout = true;
        rig.Point(VrHand.Right, 640, 360);
        rig.Tick();

        rig.Trigger(VrHand.Right, true);
        rig.Tick();
        rig.Trigger(VrHand.Right, false);
        rig.Tick();

        Assert.Equal(["Move 640,360"], rig.Sent);
    }

    [Fact]
    public void InLayoutModeDraggingTheTriggerDoesNotScroll()
    {
        var rig = new Rig();
        rig.StartOpen();
        rig.Layout = true;
        rig.Point(VrHand.Right, 640, 360);
        rig.Tick();

        rig.Trigger(VrHand.Right, true);
        rig.Tick();
        rig.Point(VrHand.Right, 640, 460);
        rig.Tick();
        rig.Trigger(VrHand.Right, false);
        rig.Tick();

        Assert.Equal(["Move 640,360", "Move 640,460"], rig.Sent);
    }

    /// <summary>Layout Mode の間は引き続けても右クリックのメニューを出さず、長押しの合図（振動）も出さない（メニューが出ないのに震えると、出るものと思う）。</summary>
    [Fact]
    public void InLayoutModeHoldingTheTriggerDoesNotOpenTheMenu()
    {
        var rig = new Rig();
        rig.StartOpen();
        rig.Layout = true;
        rig.Point(VrHand.Right, 640, 360);
        rig.Tick();

        rig.Trigger(VrHand.Right, true);
        rig.Tick();
        rig.Now = VrLaser.LongPressMilliseconds;
        rig.Tick();
        rig.Trigger(VrHand.Right, false);
        rig.Tick();

        Assert.Equal(["Move 640,360"], rig.Sent);
        Assert.DoesNotContain(VrLaser.LongPressPulseSeconds, rig.Overlays.Pulses);
    }

    /// <summary>Layout Mode の間も、外れたことは送る（送らないと <c>:hover</c> が残る）。</summary>
    [Fact]
    public void InLayoutModeLeavingTheWindowStillClearsTheHover()
    {
        var rig = new Rig();
        rig.StartOpen();
        rig.Layout = true;
        rig.Point(VrHand.Right, 100, 100);
        rig.Tick();

        rig.PointAway(VrHand.Right);
        rig.Tick();

        Assert.Equal(["Move 100,100", "Leave 100,100"], rig.Sent);
    }

    /// <summary>Layout Mode の間は押せる物の上でも震えない（押せないので）。出たら、また震える。</summary>
    [Fact]
    public void InLayoutModeButtonsDoNotBuzz()
    {
        var rig = new Rig();
        rig.StartOpen();
        rig.Clickable = true;
        rig.Layout = true;
        rig.Point(VrHand.Right);
        rig.Ticks(3);

        Assert.DoesNotContain(VrLaser.HoverPulseSeconds, rig.Overlays.Pulses);

        rig.Layout = false;
        rig.Ticks(2);

        Assert.Contains(VrLaser.HoverPulseSeconds, rig.Overlays.Pulses);
    }

    /// <summary>閉じている窓は指さない（見えていない物を押せてしまう）。</summary>
    [Fact]
    public void AClosedWindowIsNotPointedAt()
    {
        var rig = new Rig();
        rig.StartOpen();
        var aim = rig.AimAt();
        rig.Shortcut();

        rig.Overlays.Hand(VrHand.Right, input => input with { Aim = aim, Click = true });
        rig.Tick();

        Assert.Empty(rig.Sent);
    }

    /// <summary>ダッシュボードが開いている間は指さない（SteamVR のレーザーと 2 本になる）。</summary>
    [Fact]
    public void TheWindowIsNotPointedAtWhileTheDashboardIsOpen()
    {
        var rig = new Rig();
        rig.StartOpen();
        rig.Overlays.DashboardIsOpen = true;

        rig.Point(VrHand.Right);
        rig.Tick();

        Assert.Empty(rig.Sent);
    }

    /// <summary>ダッシュボードが開いている間は隠す（重なって邪魔になる）。閉じたわけではない。画面はそのまま、browser は眠らせ、ダッシュボードを閉じたら同じ所・同じ画面で出る。</summary>
    [Fact]
    public void TheDashboardHidesTheWindowWithoutClosingIt()
    {
        var rig = new Rig();
        rig.StartOpen("hrs");
        var pose = rig.Window.Pose;

        rig.Overlays.DashboardIsOpen = true;
        rig.Tick();

        Assert.True(rig.Window.Shown);
        Assert.False(rig.Window.Visible);
        Assert.Equal("hrs", rig.Window.View);
        Assert.Equal([true, false], rig.Overlays.ShownChanges);
        Assert.Equal([false], rig.Awake);

        rig.Overlays.DashboardIsOpen = false;
        rig.Tick();

        Assert.True(rig.Window.Visible);
        Assert.Equal([true, false, true], rig.Overlays.ShownChanges);
        Assert.Equal([false, true], rig.Awake);
        Assert.Equal(["hrs"], rig.Showing);
        AssertNear(pose, rig.Window.Pose);
    }

    /// <summary>
    /// 手首に付けた窓も隠す。手首の板がまだ出ていても窓の側で見る（本物の手首はダッシュボードで隠れるが、それに頼りきらない）。
    /// </summary>
    [Fact]
    public void TheDashboardHidesAWindowOnTheWristToo()
    {
        var rig = OnWrist();
        rig.StartOpen("hrs");

        rig.Overlays.DashboardIsOpen = true;
        rig.Tick();

        Assert.True(rig.Window.Shown);
        Assert.False(rig.Window.Visible);

        rig.Overlays.DashboardIsOpen = false;
        rig.Tick();

        Assert.True(rig.Window.Visible);
    }

    /// <summary>
    /// 掴んでいる最中にダッシュボードが開いたら、動かしたところで置く（閉じたあとも手に付いてこない）。
    /// </summary>
    [Fact]
    public void TheDashboardOpeningWhileMovingPutsItDown()
    {
        var rig = new Rig();
        rig.StartOpen();
        rig.Grab(VrHand.Right);

        rig.MoveHand(VrHand.Right, (0.1f, 0f, 0f));
        rig.Tick();
        var moved = rig.Window.Pose;

        rig.Overlays.DashboardIsOpen = true;
        rig.Tick();

        Assert.False(rig.Window.Dragging);

        rig.Overlays.DashboardIsOpen = false;
        rig.MoveHand(VrHand.Right, (0.2f, 0f, 0f));
        rig.Tick();

        AssertNear(moved, rig.Window.Pose);
    }

    /// <summary>
    /// Layout Mode の間は指してグリップで掴める。掴み始めた周はまだ動かさず、次の周から手に付いてくる。離したら置く。
    /// </summary>
    [Fact]
    public void GripWhilePointingMovesTheWindowWithTheHand()
    {
        var rig = new Rig();
        rig.StartOpen();
        var before = rig.Window.Pose;

        rig.Grab(VrHand.Right);
        Assert.True(rig.Window.Dragging);
        Assert.Equal(before, rig.Window.Pose);

        rig.MoveHand(VrHand.Right, (0.2f, 0.1f, 0f));
        rig.Tick();

        var (x, y, z) = rig.Window.Pose.Position;
        var (bx, by, bz) = before.Position;
        Assert.Equal(bx + 0.2f, x, 1e-4f);
        Assert.Equal(by + 0.1f, y, 1e-4f);
        Assert.Equal(bz, z, 1e-4f);
        AssertNear(rig.Window.Pose, rig.Overlays.Placed.Last());

        rig.Grip(VrHand.Right, false);
        rig.Tick();
        Assert.False(rig.Window.Dragging);
    }

    /// <summary>
    /// 掴んでいる間は指さない — 窓は手に付いて動くので点が同じ所に居続け、トリガーでその下のボタンが押される
    /// （しかも掴んだままのトリガーは「大きさを変える」の合図）。
    /// </summary>
    [Fact]
    public void NothingIsPointedAtWhileTheWindowIsMoved()
    {
        var rig = new Rig();
        rig.StartOpen();
        rig.Grab(VrHand.Right);
        rig.Sent.Clear();

        rig.Trigger(VrHand.Right, true);
        rig.Point(VrHand.Left);
        rig.Trigger(VrHand.Left, true);
        rig.Tick();
        rig.Trigger(VrHand.Right, false);
        rig.Trigger(VrHand.Left, false);
        rig.Tick();

        Assert.DoesNotContain(rig.Sent, sent => sent.StartsWith("Down") || sent.StartsWith("Up") || sent.StartsWith("Move"));
    }

    /// <summary>掴んだままトリガーを引いて手を前へ押すと縮む（30 cm で半分）。その間、窓はその場に止める。</summary>
    [Fact]
    public void PushingForwardWithTheTriggerShrinksTheWindowInPlace()
    {
        var rig = new Rig();
        rig.StartOpen();
        var start = rig.Window.WidthInMeters;
        rig.Grab(VrHand.Right);
        var placed = rig.Window.Pose;

        rig.Trigger(VrHand.Right, true);
        rig.Tick();

        var (fx, fy, fz) = VrWindowPlacement.Forward(placed);
        rig.MoveHand(VrHand.Right, (fx * 0.3f, fy * 0.3f, fz * 0.3f));
        rig.Tick();

        Assert.Equal(start / 2f, rig.Window.WidthInMeters, 1e-4f);
        Assert.Equal(start / 2f, rig.Overlays.Widths.Last(), 1e-4f);
        Assert.Equal(placed, rig.Window.Pose);
    }

    [Fact]
    public void PullingBackWithTheTriggerEnlargesTheWindow()
    {
        var rig = new Rig();
        rig.StartOpen();
        var start = rig.Window.WidthInMeters;
        rig.Grab(VrHand.Right);

        rig.Trigger(VrHand.Right, true);
        rig.Tick();

        var (fx, fy, fz) = VrWindowPlacement.Forward(rig.Window.Pose);
        rig.MoveHand(VrHand.Right, (-fx * 0.3f, -fy * 0.3f, -fz * 0.3f));
        rig.Tick();

        Assert.Equal(start * 2f, rig.Window.WidthInMeters, 1e-4f);
    }

    /// <summary>
    /// トリガーを離したら、また手に付いてくる。大きさを変えている間に動かした手の分だけ跳ばない
    /// （離した時の手から見た置き場を取り直す）。
    /// </summary>
    [Fact]
    public void LettingGoOfTheTriggerFollowsTheHandAgainWithoutAJump()
    {
        var rig = new Rig();
        rig.StartOpen();
        rig.Grab(VrHand.Right);
        var placed = rig.Window.Pose;

        rig.Trigger(VrHand.Right, true);
        rig.Tick();
        rig.MoveHand(VrHand.Right, (0f, 0f, 0.1f));
        rig.Tick();

        rig.Trigger(VrHand.Right, false);
        rig.Tick();
        AssertNear(placed, rig.Window.Pose);

        rig.MoveHand(VrHand.Right, (0.1f, 0f, 0f));
        rig.Tick();
        Assert.Equal(placed.Position.X + 0.1f, rig.Window.Pose.Position.X, 1e-4f);
    }

    /// <summary>置いたら大きさを覚える（次に開いたときも同じ大きさ）。変えていなければ書かない。</summary>
    [Fact]
    public void PuttingItDownKeepsTheWidth()
    {
        var rig = new Rig();
        rig.StartOpen();
        rig.Grab(VrHand.Right);
        rig.Grip(VrHand.Right, false);
        rig.Tick();
        Assert.Equal(0, rig.Settings.Saves);

        rig.Grab(VrHand.Right);
        rig.Trigger(VrHand.Right, true);
        rig.Tick();
        var (fx, fy, fz) = VrWindowPlacement.Forward(rig.Window.Pose);
        rig.MoveHand(VrHand.Right, (-fx * 0.3f, -fy * 0.3f, -fz * 0.3f));
        rig.Tick();
        rig.Grip(VrHand.Right, false);
        rig.Trigger(VrHand.Right, false);
        rig.Tick();

        Assert.Equal(1, rig.Settings.Saves);
        Assert.Equal(VrWindowHost.DefaultWidth * 2f, rig.Settings.Width!.Value, 1e-4f);
    }

    /// <summary>置いたら歪みを取ってから置き直す（掴み直すたびに SteamVR の姿勢の誤差が積もる）。</summary>
    [Fact]
    public void PuttingItDownTakesOutTheDrift()
    {
        var rig = new Rig();
        rig.StartOpen();
        rig.Grab(VrHand.Right);

        // 掴んだ手の回転を少しだけ伸ばす（SteamVR の姿勢の誤差に見立てる）。
        var grip = rig.Overlays.Pose[RightController]!.Value;
        rig.Overlays.Pose[RightController] = grip with { M0 = grip.M0 * 1.01f, M4 = grip.M4 * 1.01f, M8 = grip.M8 * 1.01f };
        rig.Tick();

        var (mx, my, mz) = rig.Window.Pose.AxisX;
        Assert.True(MathF.Abs(mx * mx + my * my + mz * mz - 1) > 1e-3f, "動かした窓が歪んでいない（試験の前提が崩れている）");

        rig.Grip(VrHand.Right, false);
        rig.Tick();

        WristPlacementTests.AssertRigid(rig.Window.Pose);
        WristPlacementTests.AssertRigid(rig.Overlays.Placed.Last());
    }

    [Fact]
    public void ItOpensAtTheSavedWidth()
    {
        var rig = new Rig();
        rig.Settings.Width = 0.8f;

        rig.StartOpen();

        Assert.Equal(0.8f, rig.Window.WidthInMeters);
    }

    [Fact]
    public void LosingTheHandPutsItDown()
    {
        var rig = new Rig();
        rig.StartOpen();
        rig.Grab(VrHand.Right);

        rig.Overlays.Pose[RightController] = null;
        rig.Tick();

        Assert.False(rig.Window.Dragging);
    }

    [Fact]
    public void ClosingWhileMovingPutsItDownFirst()
    {
        var rig = new Rig();
        rig.StartOpen();
        rig.Grab(VrHand.Right);

        rig.Shortcut();

        Assert.False(rig.Window.Dragging);
        Assert.False(rig.Window.Shown);
    }

    /// <summary>「手首の上」なら、手首の板の真上に同じ面で付く（ディスプレイとして）。付ける先は手首のコントローラー、窓の下端は板の上端の 3 cm 上。手首の大きさで開く。</summary>
    [Fact]
    public void OnTheWristItSitsAboveTheWristBoardAsADisplay()
    {
        var rig = OnWrist();

        rig.StartOpen();

        Assert.True(rig.Window.OnWrist);
        Assert.True(rig.Window.Visible);
        Assert.Equal(VrWindowHost.DefaultWristWidth, rig.Window.WidthInMeters);
        Assert.Empty(rig.Overlays.Placed);

        var (device, offset) = Assert.Single(rig.Overlays.Attached);
        Assert.Equal(LeftController, device);
        AssertNear(WristOffset.Then(VrWindowPlacement.OnWrist(WristBoardHeight, rig.Window.HeightInMeters)), offset);
        AssertNear(rig.Overlays.Pose[LeftController]!.Value.Then(offset), rig.Window.Pose);
    }

    /// <summary>腕と一緒に動く（手首の機器に付けてあるので、付け直さなくても付いてくる）。</summary>
    [Fact]
    public void ItMovesWithTheWrist()
    {
        var rig = OnWrist();
        rig.StartOpen();
        var (_, offset) = rig.Overlays.Attached.Single();

        rig.MoveHand(VrHand.Left, (0.2f, 0.1f, 0f));
        rig.Tick();

        Assert.Single(rig.Overlays.Attached);
        AssertNear(rig.Overlays.Pose[LeftController]!.Value.Then(offset), rig.Window.Pose);
    }

    /// <summary>
    /// 手首の板がフェードしている間は、窓も同じ透明度（一緒に薄くなる・濃くなる）。
    /// 手首が消えていく途中は指させない（薄くなっていく窓を押せてしまう）。また出たら指せる。
    /// </summary>
    [Fact]
    public void TheWindowFadesWithTheWristAndIsNotPointedAtWhileItGoes()
    {
        var rig = OnWrist();
        rig.StartOpen("hrs");
        Assert.Empty(rig.Overlays.Alphas);

        rig.WristAlpha = 0.4f;
        rig.WristLive = false;
        rig.Tick();
        rig.Point(VrHand.Right);
        rig.Tick();

        Assert.True(rig.Window.Visible);
        Assert.Equal([0.4f], rig.Overlays.Alphas);
        Assert.Empty(rig.Sent);

        rig.WristAlpha = 1f;
        rig.WristLive = true;
        rig.Tick();
        rig.Point(VrHand.Right);
        rig.Tick();

        Assert.Equal([0.4f, 1f], rig.Overlays.Alphas);
        Assert.NotEmpty(rig.Sent);
    }

    /// <summary>掴んでいる間に手首が消え始めたら、その周で置く（消え終わるのを待たない。薄くなっていく窓を持ち歩かない）。</summary>
    [Fact]
    public void AGrabbedWindowIsPutDownWhenTheWristStartsToGo()
    {
        var rig = OnWrist();
        rig.StartOpen("hrs");
        rig.Grab();
        Assert.True(rig.Window.Dragging);

        rig.WristAlpha = 0.8f;
        rig.WristLive = false;
        rig.Tick();

        Assert.False(rig.Window.Dragging);
        Assert.True(rig.Window.Visible);
    }

    /// <summary>
    /// 手首の上で薄くなっている途中に閉じた窓を、目の前で開き直しても薄いまま残さない（空間に置く窓は不透明）。
    /// </summary>
    [Fact]
    public void AWindowReopenedInFrontIsNotLeftFaint()
    {
        var rig = OnWrist();
        rig.StartOpen("hrs");
        rig.WristAlpha = 0.3f;
        rig.Tick();

        rig.Shortcut("hrs");
        rig.Settings.Spot = VrWindowSpot.InFront;
        rig.Shortcut("hrs");

        Assert.True(rig.Window.Visible);
        Assert.Equal(1f, rig.Overlays.Alphas.Last());
    }

    /// <summary>
    /// 手首を下ろして手首の板が隠れたら、窓も一緒に隠れる。閉じたわけではない（画面はそのまま、browser は眠らせる）。
    /// また見たら同じ画面で出る。
    /// </summary>
    [Fact]
    public void LoweringTheWristHidesTheWindowWithoutClosingIt()
    {
        var rig = OnWrist();
        rig.StartOpen("hrs");

        rig.WristShown = false;
        rig.Tick();

        Assert.True(rig.Window.Shown);
        Assert.False(rig.Window.Visible);
        Assert.Equal("hrs", rig.Window.View);
        Assert.Equal([true, false], rig.Overlays.ShownChanges);
        Assert.Equal([false], rig.Awake);
        Assert.Equal(["hrs"], rig.Showing);

        rig.WristShown = true;
        rig.Tick();

        Assert.True(rig.Window.Visible);
        Assert.Equal([true, false, true], rig.Overlays.ShownChanges);
        Assert.Equal([false, true], rig.Awake);
        Assert.Equal(["hrs"], rig.Showing);
    }

    [Fact]
    public void AHiddenWristWindowIsNotFed()
    {
        var rig = OnWrist();
        rig.StartOpen();
        var uploads = rig.Window.Uploads;

        rig.WristShown = false;
        rig.Frame = new VrFrame(99, 2);
        rig.Tick();

        Assert.Equal(uploads, rig.Window.Uploads);
    }

    /// <summary>手首の板が出ていない間に開いたら、開いておいて（browser は眠らせて）、手首を見たら出る。</summary>
    [Fact]
    public void OpeningWhileTheWristIsDownWaitsForIt()
    {
        var rig = OnWrist();
        rig.WristShown = false;

        rig.StartOpen();

        Assert.True(rig.Window.Shown);
        Assert.False(rig.Window.Visible);
        Assert.Equal(["hrs"], rig.Showing);
        Assert.Equal([false], rig.Awake);

        rig.WristShown = true;
        rig.Tick();

        Assert.True(rig.Window.Visible);
        Assert.Equal([false, true], rig.Awake);
    }

    /// <summary>隠れている間に別の画面へ切り替えても、眠らせたまま（切り替えで起きた browser を寝かせ直す）。</summary>
    [Fact]
    public void SwitchingWhileHiddenKeepsTheBrowserAsleep()
    {
        var rig = OnWrist();
        rig.StartOpen("hrs");
        rig.WristShown = false;
        rig.Tick();

        rig.Shortcut("videourl");

        Assert.Equal(["hrs", "videourl"], rig.Showing);
        Assert.Equal([false, false], rig.Awake);
        Assert.False(rig.Window.Visible);
    }

    /// <summary>
    /// 指すのは手首と反対の手だけ（付けた手で指すと、指す手と一緒に窓が動く）。点は手首の機器に付ける（窓と一緒に動くので震えない）。
    /// </summary>
    [Fact]
    public void OnlyTheOtherHandPointsAtAWindowOnTheWrist()
    {
        var rig = OnWrist();
        rig.StartOpen();

        rig.Point(VrHand.Left, 100, 100);
        rig.Tick();
        Assert.Empty(rig.Sent);

        rig.PointAway(VrHand.Left);
        rig.Point(VrHand.Right, 100, 100);
        rig.Tick();
        Assert.Equal(["Move 100,100"], rig.Sent);
        Assert.Equal(LeftController, rig.Overlays.DotsAttached.Values.Single().Device);
    }

    /// <summary>
    /// 掴んでずらすと、手首の板から見た置き場が変わる（窓は手首に付いたまま）。置いたら付けた手ごとに覚え、
    /// そのあと手首を動かすと、ずらした所のまま付いてくる。大きさを変えていなければ幅は書かない。
    /// </summary>
    [Fact]
    public void MovingItShiftsWhereItSitsOnTheWristAndRemembersIt()
    {
        var rig = OnWrist();
        rig.StartOpen();
        var before = rig.Window.Pose;

        rig.Grab(VrHand.Right);
        rig.MoveHand(VrHand.Right, (0.1f, 0f, 0f));
        rig.Tick();

        // 手が動いた分だけ動く（掴んだ瞬間に跳ばない）。X だけ見ると、手首の板の置き場（Y・Z にだけずれがある）を数え間違えても通る。
        AssertNear(VrMatrix.Translation(0.1f, 0f, 0f).Then(before), rig.Window.Pose);
        Assert.Equal(LeftController, rig.Overlays.Attached.Last().Device);

        rig.Grip(VrHand.Right, false);
        rig.Tick();

        Assert.False(rig.Window.Dragging);
        var saved = Assert.Contains(VrHand.Left, rig.Settings.WristOffsets);
        var wrist = rig.Overlays.Pose[LeftController]!.Value;
        AssertNear(WristOffset.Inverse().Then(wrist.Inverse()).Then(rig.Window.Pose), saved);
        Assert.Equal(0, rig.Settings.WristWidthSaves);

        rig.MoveHand(VrHand.Left, (0f, 0.2f, 0f));
        rig.Tick();
        AssertNear(rig.Overlays.Pose[LeftController]!.Value.Then(WristOffset).Then(saved), rig.Window.Pose);
    }

    [Fact]
    public void ItOpensWhereItWasPutForThatHand()
    {
        var rig = OnWrist();
        var put = VrMatrix.Translation(0.05f, 0.2f, 0.01f);
        rig.Settings.WristOffsets[VrHand.Left] = put;

        rig.StartOpen();

        AssertNear(WristOffset.Then(put), rig.Overlays.Attached.Last().Offset);
    }

    /// <summary>
    /// 置き場は開くたびに設定から読み直す（大きさと同じ。前に開いていたときの置き場を持ち越さない）。
    /// 閉じている間に設定の置き場が変わったら、次に開いたときはそこに出る。
    /// </summary>
    [Fact]
    public void ReopeningReadsWhereItWasPutAgain()
    {
        var rig = OnWrist();
        rig.StartOpen();
        rig.Shortcut();
        Assert.False(rig.Window.Shown);

        var put = VrMatrix.Translation(-0.04f, 0.15f, 0.02f);
        rig.Settings.WristOffsets[VrHand.Left] = put;
        rig.Shortcut();

        AssertNear(WristOffset.Then(put), rig.Overlays.Attached.Last().Offset);
    }

    /// <summary>
    /// 置いたら歪みを取ってから覚える（手首の板と同じ。掴み直すたびに SteamVR の姿勢の誤差が積もる）。
    /// 掴んだ手の姿勢をわざと剛体から外して動かし、覚えた置き場が剛体になっていること。
    /// </summary>
    [Fact]
    public void PuttingItDownOnTheWristTakesOutTheDrift()
    {
        var rig = OnWrist();
        rig.StartOpen();
        rig.Grab(VrHand.Right);

        // 掴んだ手の回転を少しだけ伸ばす（SteamVR の姿勢の誤差に見立てる）。
        var grip = rig.Overlays.Pose[RightController]!.Value;
        rig.Overlays.Pose[RightController] = grip with { M0 = grip.M0 * 1.01f, M4 = grip.M4 * 1.01f, M8 = grip.M8 * 1.01f };
        rig.Tick();

        var (mx, my, mz) = rig.Overlays.Attached.Last().Offset.AxisX;
        Assert.True(MathF.Abs(mx * mx + my * my + mz * mz - 1) > 1e-3f, "動かした置き場が歪んでいない（試験の前提が崩れている）");

        rig.Grip(VrHand.Right, false);
        rig.Tick();

        WristPlacementTests.AssertRigid(rig.Settings.WristOffsets[VrHand.Left]);
        WristPlacementTests.AssertRigid(rig.Overlays.Attached.Last().Offset);
    }

    /// <summary>
    /// 手首に付けた窓の大きさを変えるときは、下端を保ったまま大きくする（ディスプレイが板の上へ伸びる。真ん中を保つと板に被る）。
    /// 幅は手首の分として覚える（空間に置く窓の幅は触らない）。
    /// </summary>
    [Fact]
    public void ResizingOnTheWristKeepsTheBottomEdge()
    {
        var rig = OnWrist();
        rig.StartOpen();
        var bottom = BottomEdge(rig);

        rig.Grab(VrHand.Right);
        rig.Trigger(VrHand.Right, true);
        rig.Tick();

        var (fx, fy, fz) = VrWindowPlacement.Forward(rig.Window.Pose);
        rig.MoveHand(VrHand.Right, (-fx * 0.3f, -fy * 0.3f, -fz * 0.3f));
        rig.Tick();

        Assert.Equal(VrWindowHost.DefaultWristWidth * 2f, rig.Window.WidthInMeters, 1e-4f);
        AssertNear(bottom, BottomEdge(rig));

        rig.Grip(VrHand.Right, false);
        rig.Trigger(VrHand.Right, false);
        rig.Tick();

        Assert.Equal(VrWindowHost.DefaultWristWidth * 2f, rig.Settings.WristWidth!.Value, 1e-4f);
        Assert.Equal(0, rig.Settings.Saves);
    }

    [Fact]
    public void LoweringTheWristWhileMovingPutsItDown()
    {
        var rig = OnWrist();
        rig.StartOpen();
        rig.Grab(VrHand.Right);
        rig.MoveHand(VrHand.Right, (0.1f, 0f, 0f));
        rig.Tick();

        rig.WristShown = false;
        rig.Tick();

        Assert.False(rig.Window.Dragging);
        Assert.Contains(VrHand.Left, rig.Settings.WristOffsets);
    }

    /// <summary>
    /// 手首の板を付ける手を替えたら、その手の手首へ移り、その手の置き場を使う。掴んでいたら、前の手の置き場として覚えてから。
    /// </summary>
    [Fact]
    public void ChangingTheWristHandMovesItToTheOtherWrist()
    {
        var rig = OnWrist();
        var right = VrMatrix.Translation(-0.05f, 0.2f, 0f);
        rig.Settings.WristOffsets[VrHand.Right] = right;
        rig.StartOpen();
        rig.Grab(VrHand.Right);
        rig.MoveHand(VrHand.Right, (0.05f, 0f, 0f));
        rig.Tick();

        rig.WristHand = VrHand.Right;
        rig.Tick();

        Assert.False(rig.Window.Dragging);
        Assert.Contains(VrHand.Left, rig.Settings.WristOffsets);

        var (device, offset) = rig.Overlays.Attached.Last();
        Assert.Equal(RightController, device);
        AssertNear(WristOffset.Then(right), offset);
    }

    /// <summary>
    /// 置き方は開いたときに決まる（開いている間に設定を変えても窓は跳ばない）。次に開いたときから効く。
    /// 大きさは置き方ごとに覚えている。
    /// </summary>
    [Fact]
    public void TheSpotIsFixedWhenOpenedAndEachKeepsItsWidth()
    {
        var rig = OnWrist();
        rig.Settings.Width = 0.8f;
        rig.Settings.WristWidth = 0.25f;
        rig.StartOpen();
        Assert.Equal(0.25f, rig.Window.WidthInMeters);

        rig.Settings.Spot = VrWindowSpot.InFront;
        rig.Tick();
        Assert.True(rig.Window.OnWrist);

        // 旗だけでなく、窓が手首に付いたまま腕と一緒に動くこと。
        var (_, offset) = rig.Overlays.Attached.Last();
        rig.MoveHand(VrHand.Left, (0.2f, 0.1f, 0f));
        rig.Tick();
        AssertNear(rig.Overlays.Pose[LeftController]!.Value.Then(offset), rig.Window.Pose);

        rig.Shortcut();
        rig.Shortcut();

        Assert.False(rig.Window.OnWrist);
        Assert.Equal(0.8f, rig.Window.WidthInMeters);
        AssertNear(VrWindowPlacement.InFront(Head), rig.Window.Pose);
    }

    private static Rig OnWrist()
    {
        var rig = new Rig();
        rig.Settings.Spot = VrWindowSpot.AboveWrist;
        return rig;
    }

    private static VrMatrix BottomEdge(Rig rig) => rig.Window.Pose.Then(VrMatrix.Translation(0, -rig.Window.HeightInMeters / 2f, 0));

    [Theory]
    [InlineData(null, VrWindowHost.DefaultWidth)]
    [InlineData(float.NaN, VrWindowHost.DefaultWidth)]
    [InlineData(0.05f, VrWindowHost.MinWidth)]
    [InlineData(9f, VrWindowHost.MaxWidth)]
    [InlineData(0.8f, 0.8f)]
    public void TheWidthIsKeptInRange(float? given, float expected) =>
        Assert.Equal(expected, VrWindowHost.ClampWidth(given));

    /// <summary>
    /// 窓が転んでも外へ投げない。 投げると VR の周ごと止まり、ダッシュボードと手首まで消える。窓だけ畳む（browser は眠らせる）。
    /// </summary>
    [Fact]
    public void AFailingWindowStopsItselfWithoutThrowing()
    {
        var rig = new Rig();
        rig.StartOpen();
        rig.Overlays.PosesFail = true;

        rig.Tick();
        rig.Tick();

        Assert.False(rig.Window.Running);
        Assert.Equal(1, rig.Overlays.Destroyed);
        Assert.Equal(["hrs", null], rig.Showing);
    }

    [Fact]
    public void StoppingPutsTheWindowAwayOnce()
    {
        var rig = new Rig();
        rig.StartOpen();

        rig.Window.Stop();
        rig.Window.Stop();

        Assert.False(rig.Window.Running);
        Assert.Equal(1, rig.Overlays.Destroyed);
        Assert.Equal(["hrs", null], rig.Showing);
    }

    private static void AssertNear(VrMatrix expected, VrMatrix actual)
    {
        var e = expected.ToArray();
        var a = actual.ToArray();
        for (var i = 0; i < 12; i++) Assert.Equal(e[i], a[i], 1e-4f);
    }

    private sealed class FakeSettings : IVrWindowSettings
    {
        /// <summary>既定は空間に置く方（前半の試験）。手首に付ける試験は <c>OnWrist()</c> で替える。</summary>
        public VrWindowSpot Spot { get; set; } = VrWindowSpot.InFront;

        public float? Width { get; set; }

        public int Saves { get; private set; }

        public void SaveWidth(float? meters)
        {
            Width = meters;
            Saves++;
        }

        public float? WristWidth { get; set; }

        public int WristWidthSaves { get; private set; }

        public void SaveWristWidth(float? meters)
        {
            WristWidth = meters;
            WristWidthSaves++;
        }

        public Dictionary<VrHand, VrMatrix> WristOffsets { get; } = [];

        public VrMatrix? WristOffset(VrHand hand) => WristOffsets.TryGetValue(hand, out var m) ? m : null;

        public void SaveWristOffset(VrHand hand, VrMatrix? offset)
        {
            if (offset is { } m) WristOffsets[hand] = m;
            else WristOffsets.Remove(hand);
        }
    }

    private sealed class FakeOverlays : IVrOverlays
    {
        /// <summary>窓の板の handle。置く・出す・消すは、これの分だけ数える（自前のポインタの点は別）。</summary>
        private const ulong WindowHandle = 30;

        public Dictionary<uint, VrMatrix?> Pose { get; } = new()
        {
            [0] = Head,
            [LeftController] = VrMatrix.Translation(-0.2f, 1.0f, -0.3f),
            [RightController] = VrMatrix.Translation(0.3f, 1.2f, -0.3f),
        };

        public List<string> Created { get; } = [];

        public List<VrMatrix> Placed { get; } = [];

        public List<bool> ShownChanges { get; } = [];

        public List<float> Widths { get; } = [];

        public bool InputEnabled { get; private set; }

        public bool PosesFail { get; set; }

        public int Destroyed { get; private set; }

        public bool DashboardIsOpen { get; set; }

        private VrHandInput _left;

        private VrHandInput _right;

        public void Hand(VrHand hand, Func<VrHandInput, VrHandInput> change)
        {
            if (hand == VrHand.Left) _left = change(_left);
            else _right = change(_right);
        }

        public VrHands ReadHands(bool captureAll) => new(_left, _right);

        public ulong CreateOverlay(string key, string name, float widthInMeters)
        {
            Created.Add(key);
            return WindowHandle;
        }

        public void PlaceAbsolute(ulong handle, VrMatrix pose)
        {
            if (handle == WindowHandle) Placed.Add(pose);
        }

        public void SetShown(ulong handle, bool shown)
        {
            if (handle == WindowHandle) ShownChanges.Add(shown);
        }

        public List<float> Alphas { get; } = [];

        public void SetAlpha(ulong handle, float alpha)
        {
            if (handle == WindowHandle) Alphas.Add(alpha);
        }

        public void SetWidth(ulong handle, float widthInMeters)
        {
            if (handle == WindowHandle) Widths.Add(widthInMeters);
        }

        public void Destroy(ulong handle)
        {
            if (handle == WindowHandle) Destroyed++;
        }

        public IReadOnlyList<VrMatrix?> Poses()
        {
            if (PosesFail) throw new InvalidOperationException("the runtime went away");

            var poses = new VrMatrix?[8];
            foreach (var (device, pose) in Pose) poses[device] = pose;
            return poses;
        }

        public uint? ControllerFor(VrHand hand) => hand == VrHand.Left ? LeftController : RightController;

        public bool DashboardOpen() => DashboardIsOpen;

        private ulong _sprites = 40;

        public ulong CreateSprite(string key, string name, VrSprite sprite, float widthInMeters) => ++_sprites;

        public List<(uint Device, VrMatrix Offset)> Attached { get; } = [];

        /// <summary>自前のポインタの点を機器に付けた所（点の handle ごとに最後のもの）。</summary>
        public Dictionary<ulong, (uint Device, VrMatrix Offset)> DotsAttached { get; } = [];

        public void Attach(ulong handle, uint device, VrMatrix offset)
        {
            if (handle == WindowHandle) Attached.Add((device, offset));
            else DotsAttached[handle] = (device, offset);
        }

        /// <summary>震わせた長さ（震わせた順）。乗ったとき・押したとき・長押しで長さが違う。</summary>
        public List<float> Pulses { get; } = [];

        public void Pulse(VrHand hand, float seconds, float amplitude) => Pulses.Add(seconds);

        public (float U, float V)? Intersect(ulong handle, VrMatrix aim) => null;

        public float? DisplayFrequency() => 90f;

        public void EnableInput(ulong handle, int width, int height) => InputEnabled = true;

        public IReadOnlyList<VrPointer> PollPointer(ulong handle) => [];

        public void UploadTexture(ulong handle, nint texture)
        {
        }

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

        public bool QuitRequested() => false;
    }
}
