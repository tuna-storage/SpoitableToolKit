namespace SpoitableToolKit.Modules.Hrs.Events.HeartRate;

/// <param name="Bpm">1 分あたりの拍数。0 は「繋がっているが計れていない」。</param>
internal sealed record HeartBeatEventParams(int Bpm) : HeartRateEventParams;
