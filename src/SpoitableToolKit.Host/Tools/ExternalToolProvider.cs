using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Abstractions.Tools;
using SpoitableToolKit.Host.App;
using SpoitableToolKit.Host.Config;
using SpoitableToolKit.Host.Settings;

namespace SpoitableToolKit.Host.Tools;

/// <summary>
/// 外部バイナリの取得と鮮度管理。
/// <para>
/// 同梱せず実行時に取ってくる。置き場は <see cref="AppPaths.ToolsDirectory"/> 配下に固定し、
/// キャッシュもそこへ向ける（ツールにユーザープロファイルを散らかさせない）。
/// </para>
/// <para>
/// <b>PATH 上で見つけたものは更新しない。</b> 他人がインストールした物なので、
/// 勝手に入れ替えるのは行儀が悪い。自動更新はホストが持ってきたコピーに限る。
/// </para>
/// </summary>
internal sealed class ExternalToolProvider(
    HostConfig config, ModuleConfigStore configs, INotificationManager notifications, ILocalizer text,
    ILoggerFactory loggerFactory) : IExternalToolProvider
{
    private const string YtDlpDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private const string YtDlpHashUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/SHA2-256SUMS";
    private const string DenoDownloadUrl =
        "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip";

    /// <summary>
    /// 鮮度を見る間隔。yt-dlp は数週間古いだけで弾かれるが、毎回 GitHub を叩く必要は無い。
    /// </summary>
    private static readonly TimeSpan FreshnessInterval = TimeSpan.FromDays(1);

    private readonly ILogger<ExternalToolProvider> _logger = loggerFactory.CreateLogger<ExternalToolProvider>();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ExternalTool> _resolved = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HttpClient Http = CreateHttpClient();

    public async Task<ExternalTool?> GetAsync(string toolId, CancellationToken cancellationToken = default)
    {
        // ツール単位ではなくプロバイダ単位で直列化する。yt-dlp を取りに来た側が
        // その中で deno も要求するので、ツール単位のロックだと絡まる余地が出る。
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_resolved.TryGetValue(toolId, out var already)) return already;

            var tool = toolId switch
            {
                ExternalTools.YtDlp => await EnsureYtDlpAsync(cancellationToken).ConfigureAwait(false),
                ExternalTools.Deno => await EnsureDenoAsync(cancellationToken).ConfigureAwait(false),
                _ => throw new ArgumentException($"Unknown tool: {toolId}", nameof(toolId)),
            };

            if (tool is not null) _resolved[toolId] = tool;
            return tool;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ExternalTool?> EnsureYtDlpAsync(CancellationToken cancellationToken)
    {
        var cache = CacheDirectory(ExternalTools.YtDlp);

        if ((FindOnPath("yt-dlp.exe") ?? FindOnPath("yt-dlp")) is { } onPath)
        {
            _logger.LogInformation("yt-dlp is on PATH ({Path}); not updating it", onPath);
            return new ExternalTool { Id = ExternalTools.YtDlp, ExecutablePath = onPath, CacheDirectory = cache, IsManagedByHost = false };
        }

        var local = Path.Combine(AppPaths.ToolsDirectory, "yt-dlp.exe");

        if (File.Exists(local))
        {
            if (!NeedsFreshnessCheck(ExternalTools.YtDlp))
                return new ExternalTool { Id = ExternalTools.YtDlp, ExecutablePath = local, CacheDirectory = cache, IsManagedByHost = true };

            if (await IsUpToDateAsync(local, cancellationToken).ConfigureAwait(false))
            {
                MarkVerified(ExternalTools.YtDlp);
                return new ExternalTool { Id = ExternalTools.YtDlp, ExecutablePath = local, CacheDirectory = cache, IsManagedByHost = true };
            }

            _logger.LogInformation("yt-dlp is out of date; fetching again");
        }
        else
        {
            _logger.LogInformation("yt-dlp is missing; fetching it");
        }

        if (!await DownloadFileAsync(YtDlpDownloadUrl, local, ExternalTools.YtDlp, cancellationToken).ConfigureAwait(false)) return null;

        MarkVerified(ExternalTools.YtDlp);
        _logger.LogInformation("Placed yt-dlp at {Path}", local);
        return new ExternalTool { Id = ExternalTools.YtDlp, ExecutablePath = local, CacheDirectory = cache, IsManagedByHost = true };
    }

    /// <summary>
    /// deno は鮮度を見ない。YouTube が変わっても JS エンジンは古くならないため
    /// （古くなるのは nsig の解答スクリプト側で、それは yt-dlp が持っている）。
    /// </summary>
    private async Task<ExternalTool?> EnsureDenoAsync(CancellationToken cancellationToken)
    {
        var cache = CacheDirectory(ExternalTools.Deno);

        if ((FindOnPath("deno.exe") ?? FindOnPath("deno")) is { } onPath)
        {
            _logger.LogInformation("deno is on PATH ({Path})", onPath);
            return new ExternalTool { Id = ExternalTools.Deno, ExecutablePath = onPath, CacheDirectory = cache, IsManagedByHost = false };
        }

        var local = Path.Combine(AppPaths.ToolsDirectory, "deno.exe");
        if (File.Exists(local))
            return new ExternalTool { Id = ExternalTools.Deno, ExecutablePath = local, CacheDirectory = cache, IsManagedByHost = true };

        _logger.LogInformation("deno is missing; fetching it (needed to solve yt-dlp's nsig challenge)");

        var archive = local + ".zip";
        var extractTo = Path.Combine(AppPaths.ToolsDirectory, "deno_extract");

        try
        {
            if (!await DownloadFileAsync(DenoDownloadUrl, archive, ExternalTools.Deno, cancellationToken).ConfigureAwait(false)) return null;

            if (Directory.Exists(extractTo)) Directory.Delete(extractTo, recursive: true);
            ZipFile.ExtractToDirectory(archive, extractTo);

            // 公式の zip は直下に deno.exe を置いているが、配置が変わっても壊れないよう探す。
            if (Directory.EnumerateFiles(extractTo, "deno.exe", SearchOption.AllDirectories).FirstOrDefault()
                is not { } produced)
            {
                _logger.LogWarning("The deno zip does not contain deno.exe");
                return null;
            }

            File.Move(produced, local, overwrite: true);
            _logger.LogInformation("Placed deno at {Path}", local);
            return new ExternalTool { Id = ExternalTools.Deno, ExecutablePath = local, CacheDirectory = cache, IsManagedByHost = true };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not set up deno");
            return null;
        }
        finally
        {
            TryDelete(archive);
            TryDeleteDirectory(extractTo);
        }
    }

    /// <summary>
    /// 配布物の SHA-256 と手元のコピーを突き合わせる。
    /// <c>-U</c> を使わないのは、失敗を握り潰したまま古いバイナリで動き続けることがあるため。
    /// <para>
    /// ネットワークが駄目なときは<b>最新扱いにする</b>。手元にあるものを使えなくする方が損。
    /// </para>
    /// </summary>
    private async Task<bool> IsUpToDateAsync(string localPath, CancellationToken cancellationToken)
    {
        try
        {
            var localHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(localPath))).ToLowerInvariant();
            var sums = await Http.GetStringAsync(YtDlpHashUrl, cancellationToken).ConfigureAwait(false);

            foreach (var line in sums.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // "<hash>  yt-dlp.exe" の形。yt-dlp_linux / yt-dlp_macos などは弾く。
                if (!line.EndsWith("yt-dlp.exe", StringComparison.OrdinalIgnoreCase)) continue;

                var expected = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                var same = string.Equals(localHash, expected, StringComparison.OrdinalIgnoreCase);
                if (!same) _logger.LogDebug("yt-dlp hash mismatch local={Local} remote={Remote}", localHash, expected);
                return same;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug("Could not check freshness (using the local copy): {Message}", ex.Message);
            return true;
        }
    }

    /// <summary>
    /// 取ってくる。<b>初回起動でここに入ると、deno は 40MB 近くあるので数十秒かかる</b> —
    /// 進み具合を出さないと、アプリが固まったようにしか見えない。
    /// </summary>
    /// <param name="label">画面に出す名前（<c>yt-dlp</c> / <c>deno</c>）。</param>
    private async Task<bool> DownloadFileAsync(
        string url, string destination, string label, CancellationToken cancellationToken)
    {
        var partial = destination + ".part";

        using var toast = notifications.PushToast(NotifyKind.Download, text.Format("host.tools.downloading", label));

        // **長さが分かる前に時計を止める。** 分からないまま既定の秒数で消えると、
        // 取得の途中で通知だけ消えて「何も起きていない」に戻ってしまう。
        toast.Progress = 0;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            using var response = await Http
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            // GitHub は長さを付けてこないことがある（chunked）。そのときは進み具合の代わりに
            // いくつ取れたかを出す。**どちらにしても、終わるまで消えない。**
            var total = response.Content.Headers.ContentLength;

            // 途中で落ちたものを本命の名前に置かない。次回「あるのに壊れている」状態を作らないため。
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var file = File.Create(partial))
            {
                await CopyWithProgressAsync(source, file, total, toast, cancellationToken).ConfigureAwait(false);
            }

            File.Move(partial, destination, overwrite: true);

            // 終わったことを見せてから消える（1 に達したあとの Dispose は効かない）。
            toast.Title = text.Format("host.tools.downloaded", label);
            toast.Body = null;
            toast.Progress = 1;
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not fetch {Url}", url);
            return false;
        }
        finally
        {
            TryDelete(partial);
        }
    }

    // 確認時刻はホスト本体の設定に置く（他のホスト設定と同じ config.json）。
    // 読み書きは必ず _gate の中から呼ばれる（GetAsync がプロバイダ単位で直列化している）ので、
    // 辞書に追加のロックは要らない。
    private bool NeedsFreshnessCheck(string toolId) =>
        !config.ToolVerifiedAt.TryGetValue(toolId, out var last)
        || !DateTimeOffset.TryParse(last, out var when)
        || DateTimeOffset.UtcNow - when > FreshnessInterval;

    private void MarkVerified(string toolId)
    {
        // 辞書は差し替える。書き換えると、同じ設定を別スレッドが書き出している最中
        // （ログ画面のチップなど）に列挙が壊れる。JS 側は戻り値を待たないので、
        // その例外は画面には何も出ない。
        config.ToolVerifiedAt = new Dictionary<string, string>(config.ToolVerifiedAt)
        {
            [toolId] = DateTimeOffset.UtcNow.ToString("O"),
        };

        configs.Save(HostConfig.Id);
    }

    private static string CacheDirectory(string toolId)
    {
        var path = Path.Combine(AppPaths.ToolsDirectory, "cache", toolId);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// 写しながら進み具合を出す。<b>長さが分かるときは百分率、分からないときは MB。</b>
    /// <para>
    /// ここだけ切り出してあるのは、<b>取得の本体がネットワーク越しで試験に掛けられない</b>ため。
    /// 進み具合の勘定はここに閉じているので、こちらは掛けられる。
    /// </para>
    /// <para>
    /// 送る回数はホスト側が抑える（百分率が動いたときだけ）。長さが分からない側は
    /// MB を整数で出すので、40MB でも 40 回に収まる。
    /// </para>
    /// </summary>
    internal static async Task CopyWithProgressAsync(
        Stream source, Stream destination, long? total, IToast toast, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long read = 0;

        while (await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) is var n and > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
            read += n;

            if (total is > 0) toast.Progress = (double)read / total.Value;
            else toast.Body = $"{read / (1024 * 1024)} MB";
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SpoitableToolKit/1.0");
        return http;
    }

    private static string? FindOnPath(string fileName)
    {
        if (Environment.GetEnvironmentVariable("PATH") is not { } pathVariable) return null;

        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // PATH に壊れた要素が混ざっていても走査は続ける。
            }
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
