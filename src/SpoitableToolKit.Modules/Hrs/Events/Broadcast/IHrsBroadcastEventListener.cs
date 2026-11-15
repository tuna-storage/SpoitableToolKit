using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Modules.Hrs.Broadcast;

namespace SpoitableToolKit.Modules.Hrs.Events.Broadcast;

/// <summary>配信の待ち受けの様子が変わった（<see cref="HrsBroadcastServer"/>）を受け取る。受けるのはモジュール（パネルへ今の様子を流す）。</summary>
internal interface IHrsBroadcastEventListener : IEventListener
{
    void OnStatusChanged(HrsBroadcastStatusEventParams e)
    {
    }
}
