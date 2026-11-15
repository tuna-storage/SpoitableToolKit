namespace SpoitableToolKit.Modules.Hrs.Devices.Models;

/// <summary>パネルへ流す名前。表示のしかた（「BLE (GATT)」など）はパネル側に置く。</summary>
internal static class HeartRateConnectionNames
{
    public static string ToWireName(this HeartRateConnection connection) => connection switch
    {
        HeartRateConnection.BleGatt => "bleGatt",
        HeartRateConnection.BleBroadcastPolar => "bleBroadcastPolar",
        HeartRateConnection.Hds => "hds",
        _ => "none",
    };
}
