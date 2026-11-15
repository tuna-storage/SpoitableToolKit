using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Window;
using SpoitableToolKit.Host.Ui.Vr.Wrist;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Vr.Window;

public class VrWindowPlacementTests
{
    /// <summary>頭。原点から 1.6m 上で、-Z を見ている。</summary>
    private static readonly VrMatrix Head = VrMatrix.Translation(0, 1.6f, 0);

    [Fact]
    public void InFrontIsAheadAndALittleBelowTheEyes()
    {
        var pose = VrWindowPlacement.InFront(Head);

        AssertAt(pose, 0, 1.5f, -0.7f);
        AssertFacesTheHead(pose, Head);
    }

    [Fact]
    public void InFrontFollowsWhereTheHeadIsTurned()
    {
        var turned = Head.Then(VrMatrix.RotationY(90));

        var pose = VrWindowPlacement.InFront(turned);

        // Y で 90° 回すと -Z を見ていた頭は -X を見る。
        AssertAt(pose, -0.7f, 1.5f, 0);
        AssertFacesTheHead(pose, turned);
    }

    /// <summary>
    /// <b>下を向いていても、窓は目の高さの近くに出る</b>（見ている向きの水平な成分の先）。足元に出ると、顔を上げたら見えない。
    /// </summary>
    [Fact]
    public void LookingDownStillOpensItNearEyeHeight()
    {
        var down = Head.Then(VrMatrix.RotationX(-60));

        var pose = VrWindowPlacement.InFront(down);

        AssertAt(pose, 0, 1.5f, -0.7f);
    }

    /// <summary>
    /// 真下を見ていて見ている向きの水平な成分が無くても、<b>顔が向いていた方に出る</b>（頭の上の向きの水平な成分を使う）。
    /// 右を向いてから真下を見たら、右に出る。
    /// </summary>
    [Fact]
    public void LookingStraightDownOpensItWhereTheFaceWasTurned()
    {
        var down = Head.Then(VrMatrix.RotationY(90)).Then(VrMatrix.RotationX(-90));

        var pose = VrWindowPlacement.InFront(down);

        AssertAt(pose, -0.7f, 1.5f, 0);
        AssertRigid(pose);
    }

    [Fact]
    public void OnTheWristItSitsJustAboveTheBoardOnTheSamePlane()
    {
        var rel = VrWindowPlacement.OnWrist(wristHeight: 0.09f, windowHeight: 0.28f);

        AssertAt(rel, 0, 0.045f + VrWindowPlacement.AboveWristGap + 0.14f, 0);
        Assert.Equal((1f, 0f, 0f), rel.AxisX);
        Assert.Equal((0f, 0f, 1f), rel.AxisZ);
    }

    [Fact]
    public void FacingTheHeadKeepsTheTextUpright()
    {
        var pose = VrWindowPlacement.FacingHead((0.4f, 1.0f, -0.5f), Head.Position);

        AssertFacesTheHead(pose, Head);
        Assert.Equal(0f, pose.AxisX.Y, 1e-4f);
        Assert.True(pose.AxisY.Y > 0, "字の上が下を向いている");
        AssertRigid(pose);
    }

    /// <summary>頭の真下に置いても<b>壊れた行列にならない</b>（上の向きが決まらないので、字の右を +X に取る）。</summary>
    [Fact]
    public void FacingTheHeadFromStraightBelowIsStillRigid()
    {
        var pose = VrWindowPlacement.FacingHead((0, 1.0f, 0), Head.Position);

        AssertRigid(pose);
        Assert.Equal(1f, pose.AxisZ.Y, 1e-4f);
    }

    /// <summary>前へ 30 cm 押すと半分、手前へ 30 cm 引くと倍。<b>掛け算で効く</b>（同じ押し方なら同じ割合で変わる）。</summary>
    [Theory]
    [InlineData(0.5f, 0f, 0.5f)]
    [InlineData(0.5f, 0.3f, 0.25f)]
    [InlineData(0.5f, -0.3f, 1.0f)]
    [InlineData(1.0f, 0.3f, 0.5f)]
    [InlineData(0.5f, 0.6f, 0.125f)]
    public void PushingHalvesAndPullingDoubles(float start, float pushed, float expected) =>
        Assert.Equal(expected, VrWindowPlacement.Resized(start, pushed), 1e-5f);

    [Fact]
    public void ForwardIsIntoTheWindow()
    {
        var pose = VrWindowPlacement.InFront(Head);

        var (fx, fy, fz) = VrWindowPlacement.Forward(pose);
        var (px, py, pz) = pose.Position;
        var (hx, hy, hz) = Head.Position;

        // 頭から窓への向きと同じ側。
        Assert.True(fx * (px - hx) + fy * (py - hy) + fz * (pz - hz) > 0);
    }

    private static void AssertAt(VrMatrix pose, float x, float y, float z)
    {
        var (px, py, pz) = pose.Position;
        Assert.Equal(x, px, 1e-4f);
        Assert.Equal(y, py, 1e-4f);
        Assert.Equal(z, pz, 1e-4f);
    }

    /// <summary>見える面（+Z）が頭の方をまっすぐ向いている。</summary>
    private static void AssertFacesTheHead(VrMatrix pose, VrMatrix head) =>
        Assert.Equal(1f, WristPlacement.Facing(head, pose), 1e-4f);

    private static void AssertRigid(VrMatrix pose)
    {
        static float Dot((float X, float Y, float Z) a, (float X, float Y, float Z) b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        Assert.Equal(1f, Dot(pose.AxisX, pose.AxisX), 1e-4f);
        Assert.Equal(1f, Dot(pose.AxisY, pose.AxisY), 1e-4f);
        Assert.Equal(1f, Dot(pose.AxisZ, pose.AxisZ), 1e-4f);
        Assert.Equal(0f, Dot(pose.AxisX, pose.AxisY), 1e-4f);
        Assert.Equal(0f, Dot(pose.AxisX, pose.AxisZ), 1e-4f);
        Assert.Equal(0f, Dot(pose.AxisY, pose.AxisZ), 1e-4f);
    }
}
