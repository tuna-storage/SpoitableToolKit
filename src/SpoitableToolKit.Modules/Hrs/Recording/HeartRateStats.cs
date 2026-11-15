namespace SpoitableToolKit.Modules.Hrs.Recording;

/// <summary>この繋がりの最小 / 最大 / 平均。1 拍も来ていなければ全部 0。</summary>
internal readonly record struct HeartRateStats(int Min, int Max, int Avg);
