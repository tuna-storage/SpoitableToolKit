using SpoitableToolKit.Modules.Hrs.Events.HeartRate;

namespace SpoitableToolKit.Modules.Hrs.Devices.Models;

/// <summary>
/// 心拍計の状態。<b>持つのは窓口</b>（<see cref="HeartRateManager"/>）。
/// パネルの「受信中」「接続済み・心拍なし」「切断してあります」は、ここから読む。
/// </summary>
internal enum HeartRateDeviceStatus
{
    /// <summary>繋がっていない（探している・圏外・時計が黙って消えた）。</summary>
    Disconnected,

    /// <summary>
    /// 繋がっているが、心拍が来ていない — 繋がった直後でまだ 1 拍も無い・届いていたが黙った（充電器の上・肌から浮いた）。
    /// <b>繋がった（<see cref="HeartRateConnectedEventParams"/>）はこの状態で出る</b>（1 拍が届いて初めて <see cref="Connected"/>）。
    /// 0 bpm は「届いた」に数える（入口は喋っている）。
    /// </summary>
    NoHeartRate,

    /// <summary>繋がっていて、心拍が届いている（1 拍が届き、そのあと黙り切っていない）。</summary>
    Connected,

    /// <summary>パネルの「切断」で切った。「接続」を押すまで繋ぎ直さない（遅れて届く 1 拍も捨てる）。覚えるのはメモリだけ。</summary>
    StoppedByUser,
}
