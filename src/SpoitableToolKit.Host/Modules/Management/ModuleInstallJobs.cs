using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Events.Config;
using SpoitableToolKit.Host.Modules.Build;
using SpoitableToolKit.Host.Modules.GitHub;
using SpoitableToolKit.Host.Modules.Management.Models;
using SpoitableToolKit.Host.Modules.Packaging;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Push;

namespace SpoitableToolKit.Host.Modules.Management;

/// <summary>
/// モジュールを<b>入れている途中のもの</b>の台帳（管理画面のカード 1 枚 = ここの 1 件）。
/// URL を調べる → （リリースの zip を落とす / ソースから build する）→ 入れるかを聞く、までを 1 件ごとに進める。<b>何件でも並べて進む</b>。
/// <list type="bullet">
///   <item><b>状態の正はここ</b>。変わるたびに、全部の面（デスクトップの窓・VR の窓）へ <see cref="Channel"/> で配る。画面は持たないので、読み直しても同じカードが出る</item>
///   <item><b>同意と、実際に動く物の結び付きもここ</b>。何を build するか（commit）・何を落とすか（.NET SDK）・何を入れるか（仮置き）は、その 1 件が自分で持つ。
///         画面から受けるのは「どの 1 件の・どの質問（通し番号）に・はい」だけ（<see cref="Answer"/>）。番号が違う答え（二度押し・古い面・別の質問）は断る</item>
///   <item>聞くのは 3 つ: build するか（リリースが無い repo）・.NET SDK を落とすか（見つからないとき）・入れるか</item>
///   <item>.NET SDK を落とすのは<b>共有の 1 本</b>。待っている件は同じ進みを出し、1 件を止めても、ほかが待っていれば止めない</item>
///   <item>build は並べて走らせる。設定（<see cref="HostConfig.ParallelModuleBuilds"/>）を切ると 1 つずつ（残りは <see cref="ModuleJobStates.Queued"/>）。落とすのは、設定に関わらず並行</item>
///   <item><b>利用者が止めた</b>件は片付けて消す。<b>失敗した</b>件（時間切れも）は、理由を持ったまま残す（利用者が閉じるか、調べ直すまで）</item>
/// </list>
/// </summary>
internal sealed class ModuleInstallJobs(
    ModuleInstaller installer, ModuleCatalog catalog, ModuleBuildSetup? build, HostConfig config, UiBroadcast broadcast, ILogger logger)
    : IConfigEventListener<HostConfig>, IDisposable
{
    public const string Channel = "moduleJobs";

    public const int MaxJobs = 12;

    /// <summary>.NET SDK を落として入れるのに待つ長さ（290 MB を落として 5,578 個のファイルを展開する）。</summary>
    public static readonly TimeSpan SdkInstallLimit = TimeSpan.FromMinutes(60);

    /// <summary>進み（量）を配る間隔。状態が変わったときは、間隔に関わらず配る。</summary>
    private const long ProgressIntervalMilliseconds = 100;

    private sealed class Job(string id, string url, string? repository, string title)
    {
        public string Id { get; } = id;
        public string Url { get; } = url;
        public string? Repository { get; } = repository;
        public string Title { get; } = title;

        public string State = ModuleJobStates.Looking;
        public CancellationTokenSource Stop = new();

        // いま聞いていること（答えを待っている間だけ Answer がある）。
        public int Ask;
        public string? AskKind;
        public TaskCompletionSource? Answer;

        public SourceOffer? Offer;
        public StagedModule? Staged;
        public StagedModuleView? StagedView;

        public string? Tag;
        public string? Asset;
        public long Bytes;
        public long Total;
        public string? SdkVersion;
        public int Step;
        public int Steps;
        public string? Phase;
        public DateTimeOffset? Since;
        public string? WaitingFor;

        public string? FailedAt;
        public string? Error;
        public string? Note;
    }

    private sealed class SdkInstall(string version, long total)
    {
        public string Version { get; } = version;
        public long Total { get; } = total;
        public CancellationTokenSource Stop { get; } = new();
        public Task<DotnetSdk> Task = null!;
        public int Waiters;
        public long Bytes;
        public bool Unpacking;

        /// <summary>待っている件が居なくなったので止めた（時間切れではない）。</summary>
        public bool Abandoned;
    }

    private readonly Lock _gate = new();
    private readonly List<Job> _jobs = [];
    private readonly List<Job> _building = [];
    private TaskCompletionSource _buildEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SdkInstall? _sdkInstall;
    private long _revision;
    private int _asks;
    private long _lastProgressPush;
    private bool _disposed;

    /// <summary>
    /// 「モジュールを追加」の URL の段: その URL で件を始められるかを、<b>件を作らずに</b>答える（画面は、通ったら同意の段か件を追う段へ進む）。
    /// 断るのは、GitHub の repo の URL でない・もう <see cref="MaxJobs"/> 件ある（同じ repo の件があれば、それを追えるので断らない）・閉じている途中。
    /// 始めるとき（<see cref="Start"/>）にも同じことを見直す（同意の段で待っている間に埋まることがある）。
    /// </summary>
    public ModuleUrlCheck Check(string url)
    {
        var showAgreement = config.ShowModuleAgreement;
        if (GitHubRepository.TryParse((url ?? string.Empty).Trim(), out var problem) is not { } parsed)
            return new ModuleUrlCheck(false, null, ModuleUrlCheck.UrlProblem, problem, showAgreement);

        var repository = parsed.ToString();
        lock (_gate)
        {
            if (_disposed) return new ModuleUrlCheck(false, repository, ModuleUrlCheck.BusyProblem, Closing, showAgreement);
            if (_jobs.Count >= MaxJobs && !_jobs.Any(j => string.Equals(j.Repository, repository, StringComparison.OrdinalIgnoreCase)))
                return new ModuleUrlCheck(false, repository, ModuleUrlCheck.BusyProblem, Full, showAgreement);
        }

        return new ModuleUrlCheck(true, repository, null, null, showAgreement);
    }

    private const string Closing = "The app is closing";

    private static readonly string Full = $"{MaxJobs} modules are already being installed; finish or cancel one first";

    /// <summary>
    /// URL を調べ始める。<b>同じ repo の件がもうあれば、2 枚目は作らない</b>（失敗して残っているものは、調べ直す）。
    /// </summary>
    public ModuleJobStart Start(string url)
    {
        url = (url ?? string.Empty).Trim();
        var repository = GitHubRepository.TryParse(url, out _)?.ToString();

        Job job;
        lock (_gate)
        {
            if (_disposed) return Started(false, null, Closing);

            if (repository is not null
                && _jobs.FirstOrDefault(j => string.Equals(j.Repository, repository, StringComparison.OrdinalIgnoreCase)) is { } existing)
            {
                if (existing.State != ModuleJobStates.Failed) return Started(true, existing.Id, null);
                job = existing;
                Reset(job);
            }
            else
            {
                if (_jobs.Count >= MaxJobs)
                    return Started(false, null, Full);

                job = new Job(Guid.NewGuid().ToString("N"), url, repository, repository ?? ModuleManifest.Shown(url));
                _jobs.Add(job);
            }

            _revision++;
        }

        Push();
        _ = Task.Run(() => RunAsync(job));
        return Started(true, job.Id, null);
    }

    /// <summary>失敗して残っている件を、同じ URL で調べ直す（先頭の commit は、聞き直す）。</summary>
    public bool Retry(string id)
    {
        Job? job;
        lock (_gate)
        {
            job = Find(id);
            if (job is not { State: ModuleJobStates.Failed } || _disposed) return false;
            Reset(job);
            _revision++;
        }

        Push();
        _ = Task.Run(() => RunAsync(job));
        return true;
    }

    /// <summary>
    /// 聞いていることに「はい」と答える。<b>いま聞いている質問そのもの</b>（<paramref name="ask"/> = 通し番号、<paramref name="kind"/> = 何の質問か）にだけ通る —
    /// 二度押し・古い面・「build する」のつもりの押下が「入れる」に届く、を通さない。1 つの質問に、答えは 1 回。
    /// </summary>
    public bool Answer(string id, int ask, string kind)
    {
        TaskCompletionSource answer;
        lock (_gate)
        {
            if (Find(id) is not { Answer: { } pending } job || job.Ask != ask || !string.Equals(job.AskKind, kind, StringComparison.Ordinal))
                return false;

            answer = pending;
            job.Answer = null;
            _revision++;
        }

        Push();
        return answer.TrySetResult();
    }

    /// <summary>
    /// 止める・閉じる。進んでいる件は止めて片付け（build の子のプロセスも・仮置きも）、失敗して残っている件は消す。
    /// </summary>
    public bool Cancel(string id)
    {
        Job? job;
        bool finished;
        lock (_gate)
        {
            job = Find(id);
            if (job is null) return false;

            finished = job.State == ModuleJobStates.Failed;
            if (finished)
            {
                _jobs.Remove(job);
                _revision++;
            }
        }

        if (finished) Push();
        else job.Stop.Cancel();   // RunAsync が片付けて、台帳から消す
        return true;
    }

    public ModuleJobsView Snapshot()
    {
        lock (_gate)
        {
            return new ModuleJobsView(_revision, DotnetSdks.Major, [.. _jobs.Select(j => new ModuleJobView(
                Id: j.Id, Title: j.Title, Repository: j.Repository, Commit: j.Offer?.Commit, State: j.State,
                // 答えを待っている間だけ（答えたあと・聞いていないときは 0）。
                Ask: j.Answer is null ? 0 : j.Ask, AskKind: j.Answer is null ? null : j.AskKind,
                Step: j.Step, Steps: j.Steps, Phase: j.Phase, Since: j.Since?.ToUnixTimeMilliseconds(),
                Tag: j.Tag, Asset: j.Asset, Bytes: j.Bytes, Total: j.Total, SdkVersion: j.SdkVersion, WaitingFor: j.WaitingFor,
                FailedAt: j.FailedAt, Error: j.Error, Note: j.Note, Staged: j.StagedView))]);
        }
    }

    public void Dispose()
    {
        Job[] jobs;
        SdkInstall? install;
        lock (_gate)
        {
            _disposed = true;
            jobs = [.. _jobs];
            install = _sdkInstall;
        }

        foreach (var job in jobs) job.Stop.Cancel();
        install?.Stop.Cancel();
    }

    private static ModuleJobStart Started(bool ok, string? id, string? error) => new(ok, id, error);

    private Job? Find(string id) => _jobs.FirstOrDefault(j => string.Equals(j.Id, id, StringComparison.Ordinal));

    /// <summary>調べ直す前に、前の回のものを消す（<see cref="_gate"/> の中で呼ぶ）。</summary>
    private static void Reset(Job job)
    {
        job.State = ModuleJobStates.Looking;
        job.Stop = new CancellationTokenSource();
        job.Ask = 0;
        job.AskKind = null;
        job.Answer = null;
        job.Offer = null;
        job.Staged = null;
        job.StagedView = null;
        job.Tag = job.Asset = job.SdkVersion = job.Phase = job.WaitingFor = job.FailedAt = job.Error = job.Note = null;
        job.Bytes = job.Total = 0;
        job.Step = job.Steps = 0;
        job.Since = null;
    }

    private async Task RunAsync(Job job)
    {
        var stop = job.Stop.Token;
        var stage = "install";   // 失敗したとき、何に失敗したと言うか（install / build / sdk）
        try
        {
            StagedModule staged;
            try
            {
                var progress = new DirectProgress<ModuleDownload>(download => Update(job, j =>
                {
                    j.State = ModuleJobStates.Downloading;
                    j.Tag = download.Tag;
                    j.Asset = download.Asset;
                    j.Bytes = download.Bytes;
                    j.Total = download.Total;
                }, progress: download.Bytes > 0));

                staged = await WithLimit(stop, ModuleCatalog.StageLimit, "Downloading the module",
                    limit => installer.StageFromGitHubAsync(job.Url, limit, progress)).ConfigureAwait(false);
            }
            catch (NoReleaseException none) when (build is not null)
            {
                // リリースが無い。ソースから build できるか — 既定の branch の先頭の commit を聞く（落とさない・動かさない）。
                // 聞けなければ（非公開・存在しない・commit が無い）、元の「リリースが無い」をそのまま言う。
                SourceOffer offer;
                try
                {
                    offer = await WithLimit(stop, ModuleCatalog.StageLimit, "Asking GitHub for the repository's head commit",
                        limit => installer.OfferSourceAsync(job.Url, limit)).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsExpected(ex))
                {
                    logger.LogInformation("{Title} has no release, and its source cannot be offered either: {Reason}", job.Title, ex.Message);
                    throw none;
                }

                stage = "build";
                staged = await BuildAsync(job, offer, at => stage = at, stop).ConfigureAwait(false);
                stage = "install";
            }

            // 画面に見せる形は、ここで 1 回だけ作る（いま入っているものとの比べを含む。配るたびに作り直さない）。
            var view = catalog.Describe(staged);
            Update(job, j =>
            {
                j.Staged = staged;
                j.StagedView = view;
            });

            string? note = null;
            while (true)
            {
                await AskAsync(job, ModuleJobStates.AskInstall, "install", stop, j => j.Note = note).ConfigureAwait(false);
                if (catalog.ConfirmInstall(staged.Token)) break;

                // 入れかけにできなかった（置き場に書けないなど）。聞いたままにして、そう出す（もう一度押せる）。
                note = "confirmFailed";
            }

            Remove(job);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // 利用者が止めた（か、アプリが閉じる）。片付けて消す。
            Discard(job);
            Remove(job);
        }
        catch (Exception ex)
        {
            var expected = IsExpected(ex);
            if (expected) logger.LogWarning("Could not install a module from {Title}: {Reason}", job.Title, ex.Message);
            else logger.LogError(ex, "Installing a module from {Title} failed unexpectedly", job.Title);

            Discard(job);
            var reason = expected ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
            Update(job, j =>
            {
                j.State = ModuleJobStates.Failed;
                j.FailedAt = stage;
                j.Error = reason;
                j.Answer = null;
                j.AskKind = null;
            });
        }
    }

    /// <summary>
    /// リリースの無い repo を、聞いてから build する: build するか → （.NET SDK が無ければ）落とすか → （並列が無効なら順番を待って）build。
    /// </summary>
    private async Task<StagedModule> BuildAsync(Job job, SourceOffer offer, Action<string> stage, CancellationToken stop)
    {
        await AskAsync(job, ModuleJobStates.AskBuild, "build", stop, j => j.Offer = offer).ConfigureAwait(false);

        // 同意は取れた。ここからは「Build 中」（.NET SDK を確かめる所から）。
        Update(job, j =>
        {
            j.State = ModuleJobStates.Building;
            j.Phase = "sdk";
            j.Since = DateTimeOffset.UtcNow;
        });

        // **断れるものは、.NET SDK を落とすかを聞く前に断る**（数百 MB を落とさせたあとで「できません」と言わない）:
        // SDK のパッケージが付いていないアプリは、何を落としても build できない。
        if (!Directory.Exists(build!.SdkFeed) || !Directory.EnumerateFiles(build.SdkFeed, "SpoitableToolKit.Sdk.*.nupkg").Any())
            throw new InvalidOperationException(
                "This copy of the app does not carry the module SDK package (sdk/SpoitableToolKit.Sdk.*.nupkg next to the app), " +
                "so it cannot build modules from source; reinstall the app");

        stage("sdk");
        var sdk = await EnsureSdkAsync(job, stop).ConfigureAwait(false);
        stage("build");

        await EnterBuildAsync(job, stop).ConfigureAwait(false);
        try
        {
            Update(job, j =>
            {
                j.State = ModuleJobStates.Building;
                j.Phase = Name(SourceBuildPhase.Source);
                j.Since = DateTimeOffset.UtcNow;
                j.WaitingFor = null;
                if (j.Steps > 0) j.Step = j.Steps;
            });

            var tools = new ModuleBuildTools(sdk.Dotnet, build.SdkFeed, build.PackageFeed);
            var phase = new DirectProgress<SourceBuildPhase>(at => Update(job, j => j.Phase = Name(at)));
            return await WithLimit(stop, ModuleCatalog.StageLimit + ModuleSourceBuild.DefaultTimeout, "The build",
                limit => installer.StageFromSourceAsync(offer, tools, limit, phase: phase)).ConfigureAwait(false);
        }
        finally
        {
            LeaveBuild(job);
        }
    }

    private static string Name(SourceBuildPhase phase) => phase switch
    {
        SourceBuildPhase.Source => "source",
        SourceBuildPhase.Build => "build",
        _ => "check",
    };

    /// <summary>
    /// build に使う .NET SDK を用意する。見つかればそれ。無ければ、<b>聞いてから</b>落とす。
    /// 落とすのは共有の 1 本: もう落としている最中なら、聞かずにそれを待つ（落とすことへの同意は、先に押した件で取れている）。
    /// </summary>
    private async Task<DotnetSdk> EnsureSdkAsync(Job job, CancellationToken stop)
    {
        DotnetSdkDownload? offered = null;
        while (true)
        {
            SdkInstall? install;
            lock (_gate) install = _sdkInstall;

            if (install is null)
            {
                if (await build!.Sdks.FindAsync(stop).ConfigureAwait(false) is { } found) return found;

                if (offered is null)
                {
                    var download = await WithLimit(stop, ModuleCatalog.StageLimit, "Asking for the list of .NET releases",
                        limit => build.Sdks.LatestAsync(limit)).ConfigureAwait(false);
                    await AskAsync(job, ModuleJobStates.AskSdk, "sdk", stop, j =>
                    {
                        j.SdkVersion = download.Version;
                        j.Bytes = 0;
                        j.Total = download.Bytes;
                        j.Steps = 2;
                        j.Step = 1;
                    }).ConfigureAwait(false);
                    offered = download;

                    // 答えを待っている間に、ほかの件が入れ終えた・落とし始めたかもしれない。見直す。
                    continue;
                }

                install = StartSdkInstall(offered);
            }

            lock (_gate)
            {
                install.Waiters++;
                job.State = install.Unpacking ? ModuleJobStates.SdkInstalling : ModuleJobStates.SdkDownloading;
                job.SdkVersion = install.Version;
                job.Bytes = install.Bytes;
                job.Total = install.Total;
                job.Steps = 2;
                job.Step = 1;
                _revision++;
            }

            Push();
            try
            {
                return await install.Task.WaitAsync(stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!stop.IsCancellationRequested)
            {
                // 共有の 1 本が止まった。待っていた件が居なくなって止めたのなら（入れ違いで待ち始めた）、やり直す。そうでなければ時間切れ。
                if (!install.Abandoned) throw new TimeoutException("Downloading the .NET SDK took too long and was stopped");
            }
            finally
            {
                lock (_gate)
                {
                    install.Waiters--;
                    if (install.Waiters == 0 && !install.Task.IsCompleted)
                    {
                        install.Abandoned = true;
                        install.Stop.Cancel();
                    }
                }
            }
        }
    }

    private SdkInstall StartSdkInstall(DotnetSdkDownload download)
    {
        lock (_gate)
        {
            if (_sdkInstall is { } running) return running;

            var install = new SdkInstall(download.Version, download.Bytes);
            install.Stop.CancelAfter(SdkInstallLimit);
            install.Task = Task.Run(async () =>
            {
                try
                {
                    var sdk = await build!.Sdks.InstallAsync(download, install.Stop.Token,
                        new DirectProgress<long>(bytes => OnSdkProgress(install, bytes)), () => OnSdkUnpacking(install)).ConfigureAwait(false);
                    OnSdkInstalled(install);
                    return sdk;
                }
                finally
                {
                    lock (_gate)
                    {
                        if (ReferenceEquals(_sdkInstall, install)) _sdkInstall = null;
                    }
                }
            });

            _sdkInstall = install;
            return install;
        }
    }

    private void OnSdkProgress(SdkInstall install, long bytes)
    {
        lock (_gate)
        {
            install.Bytes = bytes;
            foreach (var job in _jobs.Where(j => j.State == ModuleJobStates.SdkDownloading))
            {
                job.Bytes = bytes;
                job.Total = install.Total;
            }

            _revision++;
            if (!ProgressDue()) return;
        }

        Push();
    }

    private void OnSdkUnpacking(SdkInstall install)
    {
        lock (_gate)
        {
            install.Unpacking = true;
            foreach (var job in _jobs.Where(j => j.State == ModuleJobStates.SdkDownloading)) job.State = ModuleJobStates.SdkInstalling;
            _revision++;
        }

        Push();
    }

    /// <summary>
    /// .NET SDK が入った。<b>「落としますか」と聞かれたままの件は、聞くまでもなくなった</b>ので進める（build することには、もう同意している）。
    /// </summary>
    private void OnSdkInstalled(SdkInstall install)
    {
        TaskCompletionSource[] moot;
        lock (_gate)
        {
            if (ReferenceEquals(_sdkInstall, install)) _sdkInstall = null;

            var asked = _jobs.Where(j => j is { AskKind: "sdk", Answer: not null }).ToArray();
            moot = [.. asked.Select(j => j.Answer!)];
            foreach (var job in asked) job.Answer = null;
            _revision++;
        }

        foreach (var answer in moot) answer.TrySetResult();
        Push();
    }

    /// <summary>
    /// build を始めてよくなるまで待つ。並列（<see cref="HostConfig.ParallelModuleBuilds"/>）が有効なら待たない。
    /// 無効なら、走っている build が無くなるまで <see cref="ModuleJobStates.Queued"/>。
    /// </summary>
    private async Task EnterBuildAsync(Job job, CancellationToken stop)
    {
        while (true)
        {
            Task ended;
            lock (_gate)
            {
                if (config.ParallelModuleBuilds || _building.Count == 0)
                {
                    _building.Add(job);
                    return;
                }

                job.State = ModuleJobStates.Queued;
                job.WaitingFor = _building[0].Title;
                _revision++;
                ended = _buildEnded.Task;
            }

            Push();
            await ended.WaitAsync(stop).ConfigureAwait(false);
        }
    }

    private void LeaveBuild(Job job) => WakeQueued(job);

    /// <summary>
    /// 順番待ちの件を起こす（起きた件は、始めてよいかを見直す）。<paramref name="left"/> は、終わった build（あれば、走っているものから外す）。
    /// </summary>
    private void WakeQueued(Job? left = null)
    {
        TaskCompletionSource ended;
        lock (_gate)
        {
            if (left is not null) _building.Remove(left);
            ended = _buildEnded;
            _buildEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        ended.TrySetResult();
    }

    /// <summary>
    /// 設定が変わった。<b>並列 Build を有効に戻したら、順番待ちの件をすぐ起こす</b>
    /// （起こさないと、走っている build が終わるまで待ち、そのあいだに来た件に追い抜かれる）。UI のスレッドから来るので、起こすだけで待たない。
    /// </summary>
    public void OnConfigChanged(ConfigChangedEventParams<HostConfig> e)
    {
        if (e.Changed(c => c.ParallelModuleBuilds)) WakeQueued();
    }

    /// <summary>聞く（状態を「聞いている」にして、答えを待つ）。質問ごとに新しい通し番号を振る。</summary>
    private async Task AskAsync(Job job, string state, string kind, CancellationToken stop, Action<Job>? also = null)
    {
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Update(job, j =>
        {
            also?.Invoke(j);
            j.State = state;
            j.AskKind = kind;
            j.Ask = ++_asks;
            j.Answer = answer;
        });

        await answer.Task.WaitAsync(stop).ConfigureAwait(false);
    }

    /// <summary>
    /// <paramref name="limit"/> で打ち切る。<b>利用者が止めたのか、時間切れなのかを分ける</b>: 止めたのなら <see cref="OperationCanceledException"/> のまま
    /// （その件は消える）、時間切れなら <see cref="TimeoutException"/>（その件は、理由を持って残る）。
    /// </summary>
    private static async Task<T> WithLimit<T>(CancellationToken stop, TimeSpan limit, string what, Func<CancellationToken, Task<T>> work)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timer.CancelAfter(limit);
        try
        {
            return await work(timer.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            throw new TimeoutException($"{what} took too long and was stopped");
        }
    }

    /// <summary>画面へそのまま出せる失敗（理由の文を持つ）。それ以外は「見込んでいない失敗」として記録に全部残す。</summary>
    private static bool IsExpected(Exception ex) =>
        ex is InvalidOperationException or InvalidDataException or HttpRequestException or IOException
            or UnauthorizedAccessException or JsonException or TimeoutException;

    private void Discard(Job job)
    {
        StagedModule? staged;
        lock (_gate)
        {
            staged = job.Staged;
            job.Staged = null;
            job.StagedView = null;
        }

        if (staged is not null) installer.Cancel(staged.Token);
    }

    private void Remove(Job job)
    {
        lock (_gate)
        {
            if (!_jobs.Remove(job)) return;
            _revision++;
        }

        Push();
    }

    /// <summary>1 件を変えて配る。<paramref name="progress"/> は量の進み（間隔を空けて配る）。もう台帳に無い件（止めたあとに届いた進み）は捨てる。</summary>
    private void Update(Job job, Action<Job> change, bool progress = false)
    {
        lock (_gate)
        {
            if (!_jobs.Contains(job)) return;

            change(job);
            _revision++;
            if (progress && !ProgressDue()) return;
        }

        Push();
    }

    /// <summary>量の進みを配ってよい頃か（<see cref="_gate"/> の中で呼ぶ）。</summary>
    private bool ProgressDue()
    {
        var now = Environment.TickCount64;
        if (now - _lastProgressPush < ProgressIntervalMilliseconds) return false;

        _lastProgressPush = now;
        return true;
    }

    private void Push() => broadcast.Push(Channel, Snapshot());
}
