using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Tools;

namespace SpoitableToolKit.Modules.VideoUrl.Resolving;

/// <summary>手元の yt-dlp を走らせる（設定の「サーバー」を使わないとき）。<b>1 回あたり 60 秒まで</b>。</summary>
internal sealed class YtDlpRunner(ILogger logger)
{
    /// <summary>yt-dlp 1 回あたりの上限。実測 3 秒前後なので、これを超えるのは詰まっている。</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// yt-dlp を <c>-J</c> で 1 回走らせて JSON を返す。失敗したら null。
    /// </summary>
    public async Task<JsonDocument?> RunJsonAsync(
        ExternalTool ytDlp, ExternalTool? deno, Uri videoUrl, string? playerClient, CancellationToken cancellationToken)
    {
        // --ignore-no-formats-error が要る。-J でも既定のフォーマット選択（bestvideo*+bestaudio/best）が
        // 走るので、それが満たせない動画だと "Requested format is not available" で終了コード 1 になり、
        // 実際には使えるフォーマットがあるのに何も取れなくなる（PoogGcJjsFU / web_safari で実測）。
        List<string> arguments = ["-J", "--ignore-no-formats-error", "--no-warnings"];

        if (playerClient is not null)
        {
            arguments.Add("--extractor-args");
            arguments.Add($"youtube:player_client={playerClient}");
        }

        arguments.Add(videoUrl.AbsoluteUri);

        var output = await RunAsync(ytDlp, deno, [.. arguments], cancellationToken).ConfigureAwait(false);
        if (output is null) return null;

        try
        {
            return JsonDocument.Parse(output);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Could not read yt-dlp's output as JSON");
            return null;
        }
    }

    /// <summary>
    /// yt-dlp を 1 回走らせる。標準出力を返し、失敗したら null。
    /// <para>
    /// <c>--cache-dir</c> と <c>DENO_DIR</c> を必ずこちらの置き場へ向ける
    /// （既定だとユーザープロファイル直下に作られる）。
    /// </para>
    /// </summary>
    private async Task<string?> RunAsync(
        ExternalTool ytDlp, ExternalTool? deno, string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ytDlp.ExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("--cache-dir");
        startInfo.ArgumentList.Add(ytDlp.CacheDirectory);

        if (deno is not null)
        {
            startInfo.ArgumentList.Add("--js-runtimes");
            startInfo.ArgumentList.Add($"deno:{deno.ExecutablePath}");
            startInfo.Environment["DENO_DIR"] = deno.CacheDirectory;
        }

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        using var process = Process.Start(startInfo);
        if (process is null) return null;

        try
        {
            // 両方の口を同時に読む。stderr が詰まると stdout ごと止まるため。
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            var output = await stdout.ConfigureAwait(false);
            var error = await stderr.ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            if (process.ExitCode == 0) return output;

            logger.LogWarning("yt-dlp exited with {ExitCode}: {Error}", process.ExitCode, LogText.Trim(error));
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("yt-dlp did not finish within {Seconds}s", Timeout.TotalSeconds);
            TryKill(process);
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
