using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Modules.Files;
using SpoitableToolKit.Host.Modules.Packaging;

namespace SpoitableToolKit.Host.Modules.Build.Sdk;

/// <summary>
/// 選んだ SDK を<b>落として</b>、SHA-512 を突き合わせ、<c>tools/dotnet</c> に展開する。<b>利用者が同意したあとに呼ぶ</b>（呼ぶ側の仕事）。
/// <b>インストーラもスクリプトも走らせない</b>（zip を展開するだけ。PATH も registry も触らない）。
/// 前の回の残り（落としかけ・展開しかけ・置き換えた古い版）の片付けもここ。
/// </summary>
/// <param name="http">SDK の zip を落とす口。</param>
/// <param name="metadata">一覧の場所（落とす物は、ここと同じホストからだけ取る）。</param>
/// <param name="toolsRoot">道具の置き場（<c>tools</c>）。SDK はその下の <c>dotnet</c> に置く。</param>
/// <param name="logger">SDK の logger（<see cref="DotnetSdks"/> に渡されたもの）。</param>
/// <param name="locator">展開したものが SDK を持つかを確かめる口。</param>
internal sealed class DotnetSdkInstaller(HttpClient http, Uri metadata, string toolsRoot, ILogger logger, DotnetSdkLocator locator)
{
    /// <summary>落とす zip の上限（10.0.404 の実物は 290 MB）。</summary>
    public const long MaxDownloadBytes = 1024L * 1024 * 1024;

    /// <summary>展開したあとの上限（実物は 775 MB・5,578 項目）。モジュールの zip の上限とは別に持つ。</summary>
    public const long MaxExpandedBytes = 3L * 1024 * 1024 * 1024;
    public const int MaxEntries = 30000;

    private readonly SemaphoreSlim _installing = new(1, 1);

    public string Directory => Path.Combine(toolsRoot, "dotnet");

    public string OwnDotnet => Path.Combine(Directory, "dotnet.exe");

    /// <summary>
    /// 落として、SHA-512 を突き合わせ、<c>tools/dotnet</c> に展開する（前に落としたものがあれば置き換える）。<b>利用者が同意したあとに呼ぶ。</b>
    /// 合わなければ何も残さない。
    /// </summary>
    /// <param name="download">落とす物（<see cref="DotnetReleaseList.LatestAsync"/> が選んだもの。形と場所はここでも見直す）。</param>
    /// <param name="cancellationToken">止める口（落としている間と、展開したものを確かめる間）。</param>
    /// <param name="progress">落とした量（バイト）。およそ 1 MB ごとと、落とし終えたときに知らせる（画面の進み表示用）。</param>
    /// <param name="unpacking">落として SHA-512 が合い、展開を始める所で 1 回呼ぶ（ここからは量が出ない。画面は「インストール中」に変える）。</param>
    public async Task<DotnetSdk> InstallAsync(
        DotnetSdkDownload download, CancellationToken cancellationToken, IProgress<long>? progress = null, Action? unpacking = null)
    {
        if (!DotnetReleaseList.VersionShape.IsMatch(download.Version) || !DotnetReleaseList.Sha512Hex.IsMatch(download.Sha512))
            throw new ArgumentException("not a download this class offered", nameof(download));

        // 受け取った「落とす物」も、一覧から選ぶときと同じ決まりで見直す（https・一覧と同じ場所）。
        // 呼ぶ側が別の URL とそれに合うハッシュを渡せると、ハッシュの突き合わせは通ってしまう。
        DotnetDownloadSites.EnsureAllowed(download.Url, metadata);
        if (!DotnetDownloadSites.IsSameSite(download.Url, metadata))
            throw new InvalidOperationException($"Refusing to fetch {download.Url}: the .NET SDK is only downloaded from {metadata.Host}");

        if (!await _installing.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The .NET SDK is already being downloaded");

        var archive = Archive;
        var fresh = Fresh;
        FileStream? held = null;
        try
        {
            if (ModulePaths.IsLink(toolsRoot) || ModulePaths.IsLink(Directory))
                throw new InvalidOperationException($"{toolsRoot} or its dotnet folder is a link; delete the link so that the .NET SDK can be put there");

            System.IO.Directory.CreateDirectory(toolsRoot);

            // ほかのアプリ（2 つ目に起動したもの）が入れている最中なら、触らない（その回の物を消してしまう）。
            held = TryHold() ?? throw new InvalidOperationException(
                "Another copy of the app is downloading the .NET SDK; try again when it has finished");

            // 前の回の残り（展開しかけ・落としかけ・置き換えた古い版で消し切れなかったもの）。消せなければ、そこへは書かない。
            Sweep();
            if (System.IO.Directory.Exists(fresh) || File.Exists(fresh) || System.IO.Directory.Exists(archive) || File.Exists(archive))
                throw new InvalidOperationException(
                    $"{fresh} or {archive} is in the way and could not be removed; delete it so that the .NET SDK can be downloaded");

            string sha;
            await using (var file = File.Create(archive))
                sha = await DownloadAsync(download.Url, file, progress, cancellationToken).ConfigureAwait(false);

            if (!string.Equals(sha, download.Sha512, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"The .NET SDK {download.Version} that was downloaded does not match the SHA-512 in the list of .NET releases; nothing was installed");

            unpacking?.Invoke();
            await using (var zip = File.OpenRead(archive))
                ModulePackage.Expand(zip, fresh, MaxEntries, MaxExpandedBytes, ".NET SDK download");
            File.Delete(archive);

            // **入れ替える前に確かめる**（展開したものが SDK を持つこと）。確かめてから入れ替えると、だめな zip で、動いていた前の SDK を潰さない。
            if (await locator.NewestSdkAsync(Path.Combine(fresh, "dotnet.exe"), cancellationToken).ConfigureAwait(false) is not { } version)
                throw new InvalidDataException(
                    $"The .NET SDK {download.Version} was downloaded and unpacked, but it does not list an SDK that can build modules; nothing was installed");

            // 入れ替え。前のものは、新しいものを置けてから消す。置けなかったら、前のものを戻す。
            string? old = null;
            if (System.IO.Directory.Exists(Directory))
            {
                old = Path.Combine(toolsRoot, "dotnet.old-" + Guid.NewGuid().ToString("N")[..8]);
                System.IO.Directory.Move(Directory, old);
            }

            try
            {
                System.IO.Directory.Move(fresh, Directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (old is not null && !System.IO.Directory.Exists(Directory)) System.IO.Directory.Move(old, Directory);
                throw new IOException($"The .NET SDK {download.Version} could not be put in {Directory} ({ex.Message}); the earlier copy was left as it was");
            }

            if (old is not null) TryDelete(old);

            logger.LogInformation("Installed the .NET SDK {Version} to {Directory} (SHA-512 {Sha})", version, Directory, sha);
            return new DotnetSdk(OwnDotnet, version);
        }
        finally
        {
            // 片付けるのは、自分が入れていたときだけ（印を取れなかったなら、残っているのはほかのアプリの物）。
            if (held is not null)
            {
                TryDeleteFile(archive);
                TryDelete(fresh);
                held.Dispose();
            }

            _installing.Release();
        }
    }

    private string Archive => Path.Combine(toolsRoot, "dotnet-sdk.zip.part");

    /// <summary>展開する先（確かめてから <see cref="Directory"/> と入れ替える）。</summary>
    private string Fresh => Path.Combine(toolsRoot, "dotnet.new");

    /// <summary>
    /// 前の回の残り（落としかけの zip・展開しかけ・置き換えた古い版）を消す。<b>アプリの起動のときに 1 回</b>、裏で。
    /// <para>
    /// 落としている・展開している最中にアプリを閉じると、片付けの前にプロセスが終わって残る（zip だけで 290 MB、展開しかけは 775 MB まで）。
    /// <see cref="InstallAsync"/> も始める前に消すが、それだけだと、二度と落とさない PC ではずっと残る。
    /// <b>落としている最中には消さない</b>（その回の物を消してしまう）。入っている SDK（<see cref="Directory"/>）には触らない。
    /// </para>
    /// </summary>
    public Task ClearLeftoversAsync() => Task.Run(() =>
    {
        if (!_installing.Wait(0)) return;
        try
        {
            if (System.IO.Directory.Exists(toolsRoot) && !ModulePaths.IsLink(toolsRoot))
            {
                // ほかのアプリ（同じ置き場で 2 つ目に起動したもの）が入れている最中なら、消さない。
                using var held = TryHold();
                if (held is not null) Sweep();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not clear what an earlier .NET SDK download left in {Tools}", toolsRoot);
        }
        finally
        {
            _installing.Release();
        }
    });

    /// <summary>
    /// 入れている間の印（<c>tools/.installing</c>。開いたまま、ほかに開かせない。閉じたら消える）。<see cref="_installing"/> は同じプロセスの中しか見えないので、
    /// <b>アプリを 2 つ起動したとき</b>のために、ファイルでも持つ — 無いと、2 つ目の起動の片付けが、1 つ目が展開している途中の物（書き終えて閉じたファイル）を消し、
    /// 欠けた SDK が入れ替わることがある。
    /// </summary>
    private FileStream? TryHold()
    {
        try
        {
            return new FileStream(Path.Combine(toolsRoot, ".installing"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
                FileOptions.DeleteOnClose);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>前の回の残りを消す（<see cref="_installing"/> を持っている間に呼ぶ）。消せないものは残る（入れる前に、呼ぶ側が確かめる）。</summary>
    private void Sweep()
    {
        foreach (var leftover in System.IO.Directory.GetDirectories(toolsRoot, "dotnet.old-*")) TryDelete(leftover);
        TryDelete(Fresh);
        TryDeleteFile(Fresh);
        TryDeleteFile(Archive);
    }

    private const long ProgressStep = 1024 * 1024;

    private async Task<string> DownloadAsync(Uri url, Stream target, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // 転送の先も、同じ決まりで確かめる。
        if (response.RequestMessage?.RequestUri is { } landed) DotnetDownloadSites.EnsureAllowed(landed, metadata);

        if (response.Content.Headers.ContentLength > MaxDownloadBytes)
            throw new InvalidDataException($"The .NET SDK download is larger than {MaxDownloadBytes / 1024 / 1024} MB");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        var buffer = new byte[81920];
        long total = 0;
        long reported = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaxDownloadBytes)
                throw new InvalidDataException($"The .NET SDK download is larger than {MaxDownloadBytes / 1024 / 1024} MB");

            hash.AppendData(buffer, 0, read);
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

            if (total - reported < ProgressStep) continue;
            reported = total;
            progress?.Report(total);
        }

        if (reported != total) progress?.Report(total);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 残っても、次に落とすときにまた消す。
        }
    }

    private static void TryDeleteFile(string file)
    {
        try
        {
            if (File.Exists(file)) File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
