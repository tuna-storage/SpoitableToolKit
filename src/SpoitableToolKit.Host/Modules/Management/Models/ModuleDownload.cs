namespace SpoitableToolKit.Host.Modules.Management.Models;

/// <summary>
/// リリースの zip を落としている途中の様子（画面の進み表示用）。<paramref name="Total"/> は GitHub が名乗った大きさ
/// （repo の持ち主が書ける値ではないが、当てにしすぎない: 0 のことも、落とした量が超えることもありうる）。
/// </summary>
internal sealed record ModuleDownload(string Tag, string Asset, long Bytes, long Total);
