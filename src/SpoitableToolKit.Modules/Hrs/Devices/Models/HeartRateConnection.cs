namespace SpoitableToolKit.Modules.Hrs.Devices.Models;

/// <summary>
/// どう繋がっているか。<b>「繋がっている」だけでは足りない</b> —
/// GATT が張れずに広告へ落ちたのか、時計から来ているのかで、直し方がまるで違う。
/// </summary>
internal enum HeartRateConnection
{
    /// <summary>繋がっていない。</summary>
    None,

    /// <summary>BLE の GATT で通知を受けている。ふつうはこれ。</summary>
    BleGatt,

    /// <summary>BLE の広告に載っている心拍を読んでいる（Polar）。GATT が張れないときの代替。</summary>
    BleBroadcastPolar,

    /// <summary>Health Data Server（Apple Watch）から HTTP で届いている。</summary>
    Hds,
}
