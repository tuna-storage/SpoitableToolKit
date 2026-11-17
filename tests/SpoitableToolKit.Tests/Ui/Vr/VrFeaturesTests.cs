using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Vr;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Vr;

/// <summary>VR のどこを使うか（設定の 2 つ）。周ごとに読むのはこれなので、取り違えると切ったはずの方が消える。</summary>
public sealed class VrFeaturesTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void TheyFollowTheTwoSettings(bool overlay, bool dashboard)
    {
        var features = VrFeatures.Of(new HostConfig { VrOverlay = overlay, VrDashboard = dashboard });

        Assert.Equal(overlay, features.Overlay);
        Assert.Equal(dashboard, features.Dashboard);
    }

    /// <summary>SteamVR から手を放すのは両方切ったときだけ。片方でも入っていれば繋ぐ。</summary>
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void OnlyBothOffMeansNone(bool overlay, bool dashboard, bool none) =>
        Assert.Equal(none, new VrFeatures(overlay, dashboard).None);
}
