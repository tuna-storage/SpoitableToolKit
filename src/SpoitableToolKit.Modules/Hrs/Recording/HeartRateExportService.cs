using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Notifications;

namespace SpoitableToolKit.Modules.Hrs.Recording;

/// <summary>記録を CSV に出す（パネルの「書き出し」）。<b>欲しいときだけ</b>（常時 2 か所へ書くと正が 2 つになる）。</summary>
internal sealed class HeartRateExportService(
    Func<HeartRateRecords?> records,
    Func<IModuleHost?> host,
    Func<ILogger> logger,
    HeartRateRecordQueryService query,
    Action pushStatus)
{
    private static readonly NotifyKind ExportedKind = new("hrs.exported", NotifyTone.Green,
        "M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8ZM14 3v5h5M9 14.5l2 2 4-4");

    /// <summary>最後に出した CSV。出した先が分からないと、出した人は探しに行けない。</summary>
    public string? LastExport { get; private set; }

    /// <summary>
    /// CSV に出す。範囲は<c>{"hours":24}</c>（0 なら全部）か、<c>{"from":…,"to":…}</c>
    /// （UTC のエポックミリ秒。統計タブの「いま見ている範囲」）。両方あれば from / to が勝つ。
    /// <para>
    /// ファイル名は<b>出した時刻</b>のまま（<c>hr_yyyyMMdd-HHmmss.csv</c>）。範囲の終わりで名付けると、
    /// 過去の同じ日を 2 回出したときにぶつかる。
    /// </para>
    /// <para>
    /// <b>書き出しは裏で。</b> 何時間ぶんも出すと数秒かかるので、押した画面を固めない。
    /// 出した先は push で返す — パスが分からないと、出した人は探しに行けない。
    /// </para>
    /// </summary>
    public Task<string> ExportCsv(string payload)
    {
        if (records() is not { } store || host() is not { } moduleHost) return Task.FromResult("{}");

        var hours = 0;
        try
        {
            if (JsonDocument.Parse(payload).RootElement.TryGetProperty("hours", out var value))
                hours = value.GetInt32();
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            // 範囲が読めなければ全部出す。押した人の意図から外れる方向ではない。
            logger().LogDebug(ex, "Could not read the export range; exporting everything");
        }

        var exportedAt = DateTimeOffset.Now;
        var (from, to) = HeartRateRecordQueryService.ReadRange(payload)
            ?? (hours > 0 ? exportedAt.AddHours(-hours) : DateTimeOffset.UnixEpoch, exportedAt);

        _ = Task.Run(async () =>
        {
            var path = Path.Combine(
                moduleHost.DataDirectory, "exports", $"hr_{exportedAt.ToLocalTime():yyyyMMdd-HHmmss}.csv");

            try
            {
                var rows = await store.ExportAsync(from, to, path).ConfigureAwait(false);

                LastExport = path;
                logger().LogInformation("Exported {Rows} heart rate row(s) to {Path}", rows, path);

                // 書き出しは裏で走るので、**パネルを見ていないと終わったことに気付けない**。
                // 出来たファイルは開けないと意味がないので、そこまで持っていく。
                moduleHost.Notifications.PushToast(
                    ExportedKind, moduleHost.Text.Format("hrs.toast.exported", rows), path,
                    new NotifyAction(moduleHost.Text["hrs.toast.openFolder"], () => Reveal(path)),

                    // Excel の「開く」に貼るにも、人に渡すにも要る。
                    new NotifyAction(moduleHost.Text["hrs.toast.copyPath"], () => moduleHost.Clipboard.SetText(path)));
            }
            catch (Exception ex)
            {
                logger().LogWarning(ex, "Could not export the CSV");
                LastExport = null;
            }

            query.Refresh();
            pushStatus();
        });

        return Task.FromResult("{}");
    }

    /// <summary>
    /// エクスプローラーで<b>そのファイルを選んだ状態</b>で開く。フォルダだけ開くと、
    /// 書き出しを続けたときにどれが今のものか分からない。
    /// </summary>
    private void Reveal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger().LogWarning(ex, "Could not open {Path}", path);
        }
    }
}
