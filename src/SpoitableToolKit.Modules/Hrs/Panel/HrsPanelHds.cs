namespace SpoitableToolKit.Modules.Hrs.Panel;

/// <summary>
/// 「心拍計」タブの Apple Watch (HDS) の欄。<b>HDS の受信が ON の間だけ</b>渡す（OFF なら渡さない = 欄ごと隠す）。
/// 時計のアプリに入れる宛先を並べる。空なら「LAN のアドレスが見つかりません」。
/// </summary>
internal sealed record HrsPanelHds(HrsPanelHdsAddress[] Addresses);
