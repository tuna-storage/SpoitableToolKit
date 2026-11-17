using SpoitableToolKit.Host.Settings;

namespace SpoitableToolKit.Host.Ui.Vr;

/// <summary>
/// VR のどこを使うか（設定の <see cref="HostConfig.VrOverlay"/> と <see cref="HostConfig.VrDashboard"/>）。<see cref="VrHost"/> が周ごとに読む。
/// 周ごとに読むので、切り替えはその場で効く。設定画面から書いても、ほかの所から書いても、読むのは書いた先だけなので道を選ばない。
/// </summary>
/// <param name="Overlay">手首の帯と VR ウィンドウ（<see cref="VrScene"/> の板と、自前のレーザー）。</param>
/// <param name="Dashboard">SteamVR のダッシュボードのパネル（こちらのタブ）。</param>
internal readonly record struct VrFeatures(bool Overlay, bool Dashboard)
{
    public static VrFeatures All => new(true, true);

    /// <summary>どちらも切ってある。この間は SteamVR に繋がない（繋いでいれば手放す）。</summary>
    public bool None => !Overlay && !Dashboard;

    public static VrFeatures Of(HostConfig config) => new(config.VrOverlay, config.VrDashboard);
}
