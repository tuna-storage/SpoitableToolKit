namespace SpoitableToolKit.Modules.Hrs.Devices.Ble;

internal enum BroadcastNote
{
    None,

    /// <summary>最初の 1 通。中身をそのまま書く（読めない機器を持ち込まれたときの手がかり）。</summary>
    First,
    Unsupported,
}
