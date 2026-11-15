namespace SpoitableToolKit.Modules.Hrs.Recording;

/// <summary>1 つの心拍。</summary>
internal readonly record struct HeartRateSample(int Bpm, DateTimeOffset At);
