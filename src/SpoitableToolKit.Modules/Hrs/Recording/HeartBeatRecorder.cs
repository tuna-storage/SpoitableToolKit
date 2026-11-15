using Microsoft.Extensions.Logging;
using SpoitableToolKit.Modules.Hrs.Broadcast;
using SpoitableToolKit.Modules.Hrs.Config;
using SpoitableToolKit.Modules.Hrs.Devices;

namespace SpoitableToolKit.Modules.Hrs.Recording;

/// <summary>
/// 1 拍を受けて、グラフ（<see cref="HeartRateLog"/>）・記録（<see cref="HeartRateRecords"/>）・札と配信（<see cref="HeartRateAnnouncer"/>）・パネルへ渡す。
/// <b>BLE のスレッドから呼ばれる。</b>
/// </summary>
internal sealed class HeartBeatRecorder(
    HeartRateLog log,
    HrsConfig config,
    Func<HeartRateRecords?> records,
    Func<HeartRateManager?> source,
    Func<ILogger> logger,
    HeartRateRecordQueryService query,
    HeartRateAnnouncer announcer,
    Action pushStatus)
{
    private bool _reportedZero;

    /// <summary>心拍が 1 つ届いた。<b>BLE のスレッドから呼ばれる。</b></summary>
    public void Record(int bpm)
    {
        // **0 は記録しない。** 心拍計は肌から離れると 0 を送ってくる。
        // 記録・最小/平均に混ぜると「最小 0」になって通算が壊れるので、表示を「計れていない」にし、グラフに 0 の点を 1 つ残すだけ。
        if (bpm <= 0)
        {
            // グラフには 0 を残す（切れたときと同じ。最小・平均・最大と記録には入れない — HeartRateLog.Gap）。
            log.Gap(DateTimeOffset.Now);

            // **1 回の「計れていない」につき 1 回だけ書く**（毎秒来る）。
            if (!_reportedZero)
            {
                _reportedZero = true;
                logger().LogInformation("Heart rate source: {Device} reports 0 bpm (not touching the skin?)",
                    source()?.Connected?.ToSuggestion());
            }

            announcer.Broadcast(0, connected: true);
            pushStatus();
            return;
        }

        var at = DateTimeOffset.Now;

        if (_reportedZero)
        {
            _reportedZero = false;
            logger().LogInformation("Heart rate source: reading again ({Bpm} bpm)", bpm);
        }

        log.Add(bpm, at);

        // **間引かずに 1 件ずつ入れる。** 積むだけなので、ここで disk を待つことはない。
        if (config.RecordingEnabled)
        {
            records()?.Write(bpm, at);

            // **0 件から 1 件になる瞬間だけ数え直す。** ここを拾わないと、パネルを開いたまま
            // 心拍計を繋いだ人は「まだ 1 件も記録されていません」のまま書き出せない
            // （数え直すのは開いた時と書き出した後だけなので、閉じて開くまで気付けない）。
            // 1 件でも入れば止まるので、毎拍 DB を数えることにはならない。
            if (query.Summary.Count == 0) query.Refresh();
        }

        announcer.Broadcast(bpm, connected: true);
        pushStatus();
    }
}
