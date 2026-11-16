namespace SpoitableToolKit.Modules.Hrs.Panel;

/// <summary>「心拍計」タブを開いている間に読み直す一覧（<c>getMonitors</c> の答え）。並びは push の <c>status</c> の <c>discovered</c> と同じ。</summary>
internal sealed record HrsPanelMonitors(HrsPanelMonitor[] Discovered);
