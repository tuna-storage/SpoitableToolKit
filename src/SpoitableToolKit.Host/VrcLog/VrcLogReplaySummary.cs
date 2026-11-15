using SpoitableToolKit.Abstractions.VrcLog;

namespace SpoitableToolKit.Host.VrcLog;

public readonly record struct VrcLogReplaySummary(
    int Lines,
    int Entries,
    IReadOnlyDictionary<string, int> EventCounts,
    IReadOnlyDictionary<string, int> TagCounts,
    IReadOnlyList<VrcLogEventParams> Samples);
