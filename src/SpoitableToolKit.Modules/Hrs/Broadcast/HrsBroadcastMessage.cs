namespace SpoitableToolKit.Modules.Hrs.Broadcast;

/// <summary>
/// 配信の 1 通（WebSocket に流す JSON の形そのもの）。<b>形は OBS のオーバーレイや外部ツールが読んでいるので、変えると読めなくなる</b> —
/// 名前と並びはこの宣言の順（camelCase）、null は書かない。パネルの配信タブのプレビューへも、同じ形のまま中継する。
/// </summary>
/// <param name="Type"><see cref="Update"/> か <see cref="Welcome"/>。</param>
/// <param name="History">直近の心拍（0 を除く）。<see cref="Welcome"/> にだけ付ける（付けないとグラフが埋まるまで 1 分かかる）。</param>
internal sealed record HrsBroadcastMessage(
    string Type, int Bpm, string Zone, bool Connected, int Avg, int Min, int Max, string? Device, IReadOnlyList<int>? History)
{
    /// <summary>1 拍ごと・繋がりが変わったとき。</summary>
    public const string Update = "hr_update";

    /// <summary>繋いできた相手へ最初に渡す 1 通（いまの様子と直近の心拍）。</summary>
    public const string Welcome = "welcome";

    public static HrsBroadcastMessage Of(HrsBroadcastState state, string type, IReadOnlyList<int>? history) => new(
        type, state.HeartRate, state.Zone, state.Connected, state.Avg, state.Min, state.Max, state.Device,
        history is { Count: > 0 } ? history : null);
}
