using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Host.VrcLog;

namespace SpoitableToolKit.Host.Events.VrcLog;

/// <summary>Host の中だけ。既定の実装は置かない。</summary>
internal interface ILogFileEventListener : IEventListener
{
    void OnLogFileChanged(LogFileChangedEventParams e);
}
