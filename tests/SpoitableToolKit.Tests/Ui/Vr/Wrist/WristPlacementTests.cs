using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Wrist;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Vr.Wrist;

public class WristPlacementTests
{
    private const float Close = 1e-4f;

    [Fact]
    public void ThenAppliesTheRightHandSideFirst()
    {
        var moved = VrMatrix.Translation(1, 0, 0).Then(VrMatrix.RotationY(90));
        var (x, y, z) = moved.Position;

        // 回しても位置は動かない（位置は左の行列から来る）。
        Assert.Equal(1, x, Close);
        Assert.Equal(0, y, Close);
        Assert.Equal(0, z, Close);

        var rotated = VrMatrix.RotationY(90).Then(VrMatrix.Translation(1, 0, 0));
        (x, _, z) = rotated.Position;

        // 回した座標系の中で X へ 1 動くと、外から見ると -Z へ動いている。
        Assert.Equal(0, x, Close);
        Assert.Equal(-1, z, Close);
    }

    /// <summary>逆を掛けたら元に戻る（掴んで動かした置き場の計算はこれに頼っている）。</summary>
    [Fact]
    public void AMatrixTimesItsInverseIsTheIdentity()
    {
        var m = VrMatrix.Translation(0.3f, -1.2f, 2f).Then(VrMatrix.RotationX(37)).Then(VrMatrix.RotationY(-112));
        var i = m.Then(m.Inverse());

        var expected = VrMatrix.Identity.ToArray();
        var actual = i.ToArray();
        for (var k = 0; k < 12; k++) Assert.Equal(expected[k], actual[k], Close);
    }

    [Fact]
    public void SavedValuesComeBackAsTheSameMatrix()
    {
        var m = VrMatrix.Translation(1, 2, 3).Then(VrMatrix.RotationX(-90));

        Assert.Equal(m, VrMatrix.FromArray(m.ToArray()));
    }

    /// <summary>壊れた保存は既定の置き場へ戻す（数でないものを OpenVR へ渡すと、板が無限遠へ飛んで見えなくなる）。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(new float[] { 1, 2, 3 })]
    [InlineData(new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, float.NaN })]
    [InlineData(new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, float.PositiveInfinity })]
    public void ABrokenSaveIsTreatedAsNone(float[]? values)
    {
        Assert.Null(VrMatrix.FromArray(values));
    }

    [Fact]
    public void ABoardInFrontOfTheHeadFacesIt()
    {
        var head = VrMatrix.Translation(0, 1.6f, 0);
        var board = head.Then(WristPlacement.InFrontOfHead);

        Assert.True(WristPlacement.Facing(head, board) > 0.99f);
    }

    [Fact]
    public void ABoardTurnedAroundFacesAway()
    {
        var head = VrMatrix.Translation(0, 1.6f, 0);
        var board = head.Then(WristPlacement.InFrontOfHead).Then(VrMatrix.RotationY(180));

        Assert.True(WristPlacement.Facing(head, board) < -0.99f);
    }

    [Fact]
    public void ABoardSeenEdgeOnIsNotFacing()
    {
        var head = VrMatrix.Translation(0, 1.6f, 0);
        var board = head.Then(WristPlacement.InFrontOfHead).Then(VrMatrix.RotationY(90));

        Assert.Equal(0, WristPlacement.Facing(head, board), 1e-3f);
    }

    /// <summary>目と板が同じ場所にあるときは向きが決まらない。<b>出さない側に倒す</b>。</summary>
    [Fact]
    public void ABoardAtTheEyeIsNotFacing()
    {
        Assert.Equal(-1, WristPlacement.Facing(VrMatrix.Identity, VrMatrix.Identity));
    }

    // 視線: 決め手は「板の真ん中が視野の真ん中の近くにあるか」。面の向きは裏・真横を弾くだけ。

    [Fact]
    public void ABoardStraightAheadIsInTheMiddleOfTheView()
    {
        var head = VrMatrix.Translation(0, 1.6f, 0);
        var board = head.Then(WristPlacement.InFrontOfHead);

        // 開発用の置き場は少し下（5 cm / 45 cm）にあるので、ぴったり 1 ではない。
        Assert.True(WristPlacement.Gaze(head, board) > 0.99f);
    }

    [Fact]
    public void ABoardBesideOrBehindTheHeadIsOutOfView()
    {
        var head = VrMatrix.Translation(0, 1.6f, 0);

        Assert.Equal(0, WristPlacement.Gaze(head, head.Then(VrMatrix.Translation(0.4f, 0, 0))), 1e-4f);
        Assert.Equal(-1, WristPlacement.Gaze(head, head.Then(VrMatrix.Translation(0, 0, 0.4f))), 1e-4f);
    }

    /// <summary>
    /// 視線は<b>頭がどこを向いているか</b>で決まり、<b>板がどちらを向いているかでは変わらない</b>（そちらは <see cref="WristPlacement.Facing"/>）。
    /// 板の前で頭を 40° 横へ振れば 40° 外れる。
    /// </summary>
    [Fact]
    public void TheGazeFollowsTheHeadNotTheWayTheBoardFaces()
    {
        var head = VrMatrix.Translation(0, 1.6f, 0);
        var board = head.Then(VrMatrix.Translation(0, 0, -0.4f));

        Assert.Equal(1, WristPlacement.Gaze(head, board.Then(VrMatrix.RotationY(70))), 1e-4f);
        Assert.Equal(MathF.Cos(40 * MathF.PI / 180f), WristPlacement.Gaze(head.Then(VrMatrix.RotationY(40)), board), 1e-4f);
    }

    /// <summary>目と板が同じ場所にあるときは向きが決まらない。<b>出さない側に倒す</b>。</summary>
    [Fact]
    public void ABoardAtTheEyeIsNotInView()
    {
        Assert.Equal(-1, WristPlacement.Gaze(VrMatrix.Identity, VrMatrix.Identity));
    }

    /// <summary>面がこちらを向いていても、視野の真ん中から外れていれば出さない（前を見ながら手首をこちらへ向けた場合）。</summary>
    [Fact]
    public void ABoardFacingYouButOutOfViewIsNotShown()
    {
        var angles = WristAngles.Default;

        Assert.False(WristPlacement.ShouldShow(shown: false, facing: 1f, gaze: angles.ShowWhenCosAbove - 0.01f, angles));
        Assert.False(WristPlacement.ShouldShow(shown: true, facing: 1f, gaze: angles.HideWhenCosAbove - 0.01f, angles));
    }

    /// <summary><b>視野の真ん中にあっても、裏や真横を向いていれば出さない</b>（面の向きは緩く: 出すのは 60° 以内、隠すのは 75° より外）。</summary>
    [Fact]
    public void ABoardInViewButTurnedAwayIsNotShown()
    {
        var angles = WristAngles.Default;

        Assert.False(WristPlacement.ShouldShow(shown: false, facing: WristPlacement.FacingShowWhenCosAbove - 0.01f, gaze: 1f, angles));
        Assert.False(WristPlacement.ShouldShow(shown: true, facing: WristPlacement.FacingHideWhenCosAbove - 0.01f, gaze: 1f, angles));
        Assert.Equal(0.5f, WristPlacement.FacingShowWhenCosAbove, 1e-4f);
        Assert.Equal(MathF.Cos(75 * MathF.PI / 180f), WristPlacement.FacingHideWhenCosAbove, 1e-4f);
    }

    /// <summary>
    /// <b>出す側と隠す側で閾値が違う。</b> その間で止めたときに、出たり消えたりを繰り返さない —
    /// 板が出ている間はレーザーも出るので、点滅はそのまま操作の邪魔になる。視線でも面の向きでも同じ。
    /// </summary>
    [Fact]
    public void BetweenTheThresholdsTheBoardKeepsWhatItWasDoing()
    {
        var angles = WristAngles.Default;
        var gazeBetween = (angles.ShowWhenCosAbove + angles.HideWhenCosAbove) / 2;
        var facingBetween = (WristPlacement.FacingShowWhenCosAbove + WristPlacement.FacingHideWhenCosAbove) / 2;

        Assert.False(WristPlacement.ShouldShow(shown: false, facing: 1f, gaze: gazeBetween, angles));
        Assert.True(WristPlacement.ShouldShow(shown: true, facing: 1f, gaze: gazeBetween, angles));

        Assert.False(WristPlacement.ShouldShow(shown: false, facing: facingBetween, gaze: 1f, angles));
        Assert.True(WristPlacement.ShouldShow(shown: true, facing: facingBetween, gaze: 1f, angles));
    }

    [Fact]
    public void TheThresholdsThemselvesCount()
    {
        var angles = WristAngles.Default;

        Assert.True(WristPlacement.ShouldShow(shown: false, facing: 1f, gaze: angles.ShowWhenCosAbove, angles));
        Assert.True(WristPlacement.ShouldShow(shown: true, facing: 1f, gaze: angles.HideWhenCosAbove, angles));
        Assert.False(WristPlacement.ShouldShow(shown: true, facing: 1f, gaze: angles.HideWhenCosAbove - 0.01f, angles));

        Assert.True(WristPlacement.ShouldShow(shown: false, facing: WristPlacement.FacingShowWhenCosAbove, gaze: 1f, angles));
        Assert.True(WristPlacement.ShouldShow(shown: true, facing: WristPlacement.FacingHideWhenCosAbove, gaze: 1f, angles));
    }

    /// <summary>既定は出す 30°・隠す 45°（cos 30° = 0.866・cos 45° = 0.707）。</summary>
    [Fact]
    public void TheDefaultAnglesAreThirtyAndFortyFiveDegrees()
    {
        Assert.Equal(0.866f, WristAngles.Default.ShowWhenCosAbove, 1e-3f);
        Assert.Equal(0.707f, WristAngles.Default.HideWhenCosAbove, 1e-3f);
        Assert.Equal(WristPlacement.DefaultShowWithinDegrees, new SpoitableToolKit.Host.Settings.HostConfig().WristShowWithinDegrees);
        Assert.Equal(WristPlacement.DefaultHideBeyondDegrees, new SpoitableToolKit.Host.Settings.HostConfig().WristHideBeyondDegrees);
    }

    [Theory]
    [InlineData(20, 60)]
    [InlineData(40, 50)]
    [InlineData(10, 80)]
    public void TheAnglesBecomeTheirCosines(int show, int hide)
    {
        var angles = WristAngles.FromDegrees(show, hide);

        Assert.Equal(MathF.Cos(show * MathF.PI / 180f), angles.ShowWhenCosAbove, 1e-5f);
        Assert.Equal(MathF.Cos(hide * MathF.PI / 180f), angles.HideWhenCosAbove, 1e-5f);
    }

    /// <summary>
    /// 隠す角度が出す角度より小さければ、出す角度として扱う。そのままだと、出した次の周で隠れる
    /// （出す 40° で出た板は 40° 以内にあるが、隠す 20° の外なので）。
    /// </summary>
    [Fact]
    public void AHideAngleInsideTheShowAngleCountsAsTheShowAngle()
    {
        var angles = WristAngles.FromDegrees(40, 20);

        Assert.Equal(angles.ShowWhenCosAbove, angles.HideWhenCosAbove);

        // 出た所で、次の周も出たまま。
        var justShown = angles.ShowWhenCosAbove;
        Assert.True(WristPlacement.ShouldShow(shown: false, facing: 1f, gaze: justShown, angles));
        Assert.True(WristPlacement.ShouldShow(shown: true, facing: 1f, gaze: justShown, angles));
    }

    /// <summary><b>範囲（5〜90°）の外は丸める。</b> 設定の入力は範囲で止まるが、config.json を手で書き換えた値はそのまま来る。</summary>
    [Theory]
    [InlineData(0, 45, 5, 45)]
    [InlineData(-30, 45, 5, 45)]
    [InlineData(30, 200, 30, 90)]
    [InlineData(120, 150, 90, 90)]
    public void AnglesOutsideTheRangeAreClamped(int show, int hide, int expectedShow, int expectedHide)
    {
        var angles = WristAngles.FromDegrees(show, hide);

        Assert.Equal(MathF.Cos(expectedShow * MathF.PI / 180f), angles.ShowWhenCosAbove, 1e-5f);
        Assert.Equal(MathF.Cos(expectedHide * MathF.PI / 180f), angles.HideWhenCosAbove, 1e-5f);
    }

    [Fact]
    public void TheRightHandMirrorsTheLeft()
    {
        var (lx, ly, lz) = WristPlacement.DefaultOffset(VrHand.Left).Position;
        var (rx, ry, rz) = WristPlacement.DefaultOffset(VrHand.Right).Position;

        Assert.Equal(-lx, rx, Close);
        Assert.Equal(ly, ry, Close);
        Assert.Equal(lz, rz, Close);
    }

    /// <summary>既定の板は XSOverlay の手首の板と被りにくい所。見える面はおおむね手の甲の側（左手は -X、右手は +X）で、位置も手の甲の側。</summary>
    [Theory]
    [InlineData(true, -1f)]
    [InlineData(false, 1f)]
    public void TheDefaultIsWhereTheUserPutIt(bool left, float backOfTheHand)
    {
        var offset = WristPlacement.DefaultOffset(left ? VrHand.Left : VrHand.Right);

        var (zx, _, _) = offset.AxisZ;
        Assert.True(zx * WristPlacement.FrontZ * backOfTheHand > 0.7f, $"見える面が手の甲の側を向いていない（{zx}）");
        Assert.True(offset.Position.X * backOfTheHand > 0, "板が手の甲の側に無い");

        var (x, y, z) = offset.Position;
        Assert.Equal(0.1162f * backOfTheHand, x, 1e-3f);
        Assert.Equal(-0.1119f, y, 1e-3f);
        Assert.Equal(0.195f, z, 1e-3f);
    }

    /// <summary>既定は<b>剛体</b>（丸めた値の歪みを取ってある）。逆行列は転置で済ませているので、外れると逆が合わない。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheDefaultIsRigid(bool left)
    {
        var offset = WristPlacement.DefaultOffset(left ? VrHand.Left : VrHand.Right);

        AssertRigid(offset);
    }

    internal static void AssertRigid(VrMatrix m)
    {
        var (xx, xy, xz) = m.AxisX;
        var (yx, yy, yz) = m.AxisY;
        var (zx, zy, zz) = m.AxisZ;

        Assert.Equal(1, xx * xx + xy * xy + xz * xz, 1e-5f);
        Assert.Equal(1, yx * yx + yy * yy + yz * yz, 1e-5f);
        Assert.Equal(1, zx * zx + zy * zy + zz * zz, 1e-5f);
        Assert.Equal(0, xx * yx + xy * yy + xz * yz, 1e-5f);
        Assert.Equal(0, xx * zx + xy * zy + xz * zz, 1e-5f);

        // 右手系（Z = X × Y）。
        Assert.Equal(zx, xy * yz - xz * yy, 1e-5f);
        Assert.Equal(zy, xz * yx - xx * yz, 1e-5f);
        Assert.Equal(zz, xx * yy - xy * yx, 1e-5f);
    }

    /// <summary>
    /// 掴み直すたびに積もる歪みを取る（6 回ほどで軸の長さが 1.00015 になる）。
    /// 軸の長さを 1、互いに直角へ戻し、向きはほぼそのまま、位置はそのまま。
    /// </summary>
    [Fact]
    public void OrthonormalizingTakesOutTheDrift()
    {
        var rigid = VrMatrix.Translation(0.1f, 0.2f, 0.3f).Then(VrMatrix.RotationX(30)).Then(VrMatrix.RotationY(-50));
        var drifted = rigid with { M0 = rigid.M0 * 1.002f, M4 = rigid.M4 * 1.002f, M1 = rigid.M1 + 0.002f };

        var fixedUp = drifted.Orthonormalized();

        AssertRigid(fixedUp);
        Assert.Equal(rigid.Position, fixedUp.Position);

        var (ax, ay, az) = rigid.AxisZ;
        var (bx, by, bz) = fixedUp.AxisZ;
        Assert.True(ax * bx + ay * by + az * bz > 0.999f, "向きが変わってしまった");
    }

    [Fact]
    public void ARigidMatrixStaysAsItIs()
    {
        var rigid = VrMatrix.Translation(0.1f, 0.2f, 0.3f).Then(VrMatrix.RotationX(30)).Then(VrMatrix.RotationY(-50));
        var again = rigid.Orthonormalized();

        var expected = rigid.ToArray();
        var actual = again.ToArray();
        for (var i = 0; i < 12; i++) Assert.Equal(expected[i], actual[i], 1e-6f);
    }

    /// <summary>潰れた行列は<b>直せないので、そのまま返す</b>（NaN を作らない）。</summary>
    [Fact]
    public void ACollapsedMatrixIsLeftAlone()
    {
        var collapsed = new VrMatrix(0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3);

        Assert.Equal(collapsed, collapsed.Orthonormalized());
    }

    [Fact]
    public void MirroringKeepsItRightHanded()
    {
        var left = VrMatrix.Translation(-0.1f, 0.2f, 0.3f).Then(VrMatrix.RotationX(30)).Then(VrMatrix.RotationY(-50));

        var right = left.MirroredX();

        AssertRigid(right);
        Assert.Equal((0.1f, 0.2f, 0.3f), right.Position);
        Assert.Equal(left, right.MirroredX());

        // 見える面（Z）も X だけ反る。
        var (lx, ly, lz) = left.AxisZ;
        Assert.Equal((-lx, ly, lz), right.AxisZ);
    }
}
