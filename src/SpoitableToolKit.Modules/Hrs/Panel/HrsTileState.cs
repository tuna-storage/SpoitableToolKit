namespace SpoitableToolKit.Modules.Hrs.Panel;

/// <summary>手首のタイルが描く分（<see cref="HrsPanelState"/> と同じ名前の一部）。</summary>
internal sealed record HrsTileState(bool Connected, int HeartRate, HrsPanelSample[] Samples);
