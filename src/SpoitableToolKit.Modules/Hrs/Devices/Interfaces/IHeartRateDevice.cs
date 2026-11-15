using SpoitableToolKit.Modules.Hrs.Devices.Models;

namespace SpoitableToolKit.Modules.Hrs.Devices.Interfaces;

/// <summary>
/// 心拍計 1 台。見えているだけのもの（BLE の広告で見つけた・まだ繋いでいない）も同じ型で、繋がっていなければ
/// <see cref="Connection"/> が <see cref="HeartRateConnection.None"/>。見つける係（<see cref="IHeartRateDeviceDiscovery"/>）が作って、
/// 窓口へ差し出す（<see cref="IHeartRateDeviceSink"/>）。新しい種類を足すときに書くのは、これと見つける係の 1 組。
/// <para>
/// 繋ぎに行く・切れたら繋ぎ直すのは<b>見つける係の仕事</b>で、ここには出さない（BLE は見えている中から 1 台を選んで繋ぐ —
/// 選ぶのは設定の指定）。ここで出すのは手で切る・繋ぐ（パネルの「切断」「接続」）だけ。
/// </para>
/// </summary>
internal interface IHeartRateDevice
{
    /// <summary>どの心拍計か。BLE はアドレスで、時計（HDS）は送り主の名前で見分ける（<see cref="MonitorIdentity.IsSame"/>）。BLE は繋いだあとで名乗り直すことがあるので、名前は変わってよい。</summary>
    MonitorIdentity Identity { get; }

    /// <summary>
    /// どう繋がっているか。繋がっていなければ <see cref="HeartRateConnection.None"/>。<b>窓口は「繋がっているか」もこれ 1 つで読む</b> —
    /// 2 つに分けると、読む間に切れたとき食い違う（繋がっているのに None、など）。
    /// </summary>
    HeartRateConnection Connection { get; }

    /// <summary>繋がっているか（<see cref="Connection"/> が None でない）。</summary>
    bool IsConnected => Connection != HeartRateConnection.None;

    /// <summary>電池（%）。<b>GATT で繋がっているときだけ</b>分かる（広告と時計からは読めない）。分からなければ null。</summary>
    int? Battery { get; }

    /// <summary>
    /// これだけ黙ったら「止まった」とみなす長さ。<b>種類ごとに違う</b> — BLE は 1 秒おきに来るが、HealthKit は 5 秒おきで、手首を下ろすと数十秒止まる。
    /// </summary>
    TimeSpan SilenceAfter { get; }

    /// <summary>
    /// 手で切る（パネルの「切断」。窓口が呼ぶ）。<see cref="Connect"/> まで、この 1 台には<b>勝手に繋ぎ直さない</b>（ほかの心拍計は受け続ける）。
    /// <b>すぐ戻る</b> — 線を放すのは見つける係の都合でよく、窓口は呼ぶ前にこの 1 台を「繋がっていない」扱いにしている（放し終わるまでに届く 1 拍は窓口が捨てる）。
    /// </summary>
    void Disconnect();

    /// <summary>手で切ったのを外す（パネルの「接続」）。切っていなければ何もしない。すぐ戻る（繋ぎに行くのは見つける係の次の周回）。</summary>
    void Connect();
}
