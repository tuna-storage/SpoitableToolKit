namespace SpoitableToolKit.Modules.Hrs.Recording;

/// <summary>統計タブへ返す範囲の点（<c>{"points":[[t,bpm],…]}</c>）。<c>t</c> は UTC のエポックミリ秒。</summary>
internal sealed record HeartRateRecordPoints(IEnumerable<long[]> Points);
