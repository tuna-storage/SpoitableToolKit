using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Modules.Hrs.Config;
using SpoitableToolKit.Modules.Hrs.Devices;
using SpoitableToolKit.Modules.Hrs.Recording;

namespace SpoitableToolKit.Modules.Hrs.Broadcast;

/// <summary>
/// サイドバーの札（<see cref="ModuleStatus"/>）と配信へ、同じ数字を出す。
/// 最小 / 最大 / 平均はこの繋がりの数字で、繋がるたびに 0 から。
/// </summary>
internal sealed class HeartRateAnnouncer(
    ModuleStatus status,
    HeartRateLog log,
    HrsConfig config,
    Func<IModuleHost?> host,
    Func<HrsBroadcastServer?> broadcast,
    Func<HeartRateManager?> source)
{
    /// <summary>
    /// 心拍計と繋がっていないときの札の字。言語に関係なくこの字（サイドバー・ダッシュボードの札・手首の帯は狭く、言葉で書くと切れる）。
    /// 繋がっているのに計れていない（0 が来た）ときの「— bpm」とは別。
    /// </summary>
    public const string NotConnected = "N/A bpm";

    /// <summary>配信へ 1 通。止まったときこそ送る（送らないとオーバーレイが固まる）。</summary>
    public void Broadcast(int bpm, bool connected)
    {
        // 配信が止まっていてもサイドバーには出したいので、配信より先に書く。
        // 0 は「繋がっているが計れていない」（着けていない）。前の数字を残すと今も計れているように見える。
        var stats = log.Overall;
        status.Text = !connected ? NotConnected : bpm > 0 ? $"{bpm} bpm" : "— bpm";
        status.State = connected && bpm > 0 ? StatusState.Ok : StatusState.Off;
        ShowStats(stats);

        if (broadcast() is not { } server) return;

        server.Publish(new HrsBroadcastState(
            HeartRate: bpm,
            Zone: HrsZones.Of(config, bpm),
            Connected: connected,
            Avg: stats.Avg,
            Min: stats.Min,
            Max: stats.Max,
            Device: source()?.Connected?.Name));
    }

    /// <summary>ダッシュボードの札の 2 行目。まだ 1 拍も無いときは出さない（「最小 0」は嘘になる）。</summary>
    public void ShowStats(HeartRateStats stats) =>
        status.Note = stats.Max > 0 && host() is { } h
            ? h.Text.Format("hrs.status.note", stats.Min, stats.Max)
            : null;
}
