namespace SpoitableToolKit.Modules.Hrs.Panel;

/// <summary>「心拍計」タブを開いている間に読み直す中身（<c>getMonitors</c> の答え）。</summary>
/// <param name="Discovered">見えている心拍計。並びは push の <c>status</c> の <c>discovered</c> と同じ。</param>
/// <param name="Hds">Apple Watch (HDS) の欄。受信が OFF なら null（書かない）。</param>
internal sealed record HrsPanelMonitors(HrsPanelMonitor[] Discovered, HrsPanelHds? Hds);
