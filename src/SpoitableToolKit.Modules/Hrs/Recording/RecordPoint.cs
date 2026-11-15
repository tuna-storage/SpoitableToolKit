namespace SpoitableToolKit.Modules.Hrs.Recording;

/// <summary>記録の 1 件。時刻は UTC のエポックミリ秒（DB の持ち方のまま）。</summary>
internal readonly record struct RecordPoint(long At, int Bpm);
