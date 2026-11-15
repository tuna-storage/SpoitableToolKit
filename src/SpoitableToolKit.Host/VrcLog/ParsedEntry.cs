using SpoitableToolKit.Abstractions.VrcLog;

namespace SpoitableToolKit.Host.VrcLog;

/// <summary>確定した 1 エントリ。<see cref="Event"/> は意味づけできた場合のみ入る。</summary>
internal readonly record struct ParsedEntry(VrcLogLine Line, VrcLogEventParams? Event);
