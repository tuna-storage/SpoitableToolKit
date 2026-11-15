using SpoitableToolKit.Host.Config;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Window;
using SpoitableToolKit.Tests.Ui.Vr.Wrist;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Vr.Window;

public sealed class ConfiguredWindowTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "stk-window", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private (ConfiguredWindow Window, HostConfig Config) New()
    {
        var config = new HostConfig();
        var store = new ModuleConfigStore(new EventHubs(_ => true, NullLoggerFactory.Instance), NullLoggerFactory.Instance);
        store.Add(HostConfig.Id, "Host", "⚙", config, _directory);

        return (new ConfiguredWindow(config, store), config);
    }

    [Fact]
    public void NothingIsSavedAtFirst()
    {
        var (window, _) = New();

        Assert.Equal(VrWindowSpot.AboveWrist, window.Spot);
        Assert.Null(window.Width);
        Assert.Null(window.WristWidth);
        Assert.Null(window.WristOffset(VrHand.Left));
        Assert.Null(window.WristOffset(VrHand.Right));
    }

    /// <summary>手首の上は顔に近く、ちょうどよい大きさが置き方で違う。</summary>
    [Fact]
    public void EachSpotKeepsItsWidthAcrossARestart()
    {
        var (window, _) = New();

        window.SaveWidth(0.8f);
        window.SaveWristWidth(0.25f);

        var (again, _) = New();
        Assert.Equal(0.8f, again.Width);
        Assert.Equal(0.25f, again.WristWidth);
    }

    [Fact]
    public void AWristPlaceSurvivesARestart()
    {
        var place = VrMatrix.Translation(0.05f, 0.2f, 0.01f).Then(VrMatrix.RotationX(-20));

        var (window, _) = New();
        window.SaveWristOffset(VrHand.Left, place);

        var (again, _) = New();
        var loaded = again.WristOffset(VrHand.Left);

        Assert.NotNull(loaded);
        var expected = place.ToArray();
        var actual = loaded.Value.ToArray();
        for (var i = 0; i < 12; i++) Assert.Equal(expected[i], actual[i], 1e-6f);
    }

    /// <summary>左で決めた所を右へ持ち越すと、鏡写しにならない。</summary>
    [Fact]
    public void EachWristKeepsItsOwnPlace()
    {
        var (window, _) = New();

        window.SaveWristOffset(VrHand.Left, VrMatrix.Translation(0.1f, 0, 0));
        window.SaveWristOffset(VrHand.Right, VrMatrix.Translation(-0.1f, 0, 0));

        Assert.Equal(0.1f, window.WristOffset(VrHand.Left)!.Value.Position.X, 1e-6f);
        Assert.Equal(-0.1f, window.WristOffset(VrHand.Right)!.Value.Position.X, 1e-6f);
    }

    [Fact]
    public void ForgettingAWristPlaceGoesBackToTheDefault()
    {
        var (window, _) = New();

        window.SaveWristOffset(VrHand.Left, VrMatrix.Translation(1, 0, 0));
        window.SaveWristOffset(VrHand.Right, VrMatrix.Translation(1, 0, 0));
        window.SaveWristOffset(VrHand.Left, null);

        Assert.Null(window.WristOffset(VrHand.Left));
        Assert.NotNull(window.WristOffset(VrHand.Right));
    }

    /// <summary>保存は別のスレッドからも走る。書き出し中の辞書を書き換えると落ちうる。</summary>
    [Fact]
    public void TheWristDictionaryIsReplacedNotChangedInPlace()
    {
        var (window, config) = New();
        var before = config.VrWindowWristOffsets;

        window.SaveWristOffset(VrHand.Left, VrMatrix.Identity);

        Assert.NotSame(before, config.VrWindowWristOffsets);
        Assert.Empty(before);
    }

    [Fact]
    public void ABrokenWristPlaceIsIgnored()
    {
        var (window, config) = New();

        config.VrWindowWristOffsets = new() { ["left"] = [1, 2, 3] };

        Assert.Null(window.WristOffset(VrHand.Left));
    }
}
