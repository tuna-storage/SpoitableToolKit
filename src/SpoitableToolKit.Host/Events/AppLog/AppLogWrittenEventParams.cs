using SpoitableToolKit.Host.AppLog;

namespace SpoitableToolKit.Host.Events.AppLog;

internal sealed record AppLogWrittenEventParams(AppLogEntry Entry);
