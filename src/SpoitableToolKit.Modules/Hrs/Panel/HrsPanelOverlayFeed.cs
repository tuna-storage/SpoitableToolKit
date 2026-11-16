using SpoitableToolKit.Modules.Hrs.Broadcast;

namespace SpoitableToolKit.Modules.Hrs.Panel;

/// <summary>
/// 配信タブのプレビューへ中継する分: 最後に配信した 1 通（<see cref="HrsBroadcastMessage.Update"/> の形のまま）と、それが何通目か。
/// パネルは番号が変わったときだけプレビューへ渡す（push は毎拍以外にも来るので、渡し直すとグラフに同じ点が積もる）。
/// </summary>
internal sealed record HrsPanelOverlayFeed(long Sent, HrsBroadcastMessage Latest);
