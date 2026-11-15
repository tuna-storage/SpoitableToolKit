namespace SpoitableToolKit.Modules.Hrs.Broadcast;

/// <summary>配信に載せる 1 通ぶん。</summary>
/// <param name="Connected">心拍が届いているか（心拍計と繋がっているかではない）。ベルトが繋がったまま黙っている間は数字が生きていない。false も送らないとオーバーレイが固まる。</param>
internal readonly record struct HrsBroadcastState(
    int HeartRate, string Zone, bool Connected, int Avg, int Min, int Max, string? Device = null);
