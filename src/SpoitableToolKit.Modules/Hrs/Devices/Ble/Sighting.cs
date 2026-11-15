namespace SpoitableToolKit.Modules.Hrs.Devices.Ble;

/// <summary>
/// BLE の様子を<b>ログに書くかどうか</b>の判断。BLE そのものからは切り離してあり、実機無しで試験できる。
/// 広告は 1 秒に何通も来るので、<b>来るたびに書くとログが埋まる</b>。変わった時だけ書く。
/// </summary>
internal enum Sighting
{
    /// <summary>前から知っている。書かない。</summary>
    Known,
    New,
    Named,
}
