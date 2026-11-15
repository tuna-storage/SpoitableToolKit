using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Host.AppLog;

namespace SpoitableToolKit.Host.Events.AppLog;

/// <summary>受けるのはアプリの中だけ（画面へ流す <c>UiPump</c>）。</summary>
internal interface IAppLogEventListener : IEventListener
{
    void OnAppLogWritten(AppLogWrittenEventParams e);
}
