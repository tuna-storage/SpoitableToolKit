using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Modules.Hrs.Avatar;

namespace SpoitableToolKit.Modules.Hrs.Events.Session;

/// <summary>アバターへ「動いていない」を送った（<see cref="HeartRateSession"/>）を受け取る。受けるのはモジュール（配信へ 0 を流す）。</summary>
internal interface IHeartRateSessionEventListener : IEventListener
{
    void OnStopped(HeartRateSessionStoppedEventParams e)
    {
    }
}
