namespace SpoitableToolKit.Modules.Hrs.Panel;

/// <summary>心拍計タブの行の「接続」（<c>useMonitor</c>）で届くもの: その行の心拍計の名前とアドレス（<c>AA:BB:CC:DD:EE:FF</c>）。</summary>
internal sealed record HrsUseMonitorRequest(string? Name, string? Address);
