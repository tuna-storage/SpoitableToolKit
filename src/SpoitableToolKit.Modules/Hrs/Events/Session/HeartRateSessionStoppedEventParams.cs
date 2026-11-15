namespace SpoitableToolKit.Modules.Hrs.Events.Session;

/// <param name="Closing">受けるのをやめた（周回を畳んだ）か。false なら、黙った・計れていない（心拍が戻ればまた送り始める）。</param>
internal sealed record HeartRateSessionStoppedEventParams(bool Closing);
