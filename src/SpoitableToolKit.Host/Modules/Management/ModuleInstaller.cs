using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Modules.Build;
using SpoitableToolKit.Host.Modules.Files;
using SpoitableToolKit.Host.Modules.GitHub;
using SpoitableToolKit.Host.Modules.Management.Models;
using SpoitableToolKit.Host.Modules.Packaging;

namespace SpoitableToolKit.Host.Modules.Management;

/// <summary>
/// 外からモジュールを入れる。<b>入れるのは次の起動</b>（動いているフォルダに触らない）。
/// <list type="number">
///   <item>落として <c>modules/.staging/&lt;token&gt;/</c> に展開し、<c>module.json</c> を読む（<see cref="StageFromGitHubAsync"/>）。ここではまだ何も入らない</item>
///   <item>利用者が「入れる」を押したら <c>modules/.pending/&lt;Id&gt;/</c> へ移す（<see cref="Confirm"/>）。やめたら仮置きを消す（<see cref="Cancel"/>）</item>
///   <item>次の起動の頭（何も読み込む前）に <see cref="PendingInstalls.Apply"/> が <c>modules/&lt;Id&gt;/</c> へ入れ替える（前のものは <c>.trash</c> へ）</item>
/// </list>
/// 置くのは<b>ユーザー側の置き場</b>だけ。作業場（<c>.staging</c>・<c>.pending</c>・<c>.trash</c>）がリンクなら使わない（リンクの先を消したり、先の物を入れたりしない）。
/// </summary>
internal sealed class ModuleInstaller(string userRoot, string bundledRoot, GitHubReleases github, ILogger logger)
{
    public const string StagingDirectoryName = ".staging";
    public const string PendingDirectoryName = ".pending";

    /// <summary>本体が印に使うファイルの名前。zip が持ち込むと、入れた直後に「外す」「OFF」が効いてしまう。</summary>
    private static readonly string[] ControlFiles = [ModuleCatalog.RemoveMarker, ".disabled"];

    private readonly string _modules = Path.Combine(ModulePaths.Normalize(userRoot), "modules");

    /// <summary>ユーザー側の置き場が同梱の場所と同じフォルダか（そこへは入れない。<see cref="PendingInstalls.Apply"/> と同じ見方）。</summary>
    private readonly bool _userIsBundled = ModulePaths.SameDirectory(userRoot, bundledRoot)
                                           || ModulePaths.SameDirectory(Path.Combine(userRoot, "modules"), Path.Combine(bundledRoot, "modules"));
    private readonly Lock _gate = new();
    private readonly Dictionary<string, StagedModule> _staged = new(StringComparer.Ordinal);

    /// <summary>
    /// build の作業場の名前。<b><c>modules/</c> の外</b>（repo の build が好きに書く所。path も短くする）。
    /// データの置き場の直下なので、利用者のフォルダと取り違えない名前にする（置き場は <c>SPOITABLE_DATA_DIR</c> で好きな所へ向けられる）。
    /// </summary>
    public const string BuildDirectoryName = ".module-build";

    private readonly string _build = Path.Combine(ModulePaths.Normalize(userRoot), BuildDirectoryName);
    private readonly ModuleSourceBuild _builder = new(logger);

    public string PendingDirectory => Path.Combine(_modules, PendingDirectoryName);

    public IReadOnlyList<PendingModule> Pending()
    {
        if (!Directory.Exists(PendingDirectory) || ModulePaths.IsLink(PendingDirectory)) return [];

        List<PendingModule> pending = [];
        foreach (var directory in Directory.GetDirectories(PendingDirectory))
        {
            try
            {
                var manifest = ModuleManifest.Parse(File.ReadAllText(Path.Combine(directory, ModuleManifest.FileName)));

                // 入るのはフォルダの名前の所（ApplyPending）。宣言と違うものは入れかけとして数えない。
                if (!string.Equals(manifest.Id, Path.GetFileName(directory), StringComparison.Ordinal)) continue;
                pending.Add(new PendingModule(manifest, ModuleSource.ReadFrom(directory), directory, KindOf(directory, manifest)));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "A pending module in {Directory} cannot be read", directory);
            }
        }

        return pending;
    }

    /// <summary>
    /// 展開したものが<b>探索で見つかる形</b>か（入れてから「読み込めません」になる前に、理由つきで断る）。
    /// モジュール: <c>&lt;Id&gt;.dll</c> か、<c>*.deps.json</c> と同じ名前の dll。ライブラリ: 直下に dll が無く、<c>shared/</c> に dll。
    /// </summary>
    /// <returns><c>module</c> か <c>library</c>。</returns>
    internal static string KindOf(string directory, ModuleManifest manifest)
    {
        if (File.Exists(Path.Combine(directory, manifest.Id + ".dll"))
            || Directory.GetFiles(directory, "*.deps.json").Any(deps => File.Exists(deps[..^".deps.json".Length] + ".dll")))
            return "module";

        var shared = Path.Combine(directory, "shared");
        if (Directory.GetFiles(directory, "*.dll").Length == 0 && Directory.Exists(shared) && Directory.GetFiles(shared, "*.dll").Length > 0)
            return "library";

        throw new InvalidDataException(
            $"The package has no module dll next to {ModuleManifest.FileName}: put {manifest.Id}.dll there (or the build's *.deps.json together with its dll), " +
            "or only shared/*.dll for a library that just shares an API");
    }

    /// <param name="progress">落としている途中の様子（最初に 0 バイトで 1 回、そのあと落とした量）。</param>
    public async Task<StagedModule> StageFromGitHubAsync(string url, CancellationToken cancellationToken, IProgress<ModuleDownload>? progress = null)
    {
        if (GitHubRepository.TryParse(url, out var problem) is not { } repository)
            throw new InvalidOperationException($"Cannot install from {url}: {problem}");

        // 起動の頭の入れ替えは、ここでは何もしない。聞いて「入れる」と言わせても入らないので、落とす前に断る。
        if (_userIsBundled)
            throw new InvalidOperationException(
                "Modules cannot be installed while the data folder is the app's own folder; unset SPOITABLE_DATA_DIR or point it somewhere else");

        var release = await github.LatestAsync(repository, cancellationToken).ConfigureAwait(false);
        var asset = GitHubReleases.ChooseAsset(release);
        progress?.Report(new ModuleDownload(release.Tag, asset.Name, 0, asset.Size));

        var (token, staging) = NewStaging();
        try
        {
            var download = Path.Combine(staging, "download.zip");
            var bytes = progress is null
                ? null
                : new DirectProgress<long>(done => progress.Report(new ModuleDownload(release.Tag, asset.Name, done, asset.Size)));
            string sha;
            await using (var file = File.Create(download))
                sha = await github.DownloadAsync(asset, file, cancellationToken, bytes).ConfigureAwait(false);

            var staged = StagePackage(token, staging, download,
                new ModuleSource(ModuleSource.GitHubKind, repository.ToString(), release.Tag, asset.Name, sha, DateTimeOffset.Now));
            logger.LogInformation("Staged {Id} {Version} from {Repository} {Tag} ({Asset}, SHA-256 {Sha})",
                staged.Manifest.Id, staged.Manifest.Version, repository, release.Tag, asset.Name, sha);
            return staged;
        }
        catch
        {
            TryDelete(staging);
            throw;
        }
    }

    /// <summary>
    /// ソースから build する前に聞くもの: <b>その repo の既定の branch の先頭の commit</b>（GitHub に聞くだけ。ここでは何も落とさず、何も動かさない）。
    /// 利用者に見せるのは、返したものの commit。build は、返したものをそのまま <see cref="StageFromSourceAsync"/> に渡す。
    /// </summary>
    public async Task<SourceOffer> OfferSourceAsync(string url, CancellationToken cancellationToken)
    {
        if (GitHubRepository.TryParse(url, out var problem) is not { } repository)
            throw new InvalidOperationException($"Cannot install from {url}: {problem}");

        if (_userIsBundled)
            throw new InvalidOperationException(
                "Modules cannot be installed while the data folder is the app's own folder; unset SPOITABLE_DATA_DIR or point it somewhere else");

        var commit = await github.HeadCommitAsync(repository, cancellationToken).ConfigureAwait(false);
        return new SourceOffer(repository, commit);
    }

    /// <summary>
    /// <paramref name="offer"/>（<see cref="OfferSourceAsync"/> が返した、利用者に見せたもの）の commit のソースを落として build し、出来た zip を仮置きする
    /// （そのあとはリリースから入れるときと同じ: <see cref="Confirm"/> か <see cref="Cancel"/>）。
    /// <b>build するのは、渡された offer の commit</b>（呼ぶ側が、利用者に見せたものをそのまま持って来る）。
    /// <para>
    /// ⚠ <b>build の間、その repo の build の手順（コード）が、利用者の権限で動く</b>（<see cref="ModuleSourceBuild"/>）。呼ぶ前に、そう言って同意を取る。
    /// </para>
    /// <para>
    /// build は<b>並べて走らせてよい</b>: 作業場（<see cref="BuildDirectoryName"/> の下の 1 つ）もパッケージの展開先も build ごとで、終わったら（止めたときも）消す。
    /// 同時に走らせる数を絞るのは、呼ぶ側。<paramref name="cancellationToken"/> で止めると、build の子のプロセスも止まる。
    /// </para>
    /// </summary>
    /// <param name="phase">いまどの段か（画面の進み表示用）。</param>
    public async Task<StagedModule> StageFromSourceAsync(
        SourceOffer offer, ModuleBuildTools tools, CancellationToken cancellationToken, TimeSpan? timeout = null,
        IProgress<SourceBuildPhase>? phase = null)
    {
        var work = Path.Combine(_build, Guid.NewGuid().ToString("N")[..8]);
        try
        {
            if (ModulePaths.IsLink(_build))
                throw new InvalidOperationException($"{_build} is a link; delete it so that modules can be built there");
            Directory.CreateDirectory(work);

            phase?.Report(SourceBuildPhase.Source);
            var archive = Path.Combine(work, "source.zip");
            string sha;
            await using (var file = File.Create(archive))
                sha = await github.DownloadSourceAsync(offer.Repository, offer.Commit, file, cancellationToken).ConfigureAwait(false);

            string tree;
            await using (var zip = File.OpenRead(archive))
                tree = ModulePackage.Expand(zip, Path.Combine(work, "s"), ModulePackage.MaxSourceEntries, ModulePackage.MaxExpandedBytes, ModulePackage.Source);
            File.Delete(archive);

            string built;
            try
            {
                var project = ModuleSourceBuild.FindProject(tree);
                phase?.Report(SourceBuildPhase.Build);
                built = await _builder.BuildAsync(project, work, tools, timeout ?? ModuleSourceBuild.DefaultTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException($"{offer.Repository} at {offer.Commit[..7]} could not be built: {ex.Message}");
            }

            phase?.Report(SourceBuildPhase.Check);
            var (token, staging) = NewStaging();
            try
            {
                var staged = StagePackage(token, staging, built,
                    new ModuleSource(ModuleSource.GitHubSourceKind, offer.Repository.ToString(), offer.Commit, string.Empty, sha, DateTimeOffset.Now));
                logger.LogInformation("Staged {Id} {Version}, built here from {Repository} at {Commit} (source SHA-256 {Sha})",
                    staged.Manifest.Id, staged.Manifest.Version, offer.Repository, offer.Commit, sha);
                return staged;
            }
            catch
            {
                TryDelete(staging);
                throw;
            }
        }
        finally
        {
            await DeleteWorkAsync(work).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// build の作業場を消す。子の dotnet が抜けた直後は、まだファイルを掴んでいることがあるので、少し間を置いてやり直す
    /// （それでも残ったら、次の起動の頭で消す）。
    /// </summary>
    private static async Task DeleteWorkAsync(string directory)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            TryDelete(directory);
            if (!Directory.Exists(directory)) return;

            await Task.Delay(200).ConfigureAwait(false);
        }
    }

    private (string Token, string Directory) NewStaging()
    {
        var token = Guid.NewGuid().ToString("N");
        var workspace = Path.Combine(_modules, StagingDirectoryName);
        if (ModulePaths.IsLink(workspace))
            throw new InvalidOperationException($"{workspace} is a link; delete it so that modules can be unpacked there");

        var staging = Path.Combine(workspace, token);
        Directory.CreateDirectory(staging);
        return (token, staging);
    }

    /// <summary>
    /// モジュールの zip を仮置きに展開して、入れてよい形かを確かめ、入れ元を書いて覚える。
    /// <b>リリースから落とした zip も、ここで build した zip も、同じ確かめを通る。</b>
    /// </summary>
    private StagedModule StagePackage(string token, string staging, string zipFile, ModuleSource source)
    {
        (string Directory, ModuleManifest Manifest) extracted;
        using (var zip = File.OpenRead(zipFile))
            extracted = ModulePackage.Extract(zip, Path.Combine(staging, "content"));
        File.Delete(zipFile);

        // 本体の印（外す・OFF）を持ち込ませない。持ち込むと、聞いたとおりに入れた直後に、自分で消えるか黙って止まる。
        if (ControlFiles.FirstOrDefault(name => File.Exists(Path.Combine(extracted.Directory, name))) is { } control)
            throw new InvalidDataException(
                $"The package has a file named {control} next to {ModuleManifest.FileName}; the app uses that name to mark a module as removed or turned off, so leave it out of the zip");

        // 本体より新しい SDK で作ったもの・この本体が読めるより古い SDK で作ったものは、入れても読み込めない。入れる前に言う。
        if (extracted.Manifest.NewerSdkProblem(ModuleManifest.AppSdk) is { } newer)
            throw new InvalidDataException(newer);

        if (extracted.Manifest.OlderSdkProblem(ModuleManifest.MinimumSdk) is { } older)
            throw new InvalidDataException(older);

        var kind = KindOf(extracted.Directory, extracted.Manifest);
        source.WriteTo(extracted.Directory);

        var staged = new StagedModule(token, extracted.Manifest, source, extracted.Directory, kind);
        lock (_gate) _staged[token] = staged;
        return staged;
    }

    /// <summary>仮置きしたものの入れ元（もう無ければ null）。</summary>
    public ModuleSource? SourceOf(string token)
    {
        lock (_gate) return _staged.TryGetValue(token, out var staged) ? staged.Source : null;
    }

    /// <summary>
    /// 仮置きを「次の起動で入る」へ移す。同じ Id の入れかけがあれば置き換える。
    /// <b>同じ Id に「外す」の印があれば消す</b>（入れると決めた方が後なので、そちらを通す。印が残ると、どちらが効くかが起動の順番しだいになる）。
    /// </summary>
    public bool Confirm(string token)
    {
        StagedModule? staged;
        lock (_gate)
        {
            if (!_staged.TryGetValue(token, out staged)) return false;
        }

        try
        {
            if (ModulePaths.IsLink(PendingDirectory))
                throw new IOException($"{PendingDirectory} is a link; delete it so that modules can be installed");

            var target = Path.Combine(PendingDirectory, staged.Manifest.Id);
            Directory.CreateDirectory(PendingDirectory);
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Directory.Move(staged.Directory, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 仮置きは残す（もう一度押せる）。
            logger.LogWarning(ex, "Could not queue {Id} for the next start", staged.Manifest.Id);
            return false;
        }

        // ここまで来たら、次の起動で入る（仮置きは .pending へ移った。ここで「できなかった」と返すと、利用者がやめても入ってしまう）。
        // 外す印は消す。消せなくても入る: 起動の頭は入れる方が先で、印の付いた前の版ごと .trash へ移す（PendingRemovals はそのあと）。
        var marker = Path.Combine(_modules, staged.Manifest.Id, ModuleCatalog.RemoveMarker);
        try
        {
            if (File.Exists(marker)) File.Delete(marker);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not take back the removal of {Id}; it is replaced at the next start anyway", staged.Manifest.Id);
        }

        lock (_gate) _staged.Remove(token);
        TryDelete(Path.Combine(_modules, StagingDirectoryName, token));

        logger.LogInformation("{Id} {Version} will be installed at the next start", staged.Manifest.Id, staged.Manifest.Version);
        return true;
    }

    public bool Cancel(string token)
    {
        lock (_gate)
        {
            if (!_staged.Remove(token, out _)) return false;
        }

        TryDelete(Path.Combine(_modules, StagingDirectoryName, token));
        return true;
    }

    public bool CancelPending(string id)
    {
        if (!ModuleManifest.IsValidId(id)) return false;

        var target = Path.Combine(PendingDirectory, id);
        if (ModulePaths.IsLink(PendingDirectory) || !Directory.Exists(target) || !ModulePaths.IsUnder(target, PendingDirectory)) return false;

        TryDelete(target);
        return !Directory.Exists(target);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 次の起動の頭でまた消す。
        }
    }
}
