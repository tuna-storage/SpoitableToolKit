using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Pointing;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Vr.Pointing;

public class VrBoardHitTests
{
    /// <summary>板。頭の高さ 1m、奥へ 1m。回していないので、見える面（+Z）は手前（原点の側）を向いている。</summary>
    private static readonly VrBoard Board = new(VrMatrix.Translation(0, 1, -1), 0.2f, 0.1f, 640, 320);

    /// <summary>板の中心から見て (x, y) の、手前 0.5m から真っ直ぐ奥（-Z）を指す。</summary>
    private static VrMatrix AimAt(float x, float y) => VrMatrix.Translation(x, 1 + y, -0.5f);

    [Fact]
    public void TheMiddleOfTheBoardIsTheMiddlePixel()
    {
        var hit = VrBoardHit.Cast(AimAt(0, 0), Board, 0, 2);

        Assert.Equal(new VrHit(320, 160, 0, 0, 0.5f), hit);
    }

    /// <summary>左上が (0, 0)。OpenVR のマウスの座標（左下が原点）とは縦が逆。</summary>
    [Fact]
    public void TheTopLeftCornerIsTheFirstPixel()
    {
        var hit = VrBoardHit.Cast(AimAt(-0.1f + 1e-4f, 0.05f - 1e-4f), Board, 0, 2);

        Assert.Equal((0, 0), (hit!.Value.X, hit.Value.Y));
    }

    [Fact]
    public void TheBottomRightCornerIsTheLastPixel()
    {
        var hit = VrBoardHit.Cast(AimAt(0.1f - 1e-4f, -0.05f + 1e-4f), Board, 0, 2);

        Assert.Equal((639, 319), (hit!.Value.X, hit.Value.Y));
    }

    [Fact]
    public void TheBackIsNotHit()
    {
        var behind = VrMatrix.Translation(0, 1, -1.5f).Then(VrMatrix.RotationY(180));

        Assert.Null(VrBoardHit.Cast(behind, Board, 0, 2));
    }

    /// <summary>手が板の面より奥にあって板から離れる向きを指していたら当たらない（面を通る線は手の後ろにあり、距離が負になる）。</summary>
    [Fact]
    public void ABoardBehindTheHandIsNotHit()
    {
        var beyond = VrMatrix.Translation(0, 1, -1.5f);

        Assert.Null(VrBoardHit.Cast(beyond, Board, 0, 2));
    }

    [Fact]
    public void PointingAwayDoesNotHit()
    {
        var away = AimAt(0, 0).Then(VrMatrix.RotationY(180));

        Assert.Null(VrBoardHit.Cast(away, Board, 0, 2));
    }

    /// <summary>板の外でも余白の中なら当たったことにして、画素は端に挟む。</summary>
    [Fact]
    public void TheMarginCountsAndClampsToTheEdge()
    {
        var justOutside = AimAt(0.105f, 0);

        Assert.Null(VrBoardHit.Cast(justOutside, Board, 0, 2));

        var hit = VrBoardHit.Cast(justOutside, Board, 0.01f, 2);
        Assert.Equal(639, hit!.Value.X);
        Assert.Equal(0.105f, hit.Value.LocalX, 1e-5f);
    }

    [Fact]
    public void ABoardOutOfReachIsNotHit()
    {
        Assert.Null(VrBoardHit.Cast(AimAt(0, 0), Board, 0, 0.4f));
        Assert.NotNull(VrBoardHit.Cast(AimAt(0, 0), Board, 0, 0.6f));
    }

    /// <summary>回した板でも、板の右へずらせば画素の右へ行く。板を右へ 90° 回すと見える面は +X を向き、板の右は奥（-Z）になる。</summary>
    [Fact]
    public void ATurnedBoardStillMapsItsOwnRight()
    {
        var turned = Board with { Pose = VrMatrix.Translation(1, 1, 0).Then(VrMatrix.RotationY(90)) };

        var aim = VrMatrix.Translation(1.5f, 1, -0.0503f).Then(VrMatrix.RotationY(90));
        var hit = VrBoardHit.Cast(aim, turned, 0, 2);

        Assert.Equal(480, hit!.Value.X);
        Assert.Equal(160, hit.Value.Y);
        Assert.Equal(0.5f, hit.Value.Distance, 1e-4f);
    }
}
