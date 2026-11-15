using SpoitableToolKit.Modules.Hrs.Devices.Models;

namespace SpoitableToolKit.Modules.Hrs.Devices.Interfaces;

/// <summary>
/// 心拍計を見つけて窓口へ差し出す係。種類ごとに 1 つ — BLE のスキャン、時計（HDS）の待ち受け、今後足すもの。
/// 窓口（<see cref="HeartRateManager"/>）だけが使う。
/// </summary>
internal interface IHeartRateDeviceDiscovery
{
    /// <summary>
    /// 見つける・繋ぐ・切れたら繋ぎ直す。見つけた・繋がりが変わった・1 拍届いた、を <paramref name="sink"/> へ<b>係のスレッドから</b>知らせる。
    /// <paramref name="cancellationToken"/> が切れるまで戻らない（使えない機械 — Bluetooth が無い・口を開けない — でも例外を出さずに待つか、すぐ戻る）。
    /// 戻る前に掴んでいるものを放し、その知らせも <paramref name="sink"/> へ出してよい（窓口はまだ聞いている）。
    /// </summary>
    Task DiscoverAsync(DeviceSelector selector, IHeartRateDeviceSink sink, CancellationToken cancellationToken);
}
