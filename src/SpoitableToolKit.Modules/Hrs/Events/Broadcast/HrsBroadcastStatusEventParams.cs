using SpoitableToolKit.Modules.Hrs.Broadcast;

namespace SpoitableToolKit.Modules.Hrs.Events.Broadcast;

/// <param name="Status">待ち受けの様子。</param>
/// <param name="Port">待ち受けているポート（開き直すまでは古い方）。</param>
/// <param name="Clients">いま繋がっている相手の数。</param>
internal sealed record HrsBroadcastStatusEventParams(HrsBroadcastStatus Status, int Port, int Clients);
