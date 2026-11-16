using System.Text.Json;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Modules.Hrs.Avatar;
using SpoitableToolKit.Modules.Hrs.Broadcast;
using SpoitableToolKit.Modules.Hrs.Config;
using SpoitableToolKit.Modules.Hrs.Devices;
using SpoitableToolKit.Modules.Hrs.Devices.Hds;
using SpoitableToolKit.Modules.Hrs.Devices.Models;
using SpoitableToolKit.Modules.Hrs.Recording;

namespace SpoitableToolKit.Modules.Hrs.Panel;

/// <summary>
/// パネルとタイルへ出す現状を組み立てて流す。<b>見るだけ</b> — どの値もほかの係が持っていて、ここは読んで並べるだけ。
/// 正となる状態は全部 C# 側にあり、パネルは開いたときに取り直す（<see cref="GetAsync"/>）。
/// <para>
/// 相手は<b>読むときに引く</b>（<c>Func</c>）。モジュールが立ち上がる途中（窓口や配信が出来る前）や畳んだあとにも、裏の仕事から push が来る。
/// </para>
/// </summary>
internal sealed class HrsPanelStatus(
    HrsConfig config,
    HeartRateLog log,
    Func<IModuleHost?> host,
    Func<HeartRateManager?> source,
    Func<HeartRateSession?> session,
    Func<HrsBroadcastServer?> broadcast,
    HdsFirewallWatch firewall,
    HeartRateRecordQueryService records,
    HeartRateExportService export,
    Func<DateTimeOffset> now)
{
    /// <summary>
    /// パネルへ現状を流す。開いていなければホストが捨てる（best-effort）ので、
    /// 届いたかどうかは気にしない。
    /// </summary>
    public void Push() =>
        host()?.Ui.Push("status",JsonSerializer.Serialize(Build(), HrsPanelJson.Options));

    /// <summary>
    /// 現状を返す。<b>記録の件数は数え終わってから返す。</b> 裏で数えて push で追いつく形だと、その push を取りこぼしたパネルが
    /// 古い件数のまま居座り、書き出しボタンが押せないままになる。
    /// 待つのはパネルを開いた瞬間の 1 回だけで、索引の付いた表を数えるだけなので安い。毎拍は数えない（<see cref="HeartRateRecordQueryService.Refresh"/> は残す）。
    /// </summary>
    public async Task<string> GetAsync()
    {
        await records.CountAsync().ConfigureAwait(false);
        return JsonSerializer.Serialize(Build(), HrsPanelJson.Options);
    }

    /// <summary>手首のタイルの分（<c>getHeartRate</c>）。<b>記録は数えない</b> — タイルはページを送るたびに開き直すので、DB を数えるには多すぎる。</summary>
    public string Tile() => JsonSerializer.Serialize(TileState(), HrsPanelJson.Options);

    /// <summary>
    /// 「心拍計」タブの一覧だけ（<c>getMonitors</c>）。タブを開いている間、パネルが数秒おきに読み直す — 広告は 1 秒に何通も来るので push しない。
    /// <b>記録は数えない</b>（数秒おきに DB を数えるには重い）。
    /// </summary>
    public string Monitors() => JsonSerializer.Serialize(new HrsPanelMonitors(MonitorRows()), HrsPanelJson.Options);

    private HrsPanelState Build()
    {
        var src = source();
        var server = broadcast();
        var summary = records.Summary;

        return new HrsPanelState
        {
            Configured = config.Device,
            Connected = src?.Connected is not null,
            Receiving = src?.Current?.Status == HeartRateDeviceStatus.Connected,
            HeldOff = src?.HeldOff?.ToSuggestion(),
            Connection = (src?.Connection ?? HeartRateConnection.None).ToWireName(),
            Device = src?.Connected?.Name,
            Address = src?.Connected is { Address: not 0 } d ? MonitorIdentity.FormatAddress(d.Address) : null,
            Battery = src?.Battery,
            HeartRate = session()?.HeartRate ?? 0,
            Running = session() is not null,
            HdsEnabled = config.Hds.Enabled,
            HdsPort = config.Hds.Port,
            Firewall = firewall.State.ToString().ToLowerInvariant(),
            FirewallCommand = HdsFirewall.CommandFor(config.Hds.Port),
            Discovered = MonitorRows(),
            Samples = Samples(),
            Stats = log.Overall is { Max: > 0 } overall ? new HrsPanelStats(overall.Min, overall.Avg, overall.Max) : null,

            RecordingEnabled = config.RecordingEnabled,
            RecordCount = summary.Count,
            RecordFirst = summary.First?.ToString("O"),
            RecordLast = summary.Last?.ToString("O"),
            LastExport = export.LastExport,

            WsEnabled = config.Broadcast.Enabled,
            WsPort = server?.Status == HrsBroadcastStatus.Listening ? server.Port : config.Broadcast.Port,
            WsStatus = (server?.Status ?? HrsBroadcastStatus.Disabled).ToString().ToLowerInvariant(),
            WsError = server?.Error,
            WsClients = server?.Clients ?? 0,
            Overlays = HrsBroadcastServer.Overlays,
        };
    }

    private HrsTileState TileState() => new(
        Connected: source()?.Connected is not null,
        HeartRate: session()?.HeartRate ?? 0,
        Samples: Samples());

    private HrsPanelMonitor[] MonitorRows()
    {
        var src = source();
        var connected = src?.Connected;
        var at = now();

        return [.. (src?.Sightings ?? []).Select(seen => HrsPanelMonitor.From(seen, connected, at))];
    }

    private HrsPanelSample[] Samples() => [.. log.Recent.Select(s => new HrsPanelSample(s.Bpm, s.At.ToString("O")))];
}
