namespace SpoitableToolKit.Modules.Hrs.Events.HeartRate;

/// <summary>心拍計が切れた理由。</summary>
internal enum HeartRateDisconnectReason
{
    /// <summary>入口が「繋がっていない」と言った（GATT の線が落ちた・圏外・電池切れ）。</summary>
    LinkLost,

    /// <summary>持ち主だった入口が黙った（窓口が手放した）。</summary>
    Silent,

    /// <summary>利用者がパネルの「切断」でその心拍計を切った（「接続」を押すまで、その 1 台には繋ぎ直さない）。</summary>
    ByUser,

    /// <summary>
    /// 別の心拍計（入口）の 1 拍で乗り換えた。すぐ次の <see cref="HeartRateConnectedEventParams"/> と <see cref="HeartBeatEventParams"/> が来る（受け手はアバターへ「繋がっていない」を送らなくてよい）。
    /// 黙って手放した先・線が変わった先へ移るときは、まだ 1 拍も無いので乗り換えではなく、前のが切れた理由（<see cref="Silent"/>・<see cref="LinkLost"/>）。
    /// </summary>
    Replaced,

    /// <summary>受けるのをやめた（設定を変えて作り直す・何も繋がっていないときのパネルの「切断」・アプリを閉じる）。</summary>
    Shutdown,
}
