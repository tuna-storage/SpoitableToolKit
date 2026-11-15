using Microsoft.Extensions.Logging;
using SpoitableToolKit.Modules.Hrs.Broadcast;
using SpoitableToolKit.Modules.Hrs.Devices.Models;
using SpoitableToolKit.Modules.Hrs.Recording;

namespace SpoitableToolKit.Modules.Hrs.Devices;

/// <summary>
/// 繋がり（どの入口から・どの機器）が変わったら書く。BLE と HDS の乗り換え（どちらが心拍を運んでいるか）もここに出る。
/// <para>
/// <b>新しく繋がったら最小 / 最大 / 平均を 0 から数え直す</b>（<see cref="HeartRateLog.RestartStats"/>）。
/// 入口はどれも<b>繋がった印（<c>Connected</c>）を受け口より先に立てる</b>ので、1 拍目がどの順で来ても
/// 数え直しはその 1 拍目より前に入る（受け口が先だと、数えた 1 拍目を後から数え直しで消す）。
/// 切れたときは数え直さない — 次に繋がるまでは、前の繋がりの数字を見せておく。
/// </para>
/// </summary>
internal sealed class HeartRateConnectionWatch(
    Func<HeartRateManager?> source,
    HeartRateLog log,
    HeartRateAnnouncer announcer,
    Func<ILogger> logger)
{
    /// <summary>最後に書いた繋がり。<b>変わったときだけ書く</b>（同じ知らせが入口と調停の両方から来る）。</summary>
    private (MonitorIdentity? Device, HeartRateConnection Connection) _noted;

    private readonly Lock _noteGate = new();

    /// <summary>繋がりを見直して、変わっていれば書く（繋がった・切れたの知らせのたびに呼ぶ）。</summary>
    public void Note()
    {
        (MonitorIdentity? Device, HeartRateConnection Connection) was;
        MonitorIdentity? device;
        HeartRateConnection connection;

        lock (_noteGate)
        {
            // **読むのもロックの中で。** 知らせは BLE と HDS の別スレッドから来るので、外で読むと
            // 古い方が後から書いて、繋がりを取り違えたまま残る。
            device = source()?.Connected;
            connection = source()?.Connection ?? HeartRateConnection.None;

            if (_noted == (device, connection)) return;

            was = _noted;
            _noted = (device, connection);

            // 数え直しもロックの中で。2 つの入口の知らせが前後しても、最後に決まった繋がりの数え直しが最後に入る。
            if (device is not null) log.RestartStats();
        }

        // ダッシュボードの札も同じ数字にそろえる（次の 1 拍を待つと、その間だけ前の繋がりの最小・最大が残る）。
        if (device is not null) announcer.ShowStats(log.Overall);

        if (device is { } now)
            logger().LogInformation("Heart rate source: {Device} over {Connection}", now.ToSuggestion(), connection);
        else if (was.Device is { } lost)
            logger().LogInformation("Heart rate source: lost {Device} (was over {Connection})", lost.ToSuggestion(), was.Connection);
    }
}
