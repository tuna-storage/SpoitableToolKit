namespace SpoitableToolKit.Modules.Hrs.Panel;

/// <summary>グラフの 1 点。<paramref name="At"/> は ISO 8601（<c>"O"</c>）。</summary>
internal sealed record HrsPanelSample(int Bpm, string At);
