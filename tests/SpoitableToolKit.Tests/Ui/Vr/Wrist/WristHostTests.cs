using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Ui.Vr;
using SpoitableToolKit.Host.Ui.Vr.Overlays;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Pointing;
using SpoitableToolKit.Host.Ui.Vr.Wrist;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Vr.Wrist;

/// <summary>手首の板を「いつ出すか・どこに置くか・掴んだらどう動かすか」。姿勢は偽物の OpenVR が返す。実機の手首にどう見えるかは、ここでは見られない。</summary>
public class WristHostTests
{
    private const uint Left = 1;
    private const uint Right = 2;

    /// <summary>頭。原点から 1.6m 上で、-Z を見ている。</summary>
    private static readonly VrMatrix Head = VrMatrix.Translation(0, 1.6f, 0);

    /// <summary>既定の <c>PointAt()</c> が指す画素（絵の真ん中。手首の真ん中ではない — それは <see cref="WristHost.Axis"/>）。</summary>
    private static readonly string Mid = $"{WristHost.Width / 2},{WristHost.Height / 2}";

    private sealed class Rig
    {
        public FakeOverlays Overlays { get; } = new();

        public FakeSettings Settings { get; } = new();

        public List<bool> Woken { get; } = [];

        public List<string> Sent { get; } = [];

        public List<WristLayout> Layouts { get; } = [];

        /// <summary>時計（ミリ秒）。<b>周を回しても進まない</b> — 進めるのは試験が書く。</summary>
        public long Clock { get; set; }

        public VrFrame Frame { get; set; } = new(99, 1);

        public bool Clickable { get; set; }

        public bool WindowHeld { get; set; }

        public WristHost Wrist { get; }

        /// <summary>手を読んで、板を置いて、両手で指す係（<see cref="VrScene"/>）。周はこれで回す（VrHost と同じ）。</summary>
        public VrScene Scene { get; }

        public Rig(bool onHead = false, IVrBoardOwner? other = null)
        {
            Wrist = new WristHost(
                Overlays, Settings, use => use(Frame),
                (kind, x, y, button, _, _) => Sent.Add($"{kind} {x},{y} {button}"),
                Woken.Add, Layouts.Add, NullLogger.Instance, onHead, () => Clock, () => Clickable, () => WindowHeld);

            // 入力を預かるかは手首の Layout Mode で決める（VrLoop と同じ組み方）。
            Scene = new VrScene(
                Overlays, NullLogger.Instance, other is null ? [Wrist] : [Wrist, other], captureAll: () => Wrist.InLayout);
        }

        /// <summary>繋いだときと同じ: 両手のポインタと手首の板を作る。</summary>
        public void Start() => Scene.Start();

        public void Tick() => Scene.Tick();

        public bool LaserShown => Scene.Pointing.For(VrHand.Left).Visible || Scene.Pointing.For(VrHand.Right).Visible;

        /// <summary>板の今の姿勢（最後に付けた所）。</summary>
        public VrMatrix Board()
        {
            var (device, offset) = Overlays.Attached.Last();
            return Overlays.Pose[device]!.Value.Then(offset);
        }

        /// <summary>絵の真ん中を手首の真ん中からずらす横の距離（メートル）。製品と同じ式をわざと書き写している（手首の真ん中が軸の画素に来るという約束を、試験の側でも持つ）。</summary>
        private float Shift => Wrist.Size * (WristHost.Width / 2f - WristHost.Axis) / WristHost.BoardWidth;

        /// <summary>手首の真ん中の置き場 <paramref name="offset"/> を付けたとき、OpenVR が受け取るはずの置き場（絵の真ん中）。</summary>
        public VrMatrix Expected(VrMatrix offset) => offset.Then(VrMatrix.Translation(Shift, 0, 0));

        /// <summary>OpenVR に付けた置き場（絵の真ん中）から、手首の真ん中の置き場（覚える方）へ戻す。</summary>
        public VrMatrix Unshifted(VrMatrix attached) => attached.Then(VrMatrix.Translation(-Shift, 0, 0));

        /// <summary>今付いている置き場を、手首の真ん中のものに直したもの。</summary>
        public VrMatrix Placed() => Unshifted(Overlays.Attached.Last().Offset);

        /// <summary>反対の手（右）で板の画素 (<paramref name="px"/>, <paramref name="py"/>) の真ん中を、板の表 30 cm 手前から真っ直ぐ指す（画素の境目を指すと、浮動小数の端数でどちらに落ちるか決まらない）。</summary>
        public void PointAt(int px = WristHost.Width / 2, int py = WristHost.Height / 2)
        {
            var width = Wrist.WidthInMeters;
            var height = width * WristHost.Height / WristHost.Width;

            var x = width * ((px + 0.5f) / WristHost.Width - 0.5f);
            var y = height * (0.5f - (py + 0.5f) / WristHost.Height);

            Overlays.RightHand = Overlays.RightHand with { Aim = Board().Then(VrMatrix.Translation(x, y, 0.3f)) };
        }

        /// <summary>同じ所から、板と反対の向きを指す。</summary>
        public void PointAway() =>
            Overlays.RightHand = Overlays.RightHand with
            {
                Aim = Board().Then(VrMatrix.Translation(0, 0, 0.3f)).Then(VrMatrix.RotationY(180)),
            };

        public void Trigger(bool down) => Overlays.RightHand = Overlays.RightHand with { Click = down };

        public void Grip(bool down) => Overlays.RightHand = Overlays.RightHand with { Grab = down };

        /// <summary>指してグリップ（1 周）。<b>掴み始めるのはこの周の終わり</b>。</summary>
        public void Grab()
        {
            PointAt();
            Grip(true);
            Tick();
        }

        public void LetGo()
        {
            Grip(false);
            Tick();
        }

        /// <summary>手首（コントローラー）を板が頭の方を向く所へ置く。板の置き場は今の設定のもの。</summary>
        public void FaceTheHead(uint controller = Left)
        {
            var offset = Settings.SavedOffset(Settings.Hand) ?? WristPlacement.DefaultOffset(Settings.Hand);

            // 板が頭の前 0.4m に、頭の方を向いて来るように、コントローラーの姿勢を逆算する。
            var board = Head.Then(VrMatrix.Translation(0, -0.2f, -0.4f));
            Overlays.Pose[controller] = board.Then(offset.Inverse());
        }

        /// <summary>
        /// 板を頭の前に置いて、<paramref name="degrees"/> だけ横へ回す。<b>板の見える面と目の方向の角度</b>（度）を返す
        /// （板は頭より 0.2 m 下にあるので、回さなくても 27° ほど傾いている）。
        /// </summary>
        public double Tilt(float degrees, uint controller = Left)
        {
            var offset = Settings.SavedOffset(Settings.Hand) ?? WristPlacement.DefaultOffset(Settings.Hand);
            var board = Head.Then(VrMatrix.Translation(0, -0.2f, -0.4f)).Then(VrMatrix.RotationY(degrees));
            Overlays.Pose[controller] = board.Then(offset.Inverse());
            return Math.Acos(WristPlacement.Facing(Head, board)) * 180 / Math.PI;
        }

        /// <summary>
        /// 頭を<b>上へ <paramref name="pitchUp"/>°・左へ <paramref name="yawLeft"/>°</b> 向ける（位置は変えない）。<see cref="FaceTheHead"/> の板の真ん中が、
        /// 視野の真ん中から何度外れたか（度）を返す（板は頭より 0.2 m 下・0.4 m 前なので、真っ直ぐ前を見ていても 26.6° 外れている）。
        /// 板の面の向き（<see cref="WristPlacement.Facing"/>）は頭の位置だけで決まるので、頭を回しても変わらない。
        /// </summary>
        public double TurnHead(float pitchUp = 0, float yawLeft = 0)
        {
            var head = Head.Then(VrMatrix.RotationY(yawLeft)).Then(VrMatrix.RotationX(pitchUp));
            Overlays.Pose[0] = head;
            return Math.Acos(WristPlacement.Gaze(head, Head.Then(VrMatrix.Translation(0, -0.2f, -0.4f)))) * 180 / Math.PI;
        }

        /// <summary>手首を返して、板を頭から背ける。</summary>
        public void TurnAway(uint controller = Left)
        {
            var offset = Settings.SavedOffset(Settings.Hand) ?? WristPlacement.DefaultOffset(Settings.Hand);
            var board = Head.Then(VrMatrix.Translation(0, -0.2f, -0.4f)).Then(VrMatrix.RotationY(180));
            Overlays.Pose[controller] = board.Then(offset.Inverse());
        }

        public void Ticks(int count)
        {
            for (var i = 0; i < count; i++) Tick();
        }

        /// <summary>フェードを済ませる（50 ms ずつ 4 周 = 200 ms。1 周で進めるのは 100 ms までなので、1 回で飛ばすと済まない）。</summary>
        public void Settle()
        {
            for (var i = 0; i < 4; i++)
            {
                Clock += 50;
                Tick();
            }
        }

        public void StartFacing()
        {
            Start();
            FaceTheHead();
            Ticks(WristHost.ShowAfterTicks);
        }

        /// <summary>🔓 を押す（Layout Mode に入る）。<b>板が出ていないと効かない</b>。</summary>
        public void Unlock()
        {
            Wrist.RequestLayout(true);
            Tick();
        }
    }

    /// <summary>手首のページはモジュールの push を受ける（タイルに描くページへ中継する）。e2e の手首はシェルの中の iframe で配る先に入っていないので、ここでしか見られない。ログの生の行は受けない。</summary>
    [Fact]
    public void TheWristHearsModulePushesForItsTilePages()
    {
        Assert.Contains("modulePush", WristHost.Channels);
        Assert.Contains("configChanged", WristHost.Channels);
        Assert.DoesNotContain("logLines", WristHost.Channels);
    }

    /// <summary>SteamVR のレーザーには渡さない（入力の口を開けない）。開けて旗を立てると、手首が見えているだけで VRChat の操作が全部止まる。代わりに自前の点を作る（線は出さない）。</summary>
    [Fact]
    public void TheBoardIsNotHandedToTheSteamVrLaser()
    {
        var rig = new Rig();

        rig.Start();

        Assert.True(rig.Wrist.Running);
        Assert.Equal([WristHost.Key], rig.Overlays.Created);
        Assert.False(rig.Overlays.InputEnabled);

        // 自前のポインタの点は手ごとに 1 つ（VrPointing）。
        Assert.Equal([VrSprite.Dot, VrSprite.Dot], rig.Overlays.Sprites);
    }

    /// <summary>
    /// <b>コントローラーが無ければ出さない</b>（仮想ヘッドセットではいつもこれ）。
    /// 付け先が無いのに出すと、板が原点に浮く。
    /// </summary>
    [Fact]
    public void NoControllerNoBoard()
    {
        var rig = new Rig();
        rig.Overlays.Controllers.Clear();

        rig.Start();
        rig.Ticks(10);

        Assert.False(rig.Wrist.Shown);
        Assert.Empty(rig.Overlays.Attached);
    }

    [Fact]
    public void ItIsAttachedToTheChosenHandWithTheDefaultOffset()
    {
        var rig = new Rig();

        rig.Start();
        rig.Tick();

        Assert.Equal((Left, rig.Expected(WristPlacement.DefaultOffset(VrHand.Left))), rig.Overlays.Attached.Single());
    }

    /// <summary>手を替えたら、<b>その場で</b>もう一方のコントローラーへ付け直す（再起動は要らない）。</summary>
    [Fact]
    public void ChangingHandsMovesItToTheOtherController()
    {
        var rig = new Rig();
        rig.Start();
        rig.Tick();

        rig.Settings.Hand = VrHand.Right;
        rig.Tick();

        Assert.Equal((Right, rig.Expected(WristPlacement.DefaultOffset(VrHand.Right))), rig.Overlays.Attached.Last());
    }

    /// <summary>掴んで動かした置き場があれば、<b>既定ではなくそちら</b>に付ける。</summary>
    [Fact]
    public void ASavedPlaceWinsOverTheDefault()
    {
        var rig = new Rig();
        var saved = VrMatrix.Translation(0.1f, 0.1f, 0.1f);
        rig.Settings.Saved[VrHand.Left] = saved;

        rig.Start();
        rig.Tick();

        Assert.Equal((Left, rig.Expected(saved)), rig.Overlays.Attached.Single());
    }

    /// <summary>同じ所に付け直さない。<b>毎周付け直すと、SteamVR に要らない仕事をさせる。</b></summary>
    [Fact]
    public void ItIsNotAttachedAgainWhenNothingMoved()
    {
        var rig = new Rig();

        rig.Start();
        rig.Ticks(5);

        Assert.Single(rig.Overlays.Attached);
    }

    /// <summary>
    /// <b>手首の板の真ん中（画素 <see cref="WristHost.Axis"/>）が、手首の置き場にぴったり乗る。</b>
    /// 絵は右に通知の板の分だけ広いので、絵の真ん中を手首に付けると手首の板が左へずれる。
    /// ずらしが大きさに比例することも確かめる（大きさごと）。
    /// </summary>
    [Theory]
    [InlineData(0.10f)]
    [InlineData(0.18f)]
    [InlineData(0.40f)]
    public void TheWristBoardCentreSitsOnTheWristPlace(float size)
    {
        var rig = new Rig();
        rig.Settings.Width = size;
        rig.StartFacing();

        // 絵の真ん中の姿勢から、画素 Axis の所まで横へ歩く（画素は左端から、絵の横幅が WidthInMeters）。
        var axis = rig.Board().Then(VrMatrix.Translation(rig.Wrist.WidthInMeters * ((float)WristHost.Axis / WristHost.Width - 0.5f), 0, 0));
        var wrist = rig.Overlays.Pose[Left]!.Value.Then(WristPlacement.DefaultOffset(VrHand.Left));

        Assert.Equal(wrist.Position.X, axis.Position.X, 1e-5f);
        Assert.Equal(wrist.Position.Y, axis.Position.Y, 1e-5f);
        Assert.Equal(wrist.Position.Z, axis.Position.Z, 1e-5f);
    }

    /// <summary>
    /// <b>大きさを変えたら、ずらしも変わるので付け直す。</b> 手首の置き場は同じでも、絵の真ん中は大きさに比例して動く。
    /// 付け直さないと、大きくした分だけ手首の板が手首からずれる。覚える置き場（手首の真ん中）は変わらない。
    /// </summary>
    [Fact]
    public void ResizingAttachesAgainWithTheNewShift()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.Unlock();
        var before = rig.Overlays.Attached.Last().Offset;

        rig.Wrist.RequestResize(1);
        rig.Tick();

        var place = WristPlacement.DefaultOffset(VrHand.Left);
        Assert.Equal(2, rig.Overlays.Attached.Count);
        Assert.NotEqual(before, rig.Overlays.Attached.Last().Offset);
        Assert.Equal((Left, rig.Expected(place)), rig.Overlays.Attached.Last());
        AssertSamePlace(place, rig.Placed());
    }

    /// <summary>
    /// <b>向けてすぐには出さない。</b> 腕を振った拍子に一瞬だけ出るのを防ぐ。
    /// </summary>
    [Fact]
    public void ItShowsOnlyAfterBeingFacedForAWhile()
    {
        var rig = new Rig();
        rig.Start();
        rig.FaceTheHead();

        rig.Ticks(WristHost.ShowAfterTicks - 1);
        Assert.False(rig.Wrist.Shown);

        rig.Tick();
        Assert.True(rig.Wrist.Shown);
        Assert.Equal([true], rig.Overlays.ShownChanges);
    }

    /// <summary>
    /// <b>背けたらすぐ隠す。</b> 出ている間はレーザーも出るので、見ていないのに出ている時間を作らない。
    /// </summary>
    [Fact]
    public void ItHidesAsSoonAsItIsTurnedAway()
    {
        var rig = new Rig();
        rig.StartFacing();

        rig.TurnAway();
        rig.Tick();

        Assert.False(rig.Wrist.Shown);
        Assert.Equal([true, false], rig.Overlays.ShownChanges);
    }

    // 出す条件は、板の真ん中が視野の真ん中の近くにあるか（頭の向き）。面の向きは裏・真横を弾くだけ。

    /// <summary>手首をこちらへ向けていても、前を見ていれば出さない。板から 50° 横を見ている。</summary>
    [Fact]
    public void AWristTurnedToYouStaysHiddenWhileYouLookElsewhere()
    {
        var rig = new Rig();
        rig.Start();
        rig.FaceTheHead();

        Assert.True(rig.TurnHead(yawLeft: 50) > 50);
        rig.Ticks(WristHost.ShowAfterTicks * 3);

        // **一度も出さない**（出してすぐ隠すのを繰り返す点滅も駄目。最後の周だけ見ると、隠れた周に当たって見逃す）。
        Assert.Empty(rig.Overlays.ShownChanges);
    }

    /// <summary><b>出ている板も、目を離したら隠す</b>（手首はこちらを向いたまま）。</summary>
    [Fact]
    public void LookingAwayHidesTheBoardEvenWithTheWristStillTurnedToYou()
    {
        var rig = new Rig();
        rig.StartFacing();
        Assert.True(rig.Wrist.Shown);

        Assert.True(rig.TurnHead(yawLeft: 50) > 50);
        rig.Tick();

        Assert.False(rig.Wrist.Shown);
    }

    /// <summary>
    /// <b>見ていても、板が真横を向いていれば出さない</b>（面の向きは 60° より外れたら出さない）。手首を 70° 回した。
    /// </summary>
    [Fact]
    public void ABoardTurnedEdgeOnStaysHiddenEvenWhenLookedAt()
    {
        var rig = new Rig();
        rig.Start();

        var facing = rig.Tilt(70);
        Assert.InRange(facing, 61, 80);   // 面の向きの 60° の外（試験の前提）。視線は 26.6° のまま

        rig.Ticks(WristHost.ShowAfterTicks * 3);

        Assert.Empty(rig.Overlays.ShownChanges);
    }

    /// <summary>
    /// <b>出ている板も、面が 75° より外れたら隠す</b>（見ていても）。60°〜75° の間なら出したまま（境目で点滅しない）。
    /// </summary>
    [Fact]
    public void AShownBoardTurnedPastSeventyFiveDegreesHides()
    {
        var rig = new Rig();
        rig.StartFacing();

        Assert.InRange(rig.Tilt(62), 61, 74.5);   // 60° と 75° の間（試験の前提）
        rig.Tick();
        Assert.True(rig.Wrist.Shown);

        Assert.InRange(rig.Tilt(80), 75.5, 89);
        rig.Tick();
        Assert.False(rig.Wrist.Shown);
    }

    // 出す・隠す角度の設定。周ごとに読む（VR の中で設定を変えて、その場で確かめながら詰めるので）。

    /// <summary><b>出す角度は設定から読み、変えたら次の周から効く。</b>（板の真ん中が視野の真ん中から 34.6° 外れた所）</summary>
    [Fact]
    public void TheShowAngleComesFromTheSettingsAndTakesEffectAtOnce()
    {
        var rig = new Rig();
        rig.Start();
        rig.FaceTheHead();

        var angle = rig.TurnHead(pitchUp: 8);
        Assert.InRange(angle, 31, 39);   // 既定の 30° の外・40° の内（試験の前提）

        rig.Ticks(WristHost.ShowAfterTicks);
        Assert.False(rig.Wrist.Shown);

        rig.Settings.ShowWithinDegrees = 40;
        rig.Ticks(WristHost.ShowAfterTicks);
        Assert.True(rig.Wrist.Shown);
    }

    /// <summary><b>隠す角度も設定から読み、変えたら次の周から効く。</b>（板の真ん中が視野の真ん中から 42.6° 外れた所）</summary>
    [Fact]
    public void TheHideAngleComesFromTheSettingsAndTakesEffectAtOnce()
    {
        var rig = new Rig();
        rig.StartFacing();

        var angle = rig.TurnHead(pitchUp: 16);
        Assert.InRange(angle, 41, 44.5);   // 既定の 45° の内・40° の外（試験の前提）

        rig.Tick();
        Assert.True(rig.Wrist.Shown);

        rig.Settings.HideBeyondDegrees = 40;
        rig.Tick();
        Assert.False(rig.Wrist.Shown);
    }

    /// <summary>
    /// 一瞬背けただけなら、次に向けたときに<b>また待つ</b>（待った回数を持ち越さない）。
    /// </summary>
    [Fact]
    public void AGlanceAwayStartsTheWaitAgain()
    {
        var rig = new Rig();
        rig.Start();
        rig.FaceTheHead();
        rig.Ticks(WristHost.ShowAfterTicks - 1);

        rig.TurnAway();
        rig.Tick();
        rig.FaceTheHead();
        rig.Ticks(WristHost.ShowAfterTicks - 1);

        Assert.False(rig.Wrist.Shown);
    }

    /// <summary>
    /// <b>見ているかは手首の真ん中で見る</b>（OpenVR に付ける絵の真ん中ではない）。絵は右に通知の板の分だけ広いので、
    /// 絵の真ん中で見ると、手首の板を見ているのに頭の向きしだいで出なくなる。
    /// 大きくして（ずれが大きい）、手首の真ん中は視野の真ん中から 30° の内、絵の真ん中は外になる所に置く（頭を 2° 上げる）。
    /// </summary>
    [Fact]
    public void LookingIsJudgedAtTheWristCentreNotThePictureCentre()
    {
        var rig = new Rig();
        rig.Settings.Width = 0.40f;
        rig.Start();
        rig.FaceTheHead();
        rig.TurnHead(pitchUp: 2);

        var head = rig.Overlays.Pose[0]!.Value;
        var offset = WristPlacement.DefaultOffset(VrHand.Left);
        var board = Head.Then(VrMatrix.Translation(0, -0.2f, -0.4f));
        var picture = rig.Overlays.Pose[Left]!.Value.Then(rig.Expected(offset));
        Assert.True(WristPlacement.Gaze(head, board) >= WristAngles.Default.ShowWhenCosAbove, "手首の真ん中が視野の 30° の外（試験の前提が崩れている）");
        Assert.True(WristPlacement.Gaze(head, picture) < WristAngles.Default.ShowWhenCosAbove, "絵の真ん中も 30° の内（試験の前提が崩れている）");

        rig.Ticks(WristHost.ShowAfterTicks);

        Assert.True(rig.Wrist.Shown);
    }

    // 出入りのフェード。時計は試験が進める（周を回しても進まない）。

    /// <summary>出すときは<b>透明から始めて</b>、<see cref="WristHost.FadeMs"/> で不透明になる。透明度は出す前に渡す（1 枚だけ濃く見えない）。</summary>
    [Fact]
    public void ItFadesInWhenItComesUp()
    {
        var rig = new Rig();
        rig.StartFacing();

        Assert.True(rig.Wrist.Shown);
        Assert.Equal([true], rig.Overlays.ShownChanges);
        Assert.Equal(0f, rig.Overlays.Alphas.First());

        rig.Clock += WristHost.FadeMs / 2;
        rig.Tick();
        Assert.Equal(0.5f, rig.Wrist.Alpha, 0.02f);

        rig.Clock += WristHost.FadeMs;
        rig.Tick();
        Assert.Equal(1f, rig.Wrist.Alpha);
        Assert.Equal(1f, rig.Overlays.Alphas.Last());
    }

    /// <summary>
    /// 背けたら<b>その周から指せなくなり</b>、薄くなっていく。SteamVR から下ろして browser を眠らせるのは<b>消え終わってから</b>
    /// （消えていく途中に眠らせると絵が止まる）。
    /// </summary>
    [Fact]
    public void TurningAwayStopsPointingAtOnceAndFadesOut()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.Settle();

        rig.TurnAway();
        rig.PointAt();
        rig.Tick();

        Assert.False(rig.Wrist.Shown);
        Assert.False(rig.Wrist.Pointed);
        Assert.True(rig.Wrist.Visible);
        Assert.False(rig.Wrist.Board!.Live);
        Assert.Equal([true], rig.Overlays.ShownChanges);
        Assert.Equal([true], rig.Woken);

        rig.Clock += WristHost.FadeMs / 2;
        rig.Tick();
        Assert.Equal(0.5f, rig.Wrist.Alpha, 0.02f);
        Assert.Equal(0.5f, rig.Wrist.Board!.Alpha, 0.02f);
        Assert.Equal([true], rig.Woken);

        rig.Clock += WristHost.FadeMs;
        rig.Tick();
        Assert.False(rig.Wrist.Visible);
        Assert.Null(rig.Wrist.Board);
        Assert.Equal([true, false], rig.Overlays.ShownChanges);
        Assert.Equal([true, false], rig.Woken);
    }

    /// <summary>消えていく途中で向け直したら、<b>今の薄さから</b>濃くなる（透明に戻してから出し直さない。出し直しもしない）。</summary>
    [Fact]
    public void TurningBackMidFadeComesBackFromWhereItWas()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.Settle();

        rig.TurnAway();
        rig.Tick();
        rig.Clock += WristHost.FadeMs / 2;
        rig.Tick();

        rig.FaceTheHead();
        rig.Ticks(WristHost.ShowAfterTicks);

        Assert.True(rig.Wrist.Shown);
        Assert.Equal(0.5f, rig.Wrist.Alpha, 0.02f);
        Assert.Equal([true], rig.Overlays.ShownChanges);

        rig.Clock += WristHost.FadeMs / 2;
        rig.Tick();
        Assert.Equal(1f, rig.Wrist.Alpha);
    }

    /// <summary>手首を見失ったら<b>フェードせずにすぐ消す</b>（付け先が追えていないので、薄くしていく間の置き場が無い）。</summary>
    [Fact]
    public void LosingTheWristHidesAtOnce()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.Settle();

        rig.Overlays.Pose[Left] = null;
        rig.Tick();

        Assert.False(rig.Wrist.Visible);
        Assert.Equal([true, false], rig.Overlays.ShownChanges);
        Assert.Equal([true, false], rig.Woken);
    }

    /// <summary>周が止まっていた（引っかかった）あとでも<b>一気に飛ばない</b>（1 周で進めるのは 100 ms まで）。</summary>
    [Fact]
    public void AStallDoesNotSnapTheFade()
    {
        var rig = new Rig();
        rig.StartFacing();

        rig.Clock += 10_000;
        rig.Tick();

        Assert.True(rig.Wrist.Alpha < 1f, $"止まっていた分を一気に進めた（{rig.Wrist.Alpha}）");
        Assert.True(rig.Wrist.Alpha > 0f);
    }

    /// <summary>出したら画面を起こし、隠したら眠らせる（描かない・タイマーを絞る）。</summary>
    [Fact]
    public void ThePageIsWokenAndPutToSleepWithTheBoard()
    {
        var rig = new Rig();
        rig.StartFacing();

        rig.TurnAway();
        rig.Tick();

        Assert.Equal([true, false], rig.Woken);
    }

    /// <summary>頭が追えていなければ、向いているかは決められない。<b>出さない側に倒す</b>。</summary>
    [Fact]
    public void NoHeadNoBoard()
    {
        var rig = new Rig();
        rig.Start();
        rig.FaceTheHead();
        rig.Overlays.Pose.Remove(0);

        rig.Ticks(WristHost.ShowAfterTicks * 2);

        Assert.False(rig.Wrist.Shown);
    }

    /// <summary>出していない間は絵を上げない（見えないものに GPU を使わない）。</summary>
    [Fact]
    public void NothingIsUploadedWhileHidden()
    {
        var rig = new Rig();
        rig.Start();
        rig.TurnAway();

        rig.Ticks(10);
        Assert.Equal(0, rig.Wrist.Uploads);

        rig.FaceTheHead();
        rig.Ticks(WristHost.ShowAfterTicks);
        Assert.Equal(1, rig.Wrist.Uploads);
    }

    /// <summary>開発用（頭の前）は<b>向きを見ずにいつも出す</b>。コントローラーが無い仮想ヘッドセットで確かめるため。</summary>
    [Fact]
    public void OnTheHeadItIsAlwaysShown()
    {
        var rig = new Rig(onHead: true);
        rig.Overlays.Controllers.Clear();

        rig.Start();
        rig.Tick();

        Assert.True(rig.Wrist.Shown);
        Assert.Equal((0U, rig.Expected(WristPlacement.InFrontOfHead)), rig.Overlays.Attached.Single());
    }

    /// <summary>
    /// 反対の手で指すと、<b>指した所の画素</b>へ動いて、トリガーで押せる。画素は左上が原点
    /// （板の左上の角寄りを指したら、画面の左上寄り）。
    /// </summary>
    [Fact]
    public void PointingAndPullingTheTriggerClicksWhereItPoints()
    {
        var rig = new Rig();
        rig.StartFacing();

        // 左上寄り（板の中心から見て左・上）。
        rig.PointAt(80, 80);
        rig.Tick();
        rig.Trigger(true);
        rig.Tick();
        rig.Trigger(false);
        rig.Tick();

        Assert.Equal(["Move 80,80 Left", "Down 80,80 Left", "Up 80,80 Left"], rig.Sent);
    }

    /// <summary>指している間だけ自前の点が出る。外れたら引っ込めて、ページに「外れた」を伝える。</summary>
    [Fact]
    public void TheLaserShowsOnlyWhilePointing()
    {
        var rig = new Rig();
        rig.StartFacing();
        Assert.False(rig.LaserShown);

        rig.PointAt();
        rig.Tick();
        Assert.True(rig.Wrist.Pointed);
        Assert.True(rig.LaserShown);

        rig.PointAway();
        rig.Tick();
        Assert.False(rig.LaserShown);
        Assert.Equal($"Leave {Mid} Left", rig.Sent.Last());
    }

    /// <summary>
    /// <b>板が隠れている間は指さない</b>（見えていない物を押せてしまう）。
    /// 板があった所を指したままトリガーを引いても、何も届かない。
    /// </summary>
    [Fact]
    public void AHiddenBoardIsNotPointedAt()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.TurnAway();
        rig.Tick();

        // 隠れた後の置き場を指し直す。手首を返すと板も動くので、返す前の狙いのままだと、隠れているからではなく外れているから当たらない。
        rig.PointAt();
        rig.Trigger(true);
        rig.Tick();

        Assert.False(rig.Wrist.Shown);
        Assert.DoesNotContain(rig.Sent, sent => sent.StartsWith("Down"));
        Assert.False(rig.LaserShown);
    }

    /// <summary>ダッシュボードが開いている間は指さない（SteamVR のレーザーが出ていて、そちらで操作する）。</summary>
    [Fact]
    public void TheLaserStaysAwayWhileTheDashboardIsOpen()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.Overlays.DashboardIsOpen = true;

        rig.PointAt();
        rig.Tick();

        Assert.False(rig.Wrist.Pointed);
        Assert.Empty(rig.Sent);
    }

    /// <summary>ダッシュボードが開いている間は隠す（重なって邪魔になる）。閉じたら、向いていれば出し直す — 腕を振ったときと同じく <see cref="WristHost.ShowAfterTicks"/> 待ってから。</summary>
    [Fact]
    public void TheDashboardHidesTheWristUntilItCloses()
    {
        var rig = new Rig();
        rig.StartFacing();
        Assert.True(rig.Wrist.Shown);

        rig.Overlays.DashboardIsOpen = true;
        rig.Tick();

        Assert.False(rig.Wrist.Shown);
        Assert.Null(rig.Wrist.Board);

        rig.Overlays.DashboardIsOpen = false;
        rig.Ticks(WristHost.ShowAfterTicks - 1);
        Assert.False(rig.Wrist.Shown);

        rig.Tick();
        Assert.True(rig.Wrist.Shown);
    }

    /// <summary>動かしている最中にダッシュボードが開いたら、<b>動かしたところまでで覚える</b>（手首を見失ったときと同じ）。</summary>
    [Fact]
    public void TheDashboardOpeningWhileMovingKeepsTheMove()
    {
        var rig = new Rig();
        rig.Overlays.Pose[Right] = VrMatrix.Translation(0.3f, 1.2f, -0.3f);
        rig.StartFacing();
        rig.Unlock();

        rig.Grab();
        rig.Overlays.Pose[Right] = VrMatrix.Translation(0.4f, 1.2f, -0.3f);
        rig.Tick();
        var moved = rig.Placed();

        rig.Overlays.DashboardIsOpen = true;
        rig.Tick();

        Assert.False(rig.Wrist.Dragging);
        AssertSamePlace(moved, rig.Settings.Saved[VrHand.Left]);
    }

    /// <summary>
    /// <b>板の外で引いたトリガーは押したことにしない</b>（VRChat で何かを持ったまま腕を動かしただけ）。
    /// </summary>
    [Fact]
    public void ATriggerPulledElsewhereDoesNotClickOnArrival()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.PointAway();
        rig.Trigger(true);
        rig.Tick();

        rig.PointAt();
        rig.Tick();

        Assert.Equal([$"Move {Mid} Left"], rig.Sent);
    }

    /// <summary>
    /// 押したまま板から外れたら<b>外れた所で離す</b>（CEF にボタンが押されたまま残らない）。
    /// </summary>
    [Fact]
    public void LeavingWhilePressedLetsGoThere()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.PointAt();
        rig.Tick();
        rig.Trigger(true);
        rig.Tick();

        rig.PointAway();
        rig.Tick();

        Assert.Equal([$"Move {Mid} Left", $"Down {Mid} Left", $"Up {Mid} Left", $"Leave {Mid} Left"], rig.Sent);
    }

    /// <summary>
    /// 手首の板は<b>巻かない</b>（巻く中身が無い）。トリガーを引いたら<b>すぐ押す</b>（巻く板のように離すまで待たない）、
    /// 引いたまま動かしても巻かない。
    /// </summary>
    [Fact]
    public void TheWristPressesAtOnceAndDoesNotScroll()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.PointAt();
        rig.Tick();

        rig.Trigger(true);
        rig.Tick();
        Assert.StartsWith("Down", rig.Sent.Last());

        rig.PointAt(py: 40);
        rig.Ticks(3);

        Assert.DoesNotContain(rig.Sent, sent => sent.StartsWith("Scroll") || sent.StartsWith("Pan"));
    }

    /// <summary>押したとき・押せる物に乗ったときに、<b>指している手</b>を軽く震わせる。</summary>
    [Fact]
    public void PressingAndHoveringAButtonPulseThePointingHand()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.PointAt();
        rig.Tick();
        Assert.Empty(rig.Overlays.Pulses);

        rig.Clickable = true;
        rig.Tick();
        rig.Tick();
        Assert.Equal([(VrHand.Right, VrLaser.HoverPulseSeconds, VrLaser.HoverPulseAmplitude)], rig.Overlays.Pulses);

        rig.Trigger(true);
        rig.Tick();
        Assert.Equal((VrHand.Right, VrLaser.PressPulseSeconds, VrLaser.PressPulseAmplitude), rig.Overlays.Pulses.Last());
        Assert.Equal(2, rig.Overlays.Pulses.Count);
    }

    /// <summary>手首を右に付けたら、<b>指すのは左手</b>。</summary>
    [Fact]
    public void WithTheBoardOnTheRightTheLeftHandPoints()
    {
        var rig = new Rig();
        rig.Settings.Hand = VrHand.Right;
        rig.Start();
        rig.FaceTheHead(Right);
        rig.Ticks(WristHost.ShowAfterTicks);

        rig.PointAt();
        rig.Tick();
        Assert.False(rig.Wrist.Pointed, "右手が指したことになっている");

        rig.Overlays.LeftHand = rig.Overlays.RightHand;
        rig.Tick();
        Assert.True(rig.Wrist.Pointed);
    }

    /// <summary>
    /// <b>手は周に 1 回だけ読む</b>（中で UpdateActionState を呼ぶ）。隠れている間も読む —
    /// 読まない周があると、押し始めを前の周との違いで決められない。
    /// </summary>
    [Fact]
    public void TheHandsAreReadOncePerTickEvenWhileHidden()
    {
        var rig = new Rig();
        rig.Start();
        rig.TurnAway();

        rig.Ticks(5);

        Assert.Equal(5, rig.Overlays.HandReads);
    }

    /// <summary>
    /// 反対の手が<b>ほかの板</b>（空間の窓など）を指してグリップしても、手首は掴まない（Layout Mode の間でも）。
    /// 掴むのは手首の板を指していたときだけ。
    /// </summary>
    [Fact]
    public void GrippingWhilePointingAtAnotherBoardDoesNotGrabTheWrist()
    {
        var other = new OtherBoard();
        var rig = new Rig(other: other);
        rig.StartFacing();
        rig.Unlock();

        // 手首の板から 50 cm 右で、30 cm 先のほかの板を指す。
        var aim = rig.Board().Then(VrMatrix.Translation(0.5f, 0, 0.3f));
        other.Pose = aim.Then(VrMatrix.Translation(0, 0, -0.3f));
        rig.Overlays.RightHand = rig.Overlays.RightHand with { Aim = aim, Grab = true };
        rig.Tick();
        rig.Tick();

        Assert.False(rig.Wrist.Pointed);
        Assert.False(rig.Wrist.Dragging);
    }

    /// <summary>空間に置いたほかの板（指されるだけ）。</summary>
    private sealed class OtherBoard : IVrBoardOwner
    {
        public VrMatrix Pose { get; set; } = VrMatrix.Identity;

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void Place(VrHands hands, bool dashboardOpen, List<VrLaserTarget> targets) =>
            targets.Add(new VrLaserTarget(
                99, null, VrMatrix.Identity, Pose, 0.2f, 0.1f, 640, 320, (_, _, _, _, _, _) => { }, () => false, false, "other board"));

        public void Take(VrHands hands, VrPointed pointed)
        {
        }
    }

    /// <summary>
    /// 出ている間は<b>板の今</b>（手・機器・その姿勢・機器から見た置き場・縦の長さ）を教える（空間の窓を手首の板の上に付けるため）。
    /// 隠れたら教えない（窓も一緒に隠れる）。
    /// </summary>
    [Fact]
    public void TheBoardIsKnownOnlyWhileShown()
    {
        var rig = new Rig();
        rig.StartFacing();

        var board = rig.Wrist.Board;
        Assert.NotNull(board);
        Assert.Equal(VrHand.Left, board.Hand);
        Assert.Equal(Left, board.Device);
        // 教えるのは手首の真ん中の置き場（絵の真ん中ではない）。窓は手首の板の上に付くので、ずらしを知らない。
        Assert.Equal(WristPlacement.DefaultOffset(VrHand.Left), board.Offset);
        Assert.Equal(rig.Overlays.Pose[Left]!.Value.Then(board.Offset), board.Pose);
        Assert.Equal(rig.Wrist.Size * WristHost.Height / WristHost.BoardWidth, board.Height, 1e-6f);

        rig.TurnAway();
        rig.Tick();

        Assert.Null(rig.Wrist.Board);
    }

    /// <summary>
    /// 掴んだら<b>反対の手に付いてくる</b>。手首から見た置き場を毎周計算し直すので、
    /// 反対の手を動かした分だけ板が動く。
    /// </summary>
    [Fact]
    public void ADraggedBoardFollowsTheOtherHand()
    {
        var rig = new Rig();
        rig.Overlays.Pose[Right] = VrMatrix.Translation(0.3f, 1.2f, -0.3f);
        rig.StartFacing();
        rig.Unlock();

        rig.Grab();
        Assert.True(rig.Wrist.Dragging);

        var before = rig.Overlays.Pose[Left]!.Value.Then(rig.Overlays.Attached.Last().Offset).Position;

        rig.Overlays.Pose[Right] = VrMatrix.Translation(0.4f, 1.2f, -0.3f);
        rig.Tick();

        var after = rig.Overlays.Pose[Left]!.Value.Then(rig.Overlays.Attached.Last().Offset).Position;
        Assert.Equal(before.X + 0.1f, after.X, 1e-4f);
        Assert.Equal(before.Y, after.Y, 1e-4f);
        Assert.Equal(before.Z, after.Z, 1e-4f);
    }

    /// <summary>
    /// <b>グリップを離した所を覚える</b>（どこで離しても効く — 板は手に付いて動いている）。
    /// 次の起動でも同じ所に出る。
    /// </summary>
    [Fact]
    public void ReleasingSavesWhereItWasLeft()
    {
        var rig = new Rig();
        rig.Overlays.Pose[Right] = VrMatrix.Translation(0.3f, 1.2f, -0.3f);
        rig.StartFacing();
        rig.Unlock();

        rig.Grab();
        rig.Overlays.Pose[Right] = VrMatrix.Translation(0.4f, 1.2f, -0.3f);
        rig.Tick();
        var moved = rig.Placed();

        rig.LetGo();

        Assert.False(rig.Wrist.Dragging);
        AssertSamePlace(moved, rig.Settings.Saved[VrHand.Left]);
    }

    /// <summary>
    /// <b>隠れている間に押したグリップでは掴まない</b>。握ったまま手首を向けても、掴み始めない
    /// （押し始めを見ていない。VRChat で何かを持ったまま手首を見ただけかもしれない）。
    /// </summary>
    [Fact]
    public void AGripPressedWhileHiddenDoesNotGrab()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.Unlock();
        rig.TurnAway();
        rig.Tick();

        // 隠れた板を指して握る（返す前の狙いのままだと、外れているから掴まないだけになる）。
        rig.PointAt();
        rig.Grip(true);
        rig.Ticks(2);
        rig.FaceTheHead();
        rig.Ticks(WristHost.ShowAfterTicks);
        rig.PointAt();
        rig.Ticks(2);

        Assert.True(rig.Wrist.InLayout, "Layout Mode から落ちている（試験の前提が崩れている）");
        Assert.True(rig.Wrist.Pointed, "板を指せていない（試験の前提が崩れている）");
        Assert.False(rig.Wrist.Dragging);
    }

    /// <summary>
    /// <b>動かしている間は指さない。</b> 板は掴んだ手に付いて動くので点は板の同じ所に居続け、
    /// トリガーを引くとその下のボタンが押されてしまう（動かしている最中に「完了」や ↺ が押される）。
    /// </summary>
    [Fact]
    public void NothingIsPointedAtWhileTheBoardIsMoved()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.Unlock();
        rig.Grab();
        rig.Sent.Clear();

        rig.Trigger(true);
        rig.Tick();

        Assert.True(rig.Wrist.Dragging);
        Assert.False(rig.Wrist.Pointed);
        Assert.DoesNotContain(rig.Sent, sent => sent.StartsWith("Down"));
    }

    [Fact]
    public void AGripWithoutPointingDoesNotGrab()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.Unlock();

        rig.PointAway();
        rig.Grip(true);
        rig.Tick();

        Assert.False(rig.Wrist.Dragging);
    }

    /// <summary>掴んでいる間は、向きに関係なく出しておく（動かしている最中に消えると離せない）。背けるのは掴んでいる手で、板は掴んだ手に付いてくるので、手首（左）を返しても板の向きは変わらない。</summary>
    [Fact]
    public void ItStaysShownWhileDragged()
    {
        var rig = new Rig();
        rig.Overlays.Pose[Right] = VrMatrix.Translation(0.3f, 1.2f, -0.3f);
        rig.StartFacing();
        rig.Unlock();

        rig.Grab();

        // 掴んでいる手を返す。板もいっしょに返って、頭から背ける。
        rig.Overlays.Pose[Right] = VrMatrix.Translation(0.3f, 1.2f, -0.3f).Then(VrMatrix.RotationY(180));
        rig.Tick();

        var board = rig.Overlays.Pose[Left]!.Value.Then(rig.Placed());
        Assert.True(WristPlacement.Facing(Head, board) < 0, "板が頭から背いていない（試験の前提が崩れている）");
        Assert.True(rig.Wrist.Shown);
    }

    /// <summary>握っている間はいつまでも掴んだまま（諦める時計は無い）。離すのはグリップの状態を毎周読んで決めるので、離した報せを取りこぼすことが無い。</summary>
    [Fact]
    public void HoldingTheGripKeepsItMovingForAsLongAsItIsHeld()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.Unlock();

        rig.Grab();
        rig.Ticks(5000);

        Assert.True(rig.Wrist.Dragging);
        Assert.Empty(rig.Settings.Saved);
    }

    /// <summary>
    /// グリップが読めなくなったら（割り当てが外れた・コントローラーの電源が落ちた）、<b>動かしたところまでで</b>離す。
    /// </summary>
    [Fact]
    public void LosingTheGripReadingReleasesWhereItWas()
    {
        var rig = new Rig();
        rig.Overlays.Pose[Right] = VrMatrix.Translation(0.3f, 1.2f, -0.3f);
        rig.StartFacing();
        rig.Unlock();

        rig.Grab();
        rig.Overlays.Pose[Right] = VrMatrix.Translation(0.4f, 1.2f, -0.3f);
        rig.Tick();
        var moved = rig.Placed();

        rig.Overlays.RightHand = default;
        rig.Tick();

        Assert.False(rig.Wrist.Dragging);
        AssertSamePlace(moved, rig.Settings.Saved[VrHand.Left]);
    }

    /// <summary>反対の手を見失ったら、<b>動かしたところまでで</b>離す。</summary>
    [Fact]
    public void LosingTheOtherHandReleasesWhereItWas()
    {
        var rig = new Rig();
        rig.Overlays.Pose[Right] = VrMatrix.Translation(0.3f, 1.2f, -0.3f);
        rig.StartFacing();
        rig.Unlock();

        rig.Grab();
        rig.Overlays.Pose[Right] = VrMatrix.Translation(0.4f, 1.2f, -0.3f);
        rig.Tick();
        var moved = rig.Placed();

        rig.Overlays.Pose.Remove(Right);
        rig.Tick();

        Assert.False(rig.Wrist.Dragging);
        AssertSamePlace(moved, rig.Settings.Saved[VrHand.Left]);
    }

    /// <summary>手首の側を見失っても、動かしたところまでで覚える（掴んでいる手が手首に近づくので、隠れて追跡が切れやすい）。覚えずに掴みを捨てると、追跡が戻ったとき掴む前の所へ跳ぶ。</summary>
    [Fact]
    public void LosingTheWristWhileMovingKeepsTheMove()
    {
        var rig = new Rig();
        rig.Overlays.Pose[Right] = VrMatrix.Translation(0.3f, 1.2f, -0.3f);
        rig.StartFacing();
        rig.Unlock();

        rig.Grab();
        rig.Overlays.Pose[Right] = VrMatrix.Translation(0.4f, 1.2f, -0.3f);
        rig.Tick();
        var moved = rig.Placed();

        var wrist = rig.Overlays.Pose[Left];
        rig.Overlays.Pose.Remove(Left);
        rig.Tick();

        Assert.False(rig.Wrist.Dragging);
        AssertSamePlace(moved, rig.Settings.Saved[VrHand.Left]);

        // 戻ってきたら、動かした所に付く。
        rig.Overlays.Pose[Left] = wrist;
        rig.Tick();
        AssertSamePlace(moved, rig.Placed());
    }

    /// <summary>動かしている最中に手を替えたら、<b>動かしたところまでを前の手の置き場として</b>覚えてやめる。</summary>
    [Fact]
    public void ChangingHandsWhileMovingKeepsTheMoveForTheOldHand()
    {
        var rig = new Rig();
        rig.Overlays.Pose[Right] = VrMatrix.Translation(0.3f, 1.2f, -0.3f);
        rig.StartFacing();
        rig.Unlock();

        rig.Grab();
        rig.Overlays.Pose[Right] = VrMatrix.Translation(0.4f, 1.2f, -0.3f);
        rig.Tick();
        var moved = rig.Placed();

        rig.Settings.Hand = VrHand.Right;
        rig.Tick();

        Assert.False(rig.Wrist.Dragging);
        AssertSamePlace(moved, rig.Settings.Saved[VrHand.Left]);
        Assert.False(rig.Settings.Saved.ContainsKey(VrHand.Right));
    }

    /// <summary>↺ は<b>置き場と大きさの両方</b>を既定へ戻す（板にボタンを 2 つ並べる余裕が無い）。</summary>
    [Fact]
    public void ResettingForgetsTheSavedPlaceAndWidth()
    {
        var rig = new Rig();
        rig.Settings.Saved[VrHand.Left] = VrMatrix.Translation(0.1f, 0.1f, 0.1f);
        rig.Settings.Width = 0.3f;
        rig.StartFacing();
        rig.Unlock();

        rig.Wrist.RequestReset();
        rig.Tick();

        Assert.False(rig.Settings.Saved.ContainsKey(VrHand.Left));
        Assert.Null(rig.Settings.Width);
        Assert.Equal((Left, rig.Expected(WristPlacement.DefaultOffset(VrHand.Left))), rig.Overlays.Attached.Last());
        Assert.Equal(WristHost.DefaultWidth * WristHost.Width / WristHost.BoardWidth, rig.Overlays.Widths.Last(), 1e-6f);
    }

    /// <summary>
    /// <b>固定の間は掴めない</b>（「固定ボタン」の役目 — 指してグリップを押しても動かない。
    /// そのグリップは VRChat に素通しで届いているもの）。握ったまま Layout Mode に入っても<b>掴み始めない</b>。
    /// </summary>
    [Fact]
    public void AGripWhileLockedDoesNotGrab()
    {
        var rig = new Rig();
        rig.StartFacing();

        rig.Grab();
        Assert.False(rig.Wrist.Dragging);

        rig.Unlock();
        rig.Tick();
        Assert.False(rig.Wrist.Dragging);
    }

    [Fact]
    public void AResetWhileLockedIsDropped()
    {
        var rig = new Rig();
        var place = VrMatrix.Translation(0.1f, 0.1f, 0.1f);
        rig.Settings.Saved[VrHand.Left] = place;
        rig.Settings.Width = 0.3f;
        rig.StartFacing();

        rig.Wrist.RequestReset();
        rig.Tick();
        rig.Unlock();
        rig.Tick();

        Assert.Equal(place, rig.Settings.Saved[VrHand.Left]);
        Assert.Equal(0.3f, rig.Settings.Width);
    }

    [Fact]
    public void AResizeWhileLockedIsDropped()
    {
        var rig = new Rig();
        rig.StartFacing();

        rig.Wrist.RequestResize(1);
        rig.Tick();
        rig.Unlock();
        rig.Tick();

        Assert.Null(rig.Settings.Width);
        Assert.Empty(rig.Overlays.Widths);
    }

    /// <summary><b>🔓 は板が出ている間だけ受ける</b>（出ていないのに来たら古い頼み）。</summary>
    [Fact]
    public void UnlockingIsTakenOnlyWhileShown()
    {
        var rig = new Rig();
        rig.Start();
        rig.TurnAway();
        rig.Tick();

        rig.Wrist.RequestLayout(true);
        rig.Ticks(2);
        rig.FaceTheHead();
        rig.Ticks(WristHost.ShowAfterTicks);

        Assert.True(rig.Wrist.Shown);
        Assert.False(rig.Wrist.InLayout);
        Assert.Empty(rig.Layouts);
    }

    /// <summary><b>ページはこちらが送った状態を写す</b>（自分では切り替えない）。入ったとき・出たときに 1 回ずつ。</summary>
    [Fact]
    public void ThePageIsToldWhenTheLayoutChanges()
    {
        var rig = new Rig();
        rig.StartFacing();

        rig.Unlock();
        Assert.True(rig.Wrist.InLayout);

        rig.Wrist.RequestLayout(false);
        rig.Tick();

        Assert.False(rig.Wrist.InLayout);
        Assert.Equal(
            [new WristLayout(true, WristHost.DefaultWidth), new WristLayout(false, WristHost.DefaultWidth)],
            rig.Layouts);
    }

    /// <summary>
    /// 掴んでいる最中に「完了」が来たら、<b>動かしたところで離して覚える</b>
    /// （掴んだまま固定に戻ると、板が反対の手に付いたまま動き続ける）。
    /// </summary>
    [Fact]
    public void DoneWhileDraggingReleasesWhereItWas()
    {
        var rig = new Rig();
        rig.Overlays.Pose[Right] = VrMatrix.Translation(0.3f, 1.2f, -0.3f);
        rig.StartFacing();
        rig.Unlock();

        rig.Grab();
        rig.Overlays.Pose[Right] = VrMatrix.Translation(0.4f, 1.2f, -0.3f);
        rig.Tick();
        var moved = rig.Placed();

        rig.Wrist.RequestLayout(false);
        rig.Tick();

        Assert.False(rig.Wrist.Dragging);
        Assert.False(rig.Wrist.InLayout);
        AssertSamePlace(moved, rig.Settings.Saved[VrHand.Left]);
    }

    /// <summary>
    /// <b>Layout Mode の間も、背けたら隠す。</b> 普段と同じく、見ていない間は視界を塞がない
    /// （「完了」を押し忘れてゲームに戻ると、ずっと出ていることになる）。
    /// </summary>
    [Fact]
    public void ItStillHidesWhenTurnedAwayInLayout()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.Unlock();

        rig.TurnAway();
        rig.Tick();

        Assert.False(rig.Wrist.Shown);
        Assert.True(rig.Wrist.InLayout);
    }

    /// <summary>
    /// <b>触らない時間が続いたら固定に戻る</b>（既定 10 秒）。押し忘れたまま次に手首を見たとき、取っ手が出ていると間違って掴める。
    /// <b>手首が隠れている周も数える</b>（腕を下ろしたまま忘れるのがいちばん多い）。
    /// </summary>
    [Fact]
    public void AForgottenLayoutLocksAfterAWhileUnwatched()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.Unlock();
        rig.TurnAway();
        rig.Tick();

        rig.Clock += 9_999;
        rig.Tick();
        Assert.True(rig.Wrist.InLayout);

        rig.Clock += 1;
        rig.Tick();
        Assert.False(rig.Wrist.InLayout);
        Assert.False(rig.Layouts.Last().On);
    }

    [Fact]
    public void TheLockWaitsForTheSetSeconds()
    {
        var rig = new Rig();
        rig.Settings.LayoutLockSeconds = 30;
        rig.StartFacing();
        rig.Unlock();
        rig.TurnAway();
        rig.Tick();

        rig.Clock += 29_999;
        rig.Tick();
        Assert.True(rig.Wrist.InLayout);

        rig.Clock += 1;
        rig.Tick();
        Assert.False(rig.Wrist.InLayout);
    }

    /// <summary><b>0 なら戻さない</b>（完了を押すまで Layout Mode のまま）。</summary>
    [Fact]
    public void ZeroSecondsNeverLocks()
    {
        var rig = new Rig();
        rig.Settings.LayoutLockSeconds = 0;
        rig.StartFacing();
        rig.Unlock();
        rig.TurnAway();
        rig.Tick();

        rig.Clock += 3_600_000;
        rig.Tick();

        Assert.True(rig.Wrist.InLayout);
    }

    /// <summary>
    /// <b>板を指したら数え直す</b>（触らない時間は続けて数える）。6 秒ずつ 2 回放っておくので、数え直さなければ合わせて 10 秒を越える。
    /// </summary>
    [Fact]
    public void PointingAtTheWristStartsTheCountAgain()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.Unlock();

        rig.Clock += 6_000;
        rig.Tick();

        rig.PointAt();
        rig.Tick();
        Assert.True(rig.Wrist.Pointed);

        rig.PointAway();
        rig.Tick();
        rig.Clock += 6_000;
        rig.Tick();

        Assert.True(rig.Wrist.InLayout);
    }

    /// <summary>手首を見ているだけで、指していなければ数える。押し忘れたまま手首を眺めていても、入力を預かったままにしない。</summary>
    [Fact]
    public void JustLookingAtTheWristStillCounts()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.Unlock();

        rig.Clock += 10_000;
        rig.Tick();

        Assert.True(rig.Wrist.Shown);
        Assert.False(rig.Wrist.InLayout);
    }

    /// <summary>
    /// <b>手首を下ろしていても、窓（ほかの板）を指していれば続く</b> — 腕を下ろして窓を並べている間に切れない。指すのをやめたら数え始める。
    /// 空間に置いた窓は<b>どちらの手でも</b>指せる（手首の手でも）。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PointingAtAnotherBoardKeepsTheLayoutWhileTheWristIsDown(bool leftHand)
    {
        var other = new OtherBoard();
        var rig = new Rig(other: other);
        rig.StartFacing();
        rig.Unlock();

        // 手首の板から 50 cm 右で、30 cm 先のほかの板を指したまま、手首を下ろす。
        var aim = rig.Board().Then(VrMatrix.Translation(0.5f, 0, 0.3f));
        other.Pose = aim.Then(VrMatrix.Translation(0, 0, -0.3f));
        Aim(rig, leftHand, aim);
        rig.TurnAway();
        rig.Tick();

        rig.Clock += 60_000;
        rig.Tick();
        Assert.False(rig.Wrist.Shown);
        Assert.True(rig.Wrist.InLayout);

        Aim(rig, leftHand, null);
        rig.Tick();
        rig.Clock += 10_000;
        rig.Tick();
        Assert.False(rig.Wrist.InLayout);

        static void Aim(Rig rig, bool leftHand, VrMatrix? aim)
        {
            if (leftHand) rig.Overlays.LeftHand = rig.Overlays.LeftHand with { Aim = aim };
            else rig.Overlays.RightHand = rig.Overlays.RightHand with { Aim = aim };
        }
    }

    /// <summary><b>窓を掴んでいる間は続く</b>（掴んでいる窓は指されない — 手に付いて動くので）。離したら数え始める。</summary>
    [Fact]
    public void HoldingTheWindowKeepsTheLayout()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.Unlock();
        rig.TurnAway();
        rig.WindowHeld = true;
        rig.Tick();

        rig.Clock += 60_000;
        rig.Tick();
        Assert.True(rig.Wrist.InLayout);

        rig.WindowHeld = false;
        rig.Tick();
        rig.Clock += 10_000;
        rig.Tick();
        Assert.False(rig.Wrist.InLayout);
    }

    /// <summary>手首を掴んでいる間も続く（掴んでいる板は指されない）。掴んだあと 1 周回してから時計を進める — 掴み始めた周は指していたので、数え始めるのは次の周から（すぐ進めると、壊れていても数え始めた所で終わる）。</summary>
    [Fact]
    public void HoldingTheWristKeepsTheLayout()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.Unlock();
        rig.Grab();
        rig.Tick();
        Assert.True(rig.Wrist.Dragging);

        rig.Clock += 60_000;
        rig.Tick();

        Assert.True(rig.Wrist.Dragging);
        Assert.True(rig.Wrist.InLayout);
    }

    /// <summary>
    /// <b>入力を全部預かるのは Layout Mode の間だけ</b>（VRChat に届かない）。手は周の頭で読むので、入った周・出た周は前のまま（1 周遅れ）。
    /// </summary>
    [Fact]
    public void TheInputsAreHeldOnlyInLayoutMode()
    {
        var rig = new Rig();
        rig.StartFacing();
        Assert.DoesNotContain(true, rig.Overlays.Captures);

        rig.Unlock();
        Assert.False(rig.Overlays.Captures.Last());

        rig.Tick();
        Assert.True(rig.Overlays.Captures.Last());

        rig.Wrist.RequestLayout(false);
        rig.Tick();
        Assert.True(rig.Overlays.Captures.Last());

        rig.Tick();
        Assert.False(rig.Overlays.Captures.Last());
    }

    /// <summary>
    /// <b>預かったまま畳まない。</b> 手首と窓だけを切ってダッシュボードのパネルで繋ぎを保つと、そのあと手を読む周が来ない。
    /// 最後に渡した優先度の高い set が SteamVR に残ると VRChat に入力が届かないままになりうるので、畳むときに普段の set で読み直す。
    /// </summary>
    [Fact]
    public void PuttingItAwayInLayoutModeGivesTheInputsBack()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.Unlock();
        rig.Tick();
        Assert.True(rig.Overlays.Captures.Last());

        rig.Scene.Stop();

        Assert.False(rig.Overlays.Captures.Last());
    }

    /// <summary>止めたら、<b>次に付けたときは固定から始める</b>。</summary>
    [Fact]
    public void StoppingLocksAgain()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.Unlock();

        rig.Wrist.Stop();
        rig.Start();

        Assert.False(rig.Wrist.InLayout);
        Assert.False(rig.Layouts.Last().On);
    }

    /// <summary>1 回押すと 1 刻み。<b>変えたその場で覚えて</b>、板にも当てて、ページにも幅を送る。</summary>
    [Fact]
    public void ResizingStepsTheWidth()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.Unlock();

        rig.Wrist.RequestResize(1);
        rig.Tick();

        var wider = WristHost.DefaultWidth + WristHost.WidthStep;
        Assert.Equal(wider, rig.Settings.Width!.Value, 1e-6f);

        // OpenVR に渡すのは絵 1 枚の実寸（設定の大きさの Width / BoardWidth 倍）。
        Assert.Equal(wider * WristHost.Width / WristHost.BoardWidth, rig.Overlays.Widths.Single(), 1e-6f);
        Assert.Equal(wider, rig.Layouts.Last().Width, 1e-6f);
        Assert.True(rig.Layouts.Last().On);

        rig.Wrist.RequestResize(-1);
        rig.Tick();

        Assert.Equal(WristHost.DefaultWidth * WristHost.Width / WristHost.BoardWidth, rig.Overlays.Widths.Last(), 1e-6f);
    }

    /// <summary>
    /// <b>周と周の間に 2 回押されたら 2 刻み</b>（押した分だけ効く）。押すのは CEF のスレッド、
    /// 拾うのは周なので、間が詰まると 1 周に 2 回分が届く。
    /// </summary>
    [Fact]
    public void TwoPressesBetweenTicksAreTwoSteps()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.Unlock();

        rig.Wrist.RequestResize(1);
        rig.Wrist.RequestResize(1);
        rig.Tick();

        Assert.Equal(WristHost.DefaultWidth + 2 * WristHost.WidthStep, rig.Wrist.Size, 1e-6f);
    }

    [Fact]
    public void TheWidthStopsAtBothEnds()
    {
        var rig = new Rig();
        rig.StartFacing();
        rig.Unlock();

        rig.Wrist.RequestResize(100);
        rig.Tick();
        Assert.Equal(WristHost.MaxWidth, rig.Wrist.Size);

        rig.Wrist.RequestResize(-100);
        rig.Tick();
        Assert.Equal(WristHost.MinWidth, rig.Wrist.Size);
    }

    /// <summary>覚えた大きさで<b>最初から</b>作る。同じ幅のままなら付け直さない。</summary>
    [Fact]
    public void ASavedWidthIsUsedFromTheStart()
    {
        var rig = new Rig();
        rig.Settings.Width = 0.3f;

        rig.StartFacing();

        Assert.Equal(0.3f * WristHost.Width / WristHost.BoardWidth, rig.Overlays.CreatedWidth, 1e-6f);
        Assert.Empty(rig.Overlays.Widths);
    }

    /// <summary>
    /// 範囲の外・刻みの外は<b>丸める</b>（手で書き換えた <c>config.json</c> も来る）。
    /// 足し込んで出来た幅も、<b>同じ幅なら同じ数</b>になること（端数が残ると、同じ幅なのに付け直す）。
    /// </summary>
    [Theory]
    [InlineData(null, 0.24f)]
    [InlineData(float.NaN, 0.24f)]
    [InlineData(float.PositiveInfinity, 0.24f)]
    [InlineData(5f, 0.40f)]
    [InlineData(-1f, 0.08f)]
    [InlineData(0.01f, 0.08f)]
    [InlineData(0.229f, 0.22f)]
    [InlineData(0.231f, 0.24f)]
    [InlineData(0.18f + 0.02f, 0.2f)]
    public void TheWidthIsKeptInRangeAndOnTheSteps(float? given, float expected)
    {
        Assert.Equal(expected, WristHost.ClampWidth(given));
    }

    /// <summary>開発用の頭の前でも、Layout Mode と大きさは効く（合成済みの絵で確かめるため）。掴むのは効かない。</summary>
    [Fact]
    public void OnTheHeadTheLayoutAndTheWidthWork()
    {
        var rig = new Rig(onHead: true);
        rig.Start();
        rig.Tick();

        rig.Unlock();
        rig.Wrist.RequestResize(1);
        rig.Tick();
        rig.Grab();

        Assert.True(rig.Wrist.InLayout);
        Assert.Equal(WristHost.DefaultWidth + WristHost.WidthStep, rig.Wrist.Size, 1e-6f);
        Assert.False(rig.Wrist.Dragging);
    }

    /// <summary>
    /// <b>手首が転んでも外へ投げない。</b> 投げると VR の周ごと止まり、ダッシュボードまで消える。
    /// 手首だけ畳む。
    /// </summary>
    [Fact]
    public void AFailingWristStopsItselfWithoutThrowing()
    {
        var rig = new Rig();
        rig.Start();
        rig.Overlays.PosesFail = true;

        rig.Tick();
        rig.Tick();

        Assert.False(rig.Wrist.Running);
        Assert.Equal(1, rig.Overlays.Destroyed);
    }

    [Fact]
    public void StoppingPutsTheBoardAwayOnce()
    {
        var rig = new Rig();
        rig.StartFacing();

        rig.Wrist.Stop();
        rig.Wrist.Stop();

        Assert.False(rig.Wrist.Running);
        Assert.Equal(1, rig.Overlays.Destroyed);
        Assert.Equal([true, false], rig.Woken);
    }

    /// <summary>離したら歪みを取ってから覚える（掴み直すたびに SteamVR の姿勢の誤差が積もって軸の長さが狂う）。反対の手の姿勢をわざと剛体から外して掴み、覚えたものが剛体になっていること。</summary>
    [Fact]
    public void ReleasingTakesOutTheDrift()
    {
        var rig = new Rig();
        rig.Overlays.Pose[Right] = VrMatrix.Translation(0.3f, 1.2f, -0.3f);
        rig.StartFacing();
        rig.Unlock();

        rig.Grab();

        // 反対の手の回転を少しだけ伸ばす（SteamVR の姿勢の誤差に見立てる）。
        var skewed = VrMatrix.Translation(0.4f, 1.2f, -0.3f).Then(VrMatrix.RotationY(20));
        rig.Overlays.Pose[Right] = skewed with { M0 = skewed.M0 * 1.01f, M4 = skewed.M4 * 1.01f, M8 = skewed.M8 * 1.01f };
        rig.Tick();

        var (mx, my, mz) = rig.Overlays.Attached.Last().Offset.AxisX;
        Assert.True(MathF.Abs(mx * mx + my * my + mz * mz - 1) > 1e-3f, "動かした置き場が歪んでいない（試験の前提が崩れている）");

        rig.LetGo();

        WristPlacementTests.AssertRigid(rig.Settings.Saved[VrHand.Left]);
    }

    /// <summary>覚えた置き場が、動かしたところと<b>同じ所</b>か（歪みを取った分の端数は見ない）。</summary>
    private static void AssertSamePlace(VrMatrix expected, VrMatrix actual)
    {
        var e = expected.ToArray();
        var a = actual.ToArray();
        for (var i = 0; i < 12; i++) Assert.Equal(e[i], a[i], 1e-5f);
    }

    private sealed class FakeSettings : IWristSettings
    {
        public VrHand Hand { get; set; } = VrHand.Left;

        public float? Width { get; set; }

        public int LayoutLockSeconds { get; set; } = 10;

        public int ShowWithinDegrees { get; set; } = WristPlacement.DefaultShowWithinDegrees;

        public int HideBeyondDegrees { get; set; } = WristPlacement.DefaultHideBeyondDegrees;

        public void SaveWidth(float? meters) => Width = meters;

        public Dictionary<VrHand, VrMatrix> Saved { get; } = [];

        public VrMatrix? SavedOffset(VrHand hand) => Saved.TryGetValue(hand, out var m) ? m : null;

        public void SaveOffset(VrHand hand, VrMatrix? offset)
        {
            if (offset is { } m) Saved[hand] = m;
            else Saved.Remove(hand);
        }
    }

    private sealed class FakeOverlays : IVrOverlays
    {
        public Dictionary<VrHand, uint> Controllers { get; } = new() { [VrHand.Left] = Left, [VrHand.Right] = Right };

        /// <summary>
        /// 機器の番号 → 姿勢。頭（0 番）と両手は最初から居る。
        /// 手の初期位置は「腰の前あたり」で、向きは試験ごとに <c>FaceTheHead</c> / <c>TurnAway</c> で決める。
        /// </summary>
        public Dictionary<uint, VrMatrix?> Pose { get; } = new()
        {
            [0] = Head,
            [Left] = VrMatrix.Translation(-0.2f, 1.0f, -0.3f),
            [Right] = VrMatrix.Translation(0.3f, 1.2f, -0.3f),
        };

        /// <summary>手首の板の handle。<b>付ける・出す・消すは、これの分だけ数える</b>（自前のレーザーの線と点は別）。</summary>
        private const ulong BoardHandle = 7;

        public List<string> Created { get; } = [];

        public List<(uint Device, VrMatrix Offset)> Attached { get; } = [];

        public List<bool> ShownChanges { get; } = [];

        public bool InputEnabled { get; private set; }

        public bool PosesFail { get; set; }

        public int Destroyed { get; private set; }

        public VrHandInput LeftHand { get; set; }

        public VrHandInput RightHand { get; set; }

        public int HandReads { get; private set; }

        /// <summary>手を読むたびの「入力を全部預かるか」（読んだ順）。</summary>
        public List<bool> Captures { get; } = [];

        public VrHands ReadHands(bool captureAll)
        {
            HandReads++;
            Captures.Add(captureAll);
            return new VrHands(LeftHand, RightHand);
        }

        public List<(VrHand Hand, float Seconds, float Amplitude)> Pulses { get; } = [];

        public void Pulse(VrHand hand, float seconds, float amplitude) => Pulses.Add((hand, seconds, amplitude));

        public bool DashboardIsOpen { get; set; }

        public bool DashboardOpen() => DashboardIsOpen;

        public float? DisplayFrequency() => 90f;

        public List<VrSprite> Sprites { get; } = [];

        public Dictionary<ulong, (uint Device, VrMatrix Offset)> SpritesAt { get; } = [];

        public Dictionary<ulong, bool> SpritesShown { get; } = [];

        public int SpritesDestroyed { get; private set; }

        public ulong CreateSprite(string key, string name, VrSprite sprite, float widthInMeters)
        {
            Sprites.Add(sprite);
            return 20 + (ulong)Sprites.Count;
        }

        public (float U, float V)? Intersect(ulong handle, VrMatrix aim) => null;

        /// <summary>作ったときの横幅。</summary>
        public float CreatedWidth { get; private set; }

        /// <summary>作ったあとに当て直した横幅（当てた順）。</summary>
        public List<float> Widths { get; } = [];

        public ulong CreateOverlay(string key, string name, float widthInMeters)
        {
            Created.Add(key);
            CreatedWidth = widthInMeters;
            return 7;
        }

        public void SetWidth(ulong handle, float widthInMeters) => Widths.Add(widthInMeters);

        public void Attach(ulong handle, uint device, VrMatrix offset)
        {
            if (handle == BoardHandle) Attached.Add((device, offset));
            else SpritesAt[handle] = (device, offset);
        }

        public void PlaceAbsolute(ulong handle, VrMatrix pose)
        {
        }

        public void SetShown(ulong handle, bool shown)
        {
            if (handle == BoardHandle) ShownChanges.Add(shown);
            else SpritesShown[handle] = shown;
        }

        /// <summary>板に渡した透明度（渡した順）。</summary>
        public List<float> Alphas { get; } = [];

        public void SetAlpha(ulong handle, float alpha)
        {
            if (handle == BoardHandle) Alphas.Add(alpha);
        }

        public uint? ControllerFor(VrHand hand) => Controllers.TryGetValue(hand, out var index) ? index : null;

        public IReadOnlyList<VrMatrix?> Poses()
        {
            if (PosesFail) throw new InvalidOperationException("the runtime went away");

            var poses = new VrMatrix?[8];
            foreach (var (device, pose) in Pose) poses[device] = pose;
            return poses;
        }

        public void EnableInput(ulong handle, int width, int height) => InputEnabled = true;

        public IReadOnlyList<VrPointer> PollPointer(ulong handle) => [];

        public void Destroy(ulong handle)
        {
            if (handle == BoardHandle) Destroyed++;
            else SpritesDestroyed++;
        }

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
