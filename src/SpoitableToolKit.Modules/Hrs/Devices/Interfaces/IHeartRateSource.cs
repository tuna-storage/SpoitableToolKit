using SpoitableToolKit.Modules.Hrs.Devices.Models;

namespace SpoitableToolKit.Modules.Hrs.Devices.Interfaces;

/// <summary>
/// 心拍の出どころ。<b>プラットフォーム依存はここから先だけ</b>。
/// <para>
/// 切り出してあるのは、心拍ループとパラメータの計算を<b>実機の BLE 無しで試験する</b>ため。
/// </para>
/// </summary>
internal interface IHeartRateSource
{
    /// <summary>スキャンで見つかっているもの。設定の候補に出す。</summary>
    IReadOnlyList<MonitorIdentity> Discovered { get; }

    /// <summary>
    /// <see cref="Discovered"/> と同じ並びに、最後に広告を受けたときの様子（電波の強さ・時刻）を添えたもの。パネルの「心拍計」タブに出す。
    /// 電波の強さと時刻が分からない入口は既定のまま（どちらも null）。
    /// </summary>
    IReadOnlyList<MonitorSighting> Sightings => [.. Discovered.Select(monitor => new MonitorSighting(monitor, null, null))];

    /// <summary>いま繋がっている先。繋がっていなければ <c>null</c>。パネルに出す。</summary>
    MonitorIdentity? Connected { get; }

    /// <summary>どう繋がっているか。繋がっていなければ <see cref="HeartRateConnection.None"/>。</summary>
    HeartRateConnection Connection { get; }

    /// <summary>
    /// これだけ黙ったら「止まった」とみなす長さ。<b>入口ごとに違う</b> —
    /// BLE は 1 秒おきに来るが、HealthKit は 5 秒おきで、手首を下ろすと数十秒止まる。
    /// </summary>
    TimeSpan SilenceAfter { get; }

    /// <summary>
    /// 心拍計の電池（%）。<b>GATT で繋がっているときだけ</b>分かる（広告と時計からは読めない）。
    /// 分からなければ <c>null</c>。繋いだときと 1 分ごとに読み直す。
    /// </summary>
    int? Battery => null;

    /// <summary>
    /// 知らせる先（窓口の <see cref="IHeartRateSink"/>）。窓口が作るときに 1 回だけ渡す。繋がった・切れた・1 拍届いた、を
    /// <b>入口のスレッドから</b>ここへ知らせる。渡される前の知らせは捨ててよい。
    /// </summary>
    void ReportTo(IHeartRateSink sink);

    /// <summary>
    /// 手で切る — <paramref name="monitor"/> の 1 台だけ（パネルの「切断」。窓口が呼ぶ）。<see cref="Connect"/> が呼ばれるまで、その 1 台には
    /// <b>勝手に繋ぎ直さない</b>（ほかの心拍計・時計は受け続ける）。すぐ戻る — 線を放すのは入口の都合でよく、
    /// 窓口は呼ぶ前にその 1 台を「繋がっていない」扱いにしている（放し終わるまでに届く 1 拍は窓口が捨てる）。
    /// </summary>
    void Disconnect(MonitorIdentity monitor);

    /// <summary>手で切った 1 台を、また繋いでよいことにする（パネルの「接続」）。切っていなければ何もしない。すぐ戻る。</summary>
    void Connect(MonitorIdentity monitor);

    /// <summary>
    /// 繋ぎに行く。見つかるまで待ち、切れたら繋ぎ直す。
    /// <paramref name="cancellationToken"/> が切れるまで戻らない。
    /// </summary>
    Task RunAsync(DeviceSelector selector, CancellationToken cancellationToken);
}
