namespace SpoitableToolKit.Modules.Hrs.Recording;

/// <summary>記録の要約。パネルに「どれだけ貯まっているか」を出すため。</summary>
internal readonly record struct RecordSummary(long Count, DateTimeOffset? First, DateTimeOffset? Last);
