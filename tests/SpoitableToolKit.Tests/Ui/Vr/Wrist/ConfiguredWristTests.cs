using SpoitableToolKit.Host.Config;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Wrist;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Vr.Wrist;

public sealed class ConfiguredWristTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "stk-wrist", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private (ConfiguredWrist Wrist, HostConfig Config) New()
    {
        var config = new HostConfig();
        var store = new ModuleConfigStore(new EventHubs(_ => true, NullLoggerFactory.Instance), NullLoggerFactory.Instance);
        store.Add(HostConfig.Id, "Host", "⚙", config, _directory);

        return (new ConfiguredWrist(config, store), config);
    }

    [Fact]
    public void NothingIsSavedAtFirst()
    {
        var (wrist, _) = New();

        Assert.Null(wrist.SavedOffset(VrHand.Left));
        Assert.Equal(VrHand.Left, wrist.Hand);
    }

    [Fact]
    public void ASavedPlaceSurvivesARestart()
    {
        var place = VrMatrix.Translation(0.01f, 0.02f, 0.13f).Then(VrMatrix.RotationX(-80));

        var (wrist, _) = New();
        wrist.SaveOffset(VrHand.Left, place);

        var (again, _) = New();
        var loaded = again.SavedOffset(VrHand.Left);

        Assert.NotNull(loaded);
        var expected = place.ToArray();
        var actual = loaded.Value.ToArray();
        for (var i = 0; i < 12; i++) Assert.Equal(expected[i], actual[i], 1e-6f);
    }

    /// <summary>左で合わせた位置を右へ持ち越すと、鏡写しにならず変な所へ出る。</summary>
    [Fact]
    public void EachHandKeepsItsOwnPlace()
    {
        var (wrist, _) = New();

        wrist.SaveOffset(VrHand.Left, VrMatrix.Translation(1, 0, 0));

        Assert.Null(wrist.SavedOffset(VrHand.Right));
    }

    [Fact]
    public void ForgettingGoesBackToTheDefault()
    {
        var (wrist, _) = New();

        wrist.SaveOffset(VrHand.Left, VrMatrix.Translation(1, 0, 0));
        wrist.SaveOffset(VrHand.Left, null);

        Assert.Null(wrist.SavedOffset(VrHand.Left));
    }

    /// <summary>保存は別のスレッドからも走るので、書き出し中の辞書を書き換えると落ちうる。</summary>
    [Fact]
    public void TheDictionaryIsReplacedNotChangedInPlace()
    {
        var (wrist, config) = New();
        var before = config.WristOffsets;

        wrist.SaveOffset(VrHand.Left, VrMatrix.Identity);

        Assert.NotSame(before, config.WristOffsets);
        Assert.Empty(before);
    }

    [Fact]
    public void ABrokenSaveIsIgnored()
    {
        var (wrist, config) = New();

        config.WristOffsets = new() { ["left"] = [1, 2, 3] };

        Assert.Null(wrist.SavedOffset(VrHand.Left));
    }

    [Fact]
    public void ASavedWidthSurvivesARestart()
    {
        var (wrist, _) = New();
        Assert.Null(wrist.Width);

        wrist.SaveWidth(0.24f);

        var (again, _) = New();
        Assert.Equal(0.24f, again.Width);
    }

    [Fact]
    public void ForgettingTheWidthGoesBackToTheDefault()
    {
        var (wrist, _) = New();

        wrist.SaveWidth(0.24f);
        wrist.SaveWidth(null);

        var (again, _) = New();
        Assert.Null(again.Width);
    }

    /// <summary>既定 10 秒、0 で戻さない。</summary>
    [Fact]
    public void TheLayoutLockFollowsTheSetting()
    {
        var (wrist, config) = New();
        Assert.Equal(10, wrist.LayoutLockSeconds);

        config.WristLayoutLockSeconds = 0;

        Assert.Equal(0, wrist.LayoutLockSeconds);
    }

    /// <summary>既定 30° / 45°。周ごとに読むので、設定を変えたら次に読んだときから。</summary>
    [Fact]
    public void TheShowAndHideAnglesFollowTheSettings()
    {
        var (wrist, config) = New();
        Assert.Equal(30, wrist.ShowWithinDegrees);
        Assert.Equal(45, wrist.HideBeyondDegrees);

        config.WristShowWithinDegrees = 25;
        config.WristHideBeyondDegrees = 50;

        Assert.Equal(25, wrist.ShowWithinDegrees);
        Assert.Equal(50, wrist.HideBeyondDegrees);
    }

    [Fact]
    public void TheHandFollowsTheSetting()
    {
        var (wrist, config) = New();

        config.WristHand = WristHand.Right;

        Assert.Equal(VrHand.Right, wrist.Hand);
    }
}
