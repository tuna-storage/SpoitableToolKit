using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Modules.Hrs.Devices;
using SpoitableToolKit.Modules.Hrs.Devices.Interfaces;

namespace SpoitableToolKit.Modules.Hrs.Events.HeartRate;

/// <summary>
/// 心拍の窓口（<see cref="HeartRateManager"/>）が配る出来事を受け取る。足す先は窓口の <see cref="HeartRateManager.Listeners"/>:
/// <code>
/// service.Listeners.AddListener&lt;IHeartRateEventListener&gt;(module, session);
/// </code>
/// <para>
/// <b>約束</b>: <see cref="OnConnected"/> と <see cref="OnDisconnected"/> は必ず対になって交互に来る。<see cref="OnHeartBeat"/> はその間にだけ来る。
/// <see cref="OnSignalLost"/> と <see cref="OnSignalBack"/> も繋がりの間にだけ、交互に来る（心拍なしを閉じるのは <see cref="OnSignalBack"/> か <see cref="OnDisconnected"/>）。
/// 心拍なしの間は <see cref="OnHeartBeat"/> は来ない — 戻るときは <see cref="OnSignalBack"/> が先。まだ 1 拍も来ていない繋がりでは <see cref="OnSignalLost"/> を出さない。
/// どれも知らせたスレッド（BLE・HDS・見張りの時計）の上で、窓口が積んだ順に 1 つずつ呼ぶ（<see cref="HeartRateDispatch"/>）。
/// </para>
/// </summary>
internal interface IHeartRateEventListener : IEventListener
{
    /// <summary>心拍を受けられるようになった（GATT の線が上がった・広告が読めた・時計から届き始めた・別の心拍計へ乗り換えた）。</summary>
    void OnConnected(HeartRateConnectedEventParams e)
    {
    }

    /// <summary>
    /// 受けられなくなった。<see cref="OnConnected"/> と必ず対になる。別の心拍計の 1 拍で乗り換えたときは理由が <see cref="HeartRateDisconnectReason.Replaced"/> で、
    /// すぐ次の <see cref="OnConnected"/> と <see cref="OnHeartBeat"/> が来る。
    /// </summary>
    void OnDisconnected(HeartRateDisconnectedEventParams e)
    {
    }

    /// <summary>1 拍。<see cref="OnConnected"/> と <see cref="OnDisconnected"/> の間にだけ来る。0 は「繋がっているが計れていない」（肌から離れた）。</summary>
    void OnHeartBeat(HeartBeatEventParams e)
    {
    }

    /// <summary>
    /// 繋がったまま心拍が来なくなった（GATT の線は上がっているが充電器の上・広告の受け方がまだ諦めていない）。黙った長さは入口が決める
    /// （<see cref="IHeartRateDevice.SilenceAfter"/>）。時計（HDS）は黙ると繋がりごと消えるので、ふつうはこちらではなく <see cref="OnDisconnected"/>（<see cref="HeartRateDisconnectReason.Silent"/>）
    /// — ちょうど黙り切った瞬間に次の 1 拍が届くと、時計でも心拍なし → 戻ったが続けて来ることがある（並びの約束は崩れない）。
    /// </summary>
    void OnSignalLost(HeartRateSignalLostEventParams e)
    {
    }

    /// <summary>心拍なしのあと、また届き始めた。すぐ次に <see cref="OnHeartBeat"/> が来る。</summary>
    void OnSignalBack(HeartRateSignalBackEventParams e)
    {
    }
}
