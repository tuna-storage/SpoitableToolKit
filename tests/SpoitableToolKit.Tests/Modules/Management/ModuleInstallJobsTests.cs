using SpoitableToolKit.Host.Config;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Localization;
using SpoitableToolKit.Host.ExternalModules;
using SpoitableToolKit.Host.Modules;
using SpoitableToolKit.Host.Modules.Build;
using SpoitableToolKit.Host.Modules.GitHub;
using SpoitableToolKit.Host.Modules.Management;
using SpoitableToolKit.Host.Modules.Management.Models;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Push;
using SpoitableToolKit.Tests.Fakes;
using SpoitableToolKit.Tests.Helpers;
using SpoitableToolKit.Tests.Modules.Build;
using Xunit;

namespace SpoitableToolKit.Tests.Modules.Management;

/// <summary>モジュールを入れている途中のものの台帳（<see cref="ModuleInstallJobs"/>。管理画面のカード 1 枚 = 1 件）。本物の build は走らせない（<see cref="ModuleSourceBuildTests"/> が走らせる）: モジュールの project が無い repo を使い、build の入口で断られる所までを見る。「途中」は足止めで作る（偽の GitHub・偽の配布元が、返す前に待つ）。</summary>
[Collection("module-loader")]
public sealed class ModuleInstallJobsTests : IDisposable
{
    private const string Sample = "https://github.com/someone/sample";
    private const string NoProject = "someone/sample at 0123456 could not be built: no module project was found";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "stk-tests", Guid.NewGuid().ToString("N"));
    private readonly HostEvents _hostEvents = new(NullLoggerFactory.Instance);
    private readonly HostConfig _config = new();
    private readonly Recorder _broadcast = new();
    private readonly FakeGitHub _github = new();
    private readonly FakeDotnetSite _dotnet = new();
    private readonly ConcurrentDictionary<string, string> _sdkListings = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ModuleInstallJobs> _made = [];
    private ModuleInstaller _installer = null!;

    private string User => Path.Combine(_root, "user");

    private string Bundled => Path.Combine(_root, "app");

    private string Tools => Path.Combine(_root, "tools");

    /// <summary>この PC の <c>dotnet.exe</c> の代わり（置くまでは「この PC には SDK が無い」）。</summary>
    private string PcDotnet => Path.Combine(_root, "pc", "dotnet.exe");

    private string BuildFolder => Path.Combine(User, ModuleInstaller.BuildDirectoryName);

    public void Dispose()
    {
        foreach (var jobs in _made) jobs.Dispose();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed class Recorder : UiBroadcast
    {
        private readonly List<(string Channel, object Payload)> _pushed = [];

        public (string Channel, object Payload)[] Pushed
        {
            get
            {
                lock (_pushed) return [.. _pushed];
            }
        }

        public override void Push(string channel, object payload)
        {
            lock (_pushed) _pushed.Add((channel, payload));
        }
    }

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <param name="canBuild">false なら、build の用意が無いアプリ（ソースから build することを、持ちかけもしない）。</param>
    private ModuleInstallJobs Make(bool bundledPackage = true, TimeSpan? httpTimeout = null, bool canBuild = true)
    {
        Directory.CreateDirectory(User);
        var configs = new ModuleConfigStore(_hostEvents.Hubs, NullLoggerFactory.Instance);
        configs.Add(HostConfig.Id, "Host", null, _config, User);
        var manager = new ModuleManager(new NoServices(), new FakeVrcLogBus(), new FakeVrcLogStore(), new FakeExternalToolProvider(),
            new FakeNotifications(), new FakeClipboard(), new FakeOscBus(), _hostEvents, new UiBroadcast(), configs,
            new ExternalModuleRegistry(NullLoggerFactory.Instance), new Localizer(NullLogger<Localizer>.Instance), _config,
            NullLoggerFactory.Instance, [User, Bundled], Path.Combine(_root, "module-data"), () => false) { Vr = new() };
        manager.LoadAll();

        var http = new HttpClient(_github);
        if (httpTimeout is { } timeout) http.Timeout = timeout;
        var github = new GitHubReleases(http, new Uri("http://127.0.0.1:1/"));
        _installer = new ModuleInstaller(User, Bundled, github, NullLogger.Instance);
        var catalog = new ModuleCatalog(manager, _config, configs, _broadcast, new FakeLocalizer(), _installer,
            new ModuleUpdates(User, github, new ManualClock(), NullLogger.Instance), User, Bundled, NullLogger<ModuleCatalog>.Instance);

        var feed = Path.Combine(Bundled, "sdk");
        if (bundledPackage)
        {
            Directory.CreateDirectory(feed);
            File.WriteAllText(Path.Combine(feed, "SpoitableToolKit.Sdk.2026.11.0.nupkg"), "x");
        }

        var sdks = new DotnetSdks(new HttpClient(_dotnet), FakeDotnetSite.Metadata, Tools, NullLogger.Instance, [PcDotnet],
            (dotnet, _) => Task.FromResult(_sdkListings.TryGetValue(dotnet, out var listing) ? listing : null));
        var jobs = new ModuleInstallJobs(_installer, catalog, canBuild ? new ModuleBuildSetup(sdks, feed, null) : null, _config, _broadcast,
            NullLogger.Instance);
        _made.Add(jobs);
        return jobs;
    }

    private void PutADotnetSdkOnThisPc()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PcDotnet)!);
        File.WriteAllText(PcDotnet, "x");
        _sdkListings[PcDotnet] = "10.0.100 [C:\\x]\n";
    }

    /// <summary>落とした（偽の）SDK が、展開した先でも入れたあとでも「SDK を持つ」と答えるようにする。</summary>
    private void TheDownloadedSdkWorks()
    {
        _sdkListings[Path.Combine(Tools, "dotnet.new", "dotnet.exe")] = "10.0.404 [C:\\x]\n";
        _sdkListings[Path.Combine(Tools, "dotnet", "dotnet.exe")] = "10.0.404 [C:\\x]\n";
    }

    private void PublishARelease(string id = "sample", string version = "1.2.0")
    {
        _github.Status = HttpStatusCode.OK;
        _github.Zip = FakeGitHub.ZipOf(
            ("module.json", JsonSerializer.Serialize(new { id, name = "Sample module", version })), ($"{id}.dll", "x")).ToArray();
    }

    /// <summary>リリースの無い repo にする（ソースは、モジュールの project が無いもの）。</summary>
    private void NoRelease()
    {
        _github.Status = HttpStatusCode.NotFound;
        _github.Source = FakeGitHub.ZipOf(("someone-sample-0123456/README.md", "# nothing to build")).ToArray();
    }

    private static ModuleJobView[] Jobs(ModuleInstallJobs jobs) => jobs.Snapshot().Jobs;

    private static ModuleJobView? Job(ModuleInstallJobs jobs, string id) => Jobs(jobs).FirstOrDefault(job => job.Id == id);

    private static string State(ModuleInstallJobs jobs, string id) => Job(jobs, id)?.State ?? "(gone)";

    private static async Task<ModuleJobView> Until(ModuleInstallJobs jobs, string id, params string[] states)
    {
        for (var attempt = 0; attempt < 3000; attempt++)
        {
            if (Job(jobs, id) is { } job && states.Contains(job.State)) return job;
            await Task.Delay(10);
        }

        Assert.Fail($"{id} は {string.Join(" / ", states)} にならなかった: {JsonSerializer.Serialize(jobs.Snapshot())}");
        return null!;
    }

    private static async Task UntilGone(ModuleInstallJobs jobs, string id)
    {
        for (var attempt = 0; attempt < 3000; attempt++)
        {
            if (Job(jobs, id) is null) return;
            await Task.Delay(10);
        }

        Assert.Fail($"{id} が消えなかった: {JsonSerializer.Serialize(jobs.Snapshot())}");
    }

    private static async Task Eventually(Func<bool> condition, string what)
    {
        for (var attempt = 0; attempt < 3000; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        Assert.Fail(what);
    }

    private static string Start(ModuleInstallJobs jobs, string url = Sample)
    {
        var answer = jobs.Start(url);
        Assert.True(answer.Ok, answer.Error);
        return answer.Id!;
    }

    private static bool Yes(ModuleInstallJobs jobs, ModuleJobView job) => jobs.Answer(job.Id, job.Ask, job.AskKind!);

    private bool AskedForTheSource => _github.AskedPaths().Any(path => path.Contains("/zipball/", StringComparison.Ordinal));

    private int SourcesAsked => _github.AskedPaths().Count(path => path.Contains("/zipball/", StringComparison.Ordinal));

    [Fact]
    public async Task AReleaseIsDownloadedThenAskedAboutAndQueuedOnYes()
    {
        PublishARelease();
        var jobs = Make();

        var id = Start(jobs);
        var asking = await Until(jobs, id, ModuleJobStates.AskInstall);

        Assert.Equal("someone/sample", asking.Title);
        Assert.Equal("install", asking.AskKind);
        Assert.True(asking.Ask > 0);
        var staged = asking.Staged!;
        Assert.Equal(("sample", "1.2.0", "v1.2.0"),
            (staged.Id, staged.Version, staged.Tag));
        Assert.Empty(_installer.Pending());

        Assert.True(Yes(jobs, asking));
        await UntilGone(jobs, id);

        Assert.Equal("sample", Assert.Single(_installer.Pending()).Manifest.Id);
        Assert.Contains(_broadcast.Pushed, push => push.Channel == "modulesChanged");
    }

    /// <summary>答えは、いま聞いている質問そのものにだけ通る（面が 2 つあり、二度押しもある。「Buildする」のつもりの押下が「インストール」の同意にならないように）。</summary>
    [Fact]
    public async Task AnAnswerOnlyCountsForTheQuestionBeingAsked()
    {
        NoRelease();
        PutADotnetSdkOnThisPc();
        var jobs = Make();
        var id = Start(jobs);
        var asking = await Until(jobs, id, ModuleJobStates.AskBuild);
        var ask = asking.Ask;

        Assert.False(jobs.Answer(id, ask, "install"), "違う種類の質問への答えが通った");
        Assert.False(jobs.Answer(id, ask, "sdk"), "違う種類の質問への答えが通った");
        Assert.False(jobs.Answer(id, ask + 1, "build"), "違う番号の答えが通った");
        Assert.False(jobs.Answer(id, 0, "build"), "番号の無い答えが通った");
        Assert.False(jobs.Answer("0123456789abcdef0123456789abcdef", ask, "build"), "無い件への答えが通った");
        Assert.Equal(ModuleJobStates.AskBuild, State(jobs, id));
        Assert.False(AskedForTheSource, "同意していないのにソースを落とした");

        Assert.True(jobs.Answer(id, ask, "build"));
        Assert.False(jobs.Answer(id, ask, "build"), "同じ質問に 2 回答えられた");
        await Until(jobs, id, ModuleJobStates.Failed);
        Assert.Equal(1, SourcesAsked);
    }

    /// <summary>調べ直すと、質問の通し番号は新しくなる。前の回の番号（古い面に残った板からの押下）では、新しい質問に答えられない。</summary>
    [Fact]
    public async Task AnOldQuestionsNumberDoesNotAnswerANewQuestion()
    {
        NoRelease();
        PutADotnetSdkOnThisPc();
        var jobs = Make();
        var id = Start(jobs);
        var first = await Until(jobs, id, ModuleJobStates.AskBuild);
        Assert.True(Yes(jobs, first));
        await Until(jobs, id, ModuleJobStates.Failed);

        _github.Commit = "fedcba9876543210fedcba9876543210fedcba98";   // その間に、branch の先頭が動いた
        Assert.True(jobs.Retry(id));
        var second = await Until(jobs, id, ModuleJobStates.AskBuild);

        Assert.NotEqual(first.Ask, second.Ask);
        Assert.Equal("fedcba9876543210fedcba9876543210fedcba98", second.Commit);
        Assert.False(jobs.Answer(id, first.Ask, "build"), "前の回の番号で、新しい commit の build に同意できた");
        Assert.Equal(1, SourcesAsked);
    }

    /// <summary>答えたら、次へ進む前にまず「答えた」を配る（画面は、これで「はい」のボタンを押せなくする）。</summary>
    [Fact]
    public async Task AnAnsweredQuestionIsPushedAsNoLongerAsking()
    {
        PublishARelease();
        var jobs = Make();
        var id = Start(jobs);
        var asking = await Until(jobs, id, ModuleJobStates.AskInstall);
        Assert.True(Yes(jobs, asking));
        await UntilGone(jobs, id);

        var pushed = _broadcast.Pushed.Where(push => push.Channel == ModuleInstallJobs.Channel)
            .Select(push => ((ModuleJobsView)push.Payload).Jobs.SingleOrDefault(job => job.Id == id))
            .ToArray();
        Assert.Contains(pushed, job => job is { State: ModuleJobStates.AskInstall, AskKind: "install" } && job.Ask == asking.Ask);
        Assert.Contains(pushed, job => job is { State: ModuleJobStates.AskInstall, Ask: 0, AskKind: null });
    }

    /// <summary>見込んでいない失敗でも、理由を持って残る（投げたままにすると、カードは「調べている」のまま止まる）。何の失敗かが分かるように、型の名前を付ける。</summary>
    [Fact]
    public async Task AnUnexpectedFailureStaysOnTheCardWithItsKind()
    {
        _github.Throws = new InvalidCastException("something nobody planned for");
        var jobs = Make();

        var failed = await Until(jobs, Start(jobs), ModuleJobStates.Failed);

        Assert.Equal("install", failed.FailedAt);
        Assert.Equal("InvalidCastException: something nobody planned for", failed.Error);
    }

    [Theory]
    [InlineData("gone")]
    [InlineData("empty")]
    [InlineData("garbage")]
    [InlineData("no-setup")]
    public async Task ARepositoryWhoseSourceCannotBeOfferedOnlySaysThatItHasNoRelease(string how)
    {
        NoRelease();
        if (how == "gone") _github.CommitStatus = HttpStatusCode.NotFound;
        if (how == "empty") _github.CommitStatus = HttpStatusCode.Conflict;
        if (how == "garbage") _github.CommitAnswer = "<html>sign in</html>";
        var jobs = Make(canBuild: how != "no-setup");

        var ended = await Until(jobs, Start(jobs), ModuleJobStates.Failed, ModuleJobStates.AskBuild);

        Assert.Equal(ModuleJobStates.Failed, ended.State);
        Assert.Equal("install", ended.FailedAt);
        Assert.Equal("someone/sample has no published release (or the repository is private or does not exist)", ended.Error);
        Assert.Null(ended.Commit);
        Assert.False(AskedForTheSource);
        Assert.Equal(how != "no-setup", _github.AskedPaths().Any(path => path.EndsWith("/commits/HEAD", StringComparison.Ordinal)));
    }

    /// <summary>build するのは見せた commit（聞いたあとで branch の先頭が動いても変わらない）。</summary>
    [Fact]
    public async Task ARepositoryWithoutAReleaseAsksBeforeBuildingAndBuildsTheCommitItShowed()
    {
        NoRelease();
        PutADotnetSdkOnThisPc();
        var jobs = Make();

        var id = Start(jobs);
        var asking = await Until(jobs, id, ModuleJobStates.AskBuild);

        Assert.Equal("build", asking.AskKind);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", asking.Commit);
        Assert.False(AskedForTheSource, "聞いただけでソースを落とした");
        Assert.Empty(_dotnet.Asked);

        _github.Commit = "fedcba9876543210fedcba9876543210fedcba98";
        Assert.True(Yes(jobs, asking));
        var failed = await Until(jobs, id, ModuleJobStates.Failed);

        Assert.Equal("build", failed.FailedAt);
        Assert.StartsWith(NoProject, failed.Error);
        Assert.Contains("/repos/someone/sample/zipball/0123456789abcdef0123456789abcdef01234567", _github.AskedPaths());
        Assert.Empty(Directory.GetDirectories(BuildFolder));
    }

    [Fact]
    public async Task CancellingBeforeAnsweringDownloadsNothing()
    {
        NoRelease();
        PutADotnetSdkOnThisPc();
        var jobs = Make();
        var id = Start(jobs);
        await Until(jobs, id, ModuleJobStates.AskBuild);

        Assert.True(jobs.Cancel(id));
        await UntilGone(jobs, id);

        Assert.False(AskedForTheSource);
        Assert.False(Directory.Exists(BuildFolder));
        Assert.False(jobs.Cancel(id), "もう無い件を止められた");
    }

    [Fact]
    public async Task CancellingWhileItBuildsStopsItAndLeavesNothing()
    {
        NoRelease();
        PutADotnetSdkOnThisPc();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _github.BeforeSource = () =>
        {
            reached.TrySetResult();
            return Task.Delay(Timeout.Infinite);
        };
        var jobs = Make();
        var id = Start(jobs);
        Assert.True(Yes(jobs, await Until(jobs, id, ModuleJobStates.AskBuild)));
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var building = await Until(jobs, id, ModuleJobStates.Building);
        Assert.Equal("source", building.Phase);
        Assert.True(building.Since > 0);
        Assert.Single(Directory.GetDirectories(BuildFolder));

        Assert.True(jobs.Cancel(id));
        await UntilGone(jobs, id);

        await Eventually(() => Directory.GetDirectories(BuildFolder).Length == 0, "止めたのに、build の作業場が残った");
    }

    [Fact]
    public async Task ATimeoutIsAFailureThatStaysNotACancel()
    {
        NoRelease();
        PutADotnetSdkOnThisPc();
        _github.BeforeSource = () => Task.Delay(Timeout.Infinite);
        var jobs = Make(httpTimeout: TimeSpan.FromMilliseconds(300));

        var id = Start(jobs);
        Assert.True(Yes(jobs, await Until(jobs, id, ModuleJobStates.AskBuild)));
        var failed = await Until(jobs, id, ModuleJobStates.Failed);

        Assert.Equal("build", failed.FailedAt);
        Assert.Equal("The build took too long and was stopped", failed.Error);
    }

    /// <summary>.NET SDK を落とすかを聞く前に断る（数百 MB を落とさせたあとで「できません」と言わない）。</summary>
    [Fact]
    public async Task ACopyOfTheAppWithoutTheSdkPackageFailsBeforeAskingAboutTheDotnetSdk()
    {
        NoRelease();
        var jobs = Make(bundledPackage: false);

        var id = Start(jobs);
        Assert.True(Yes(jobs, await Until(jobs, id, ModuleJobStates.AskBuild)));
        var failed = await Until(jobs, id, ModuleJobStates.Failed);

        Assert.Equal(
            "This copy of the app does not carry the module SDK package (sdk/SpoitableToolKit.Sdk.*.nupkg next to the app), " +
            "so it cannot build modules from source; reinstall the app",
            failed.Error);
        Assert.Empty(_dotnet.Asked);
        Assert.False(AskedForTheSource);
    }

    /// <summary>手順は 2 つ（.NET SDK → Build）と数える。</summary>
    [Fact]
    public async Task WithoutADotnetSdkItAsksThenDownloadsThenBuilds()
    {
        NoRelease();
        TheDownloadedSdkWorks();
        var jobs = Make();
        var id = Start(jobs);
        Assert.True(Yes(jobs, await Until(jobs, id, ModuleJobStates.AskBuild)));

        var asking = await Until(jobs, id, ModuleJobStates.AskSdk);

        Assert.Equal("sdk", asking.AskKind);
        Assert.Equal("10.0.404", asking.SdkVersion);
        Assert.Equal(_dotnet.Zip.Length, asking.Total);
        Assert.Equal((1, 2), (asking.Step, asking.Steps));
        Assert.Equal(0, _dotnet.Downloads);
        Assert.False(AskedForTheSource, "SDK が無いのにソースを落とした");
        Assert.False(Directory.Exists(Path.Combine(Tools, "dotnet")));

        Assert.True(Yes(jobs, asking));
        var failed = await Until(jobs, id, ModuleJobStates.Failed);

        Assert.StartsWith(NoProject, failed.Error);
        Assert.Equal("build", failed.FailedAt);
        Assert.Equal((2, 2), (failed.Step, failed.Steps));
        Assert.Equal(1, _dotnet.Downloads);
        Assert.True(File.Exists(Path.Combine(Tools, "dotnet", "dotnet.exe")));
    }

    [Fact]
    public async Task ADownloadThatIsNotAnSdkFailsAsTheSdkStep()
    {
        NoRelease();
        var jobs = Make();
        var id = Start(jobs);
        Assert.True(Yes(jobs, await Until(jobs, id, ModuleJobStates.AskBuild)));
        Assert.True(Yes(jobs, await Until(jobs, id, ModuleJobStates.AskSdk)));

        var failed = await Until(jobs, id, ModuleJobStates.Failed);

        Assert.Equal("sdk", failed.FailedAt);
        Assert.Equal(
            "The .NET SDK 10.0.404 was downloaded and unpacked, but it does not list an SDK that can build modules; nothing was installed",
            failed.Error);
        Assert.False(AskedForTheSource);
    }

    /// <summary>.NET SDK を落とすのは共有の 1 本。1 件を止めても、ほかが待っていれば止めない。「落としますか」と聞かれたままだった件は、入った時点で先へ進む（聞くまでもなくなった）。</summary>
    [Fact]
    public async Task TheDotnetSdkIsDownloadedOnceForEveryCardThatNeedsIt()
    {
        NoRelease();
        TheDownloadedSdkWorks();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _dotnet.BeforeZip = () => release.Task;
        var jobs = Make();

        // a・b は、SDK が無いと分かって「落としますか」と聞かれる。a だけ「はい」。
        var a = Start(jobs, "https://github.com/someone/a");
        var b = Start(jobs, "https://github.com/someone/b");
        Assert.True(Yes(jobs, await Until(jobs, a, ModuleJobStates.AskBuild)));
        Assert.True(Yes(jobs, await Until(jobs, b, ModuleJobStates.AskBuild)));
        var askingA = await Until(jobs, a, ModuleJobStates.AskSdk);
        await Until(jobs, b, ModuleJobStates.AskSdk);
        Assert.True(Yes(jobs, askingA));
        await Until(jobs, a, ModuleJobStates.SdkDownloading);

        // c は、落としている最中に来た。聞かれずに、同じ 1 本を待つ。
        var c = Start(jobs, "https://github.com/someone/c");
        Assert.True(Yes(jobs, await Until(jobs, c, ModuleJobStates.AskBuild)));
        var waiting = await Until(jobs, c, ModuleJobStates.SdkDownloading);
        Assert.Equal("10.0.404", waiting.SdkVersion);
        Assert.Equal(ModuleJobStates.AskSdk, State(jobs, b));

        // 最初に「はい」と言った a を止めても、c が待っているので、落とすのは続く。
        Assert.True(jobs.Cancel(a));
        await UntilGone(jobs, a);
        Assert.Equal(ModuleJobStates.SdkDownloading, State(jobs, c));

        release.SetResult();

        // 入った。c は build へ。b は「落としますか」に答えていないが、聞くまでもなくなったので、同じく build へ。
        var failedC = await Until(jobs, c, ModuleJobStates.Failed);
        var failedB = await Until(jobs, b, ModuleJobStates.Failed);
        Assert.Contains("could not be built: no module project was found", failedC.Error);
        Assert.Contains("could not be built: no module project was found", failedB.Error);
        Assert.Equal(1, _dotnet.Downloads);
    }

    /// <summary>.NET SDK が入って先へ進むのは、「落としますか」と聞かれていた件だけ。「Buildしますか」にまだ答えていない件は、利用者が「Buildする」を押すまで動かない。</summary>
    [Fact]
    public async Task ACardStillAskingAboutTheBuildKeepsAskingWhenTheSdkArrives()
    {
        NoRelease();
        TheDownloadedSdkWorks();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _dotnet.BeforeZip = () => release.Task;
        var jobs = Make();

        var a = Start(jobs, "https://github.com/someone/a");
        Assert.True(Yes(jobs, await Until(jobs, a, ModuleJobStates.AskBuild)));
        Assert.True(Yes(jobs, await Until(jobs, a, ModuleJobStates.AskSdk)));
        await Until(jobs, a, ModuleJobStates.SdkDownloading);

        // b は、落としている最中に来て「Buildしますか」と聞かれている（まだ答えていない）。
        var b = Start(jobs, "https://github.com/someone/b");
        var asking = await Until(jobs, b, ModuleJobStates.AskBuild);

        release.SetResult();
        await Until(jobs, a, ModuleJobStates.Failed);

        Assert.Equal(ModuleJobStates.AskBuild, State(jobs, b));
        Assert.Equal(1, SourcesAsked);

        // 聞かれたままの質問には、そのまま答えられる。答えたら、入った SDK で build へ（もう聞かない）。
        Assert.True(Yes(jobs, asking));
        await Until(jobs, b, ModuleJobStates.Failed);
        Assert.Equal(2, SourcesAsked);
        Assert.Equal(1, _dotnet.Downloads);
    }

    /// <summary>誰も要らないものを落とし続けない。次に要る件は、また聞かれる。</summary>
    [Fact]
    public async Task TheDotnetSdkDownloadStopsWhenNoCardWaitsForIt()
    {
        NoRelease();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _dotnet.BeforeZip = () =>
        {
            reached.TrySetResult();
            return Task.Delay(Timeout.Infinite);
        };
        var jobs = Make();
        var id = Start(jobs);
        Assert.True(Yes(jobs, await Until(jobs, id, ModuleJobStates.AskBuild)));
        Assert.True(Yes(jobs, await Until(jobs, id, ModuleJobStates.AskSdk)));
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await Until(jobs, id, ModuleJobStates.SdkDownloading);

        Assert.True(jobs.Cancel(id));
        await UntilGone(jobs, id);

        // 止まっていれば、落としかけは消えている。次の件は、同じ 1 本に乗らずに、聞かれる所から。
        await Eventually(() => !File.Exists(Path.Combine(Tools, "dotnet-sdk.zip.part")), "誰も待っていないのに、.NET SDK を落とし続けた");
        var next = Start(jobs, "https://github.com/someone/next");
        Assert.True(Yes(jobs, await Until(jobs, next, ModuleJobStates.AskBuild)));
        await Until(jobs, next, ModuleJobStates.AskSdk);
    }

    /// <summary>build は並べて走る（既定）。設定で並列を切ると 1 つずつで、2 つ目は前の build が終わるのを待つ（誰を待っているかを出す）。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BuildsRunSideBySideUnlessTheSettingIsOff(bool parallel)
    {
        NoRelease();
        PutADotnetSdkOnThisPc();
        _config.ParallelModuleBuilds = parallel;
        var holds = new ConcurrentQueue<TaskCompletionSource>();
        _github.BeforeSource = () =>
        {
            var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            holds.Enqueue(hold);
            return hold.Task;
        };
        var jobs = Make();

        var a = Start(jobs, "https://github.com/someone/a");
        Assert.True(Yes(jobs, await Until(jobs, a, ModuleJobStates.AskBuild)));
        await Eventually(() => holds.Count == 1, "1 つ目の build が始まらなかった");
        var b = Start(jobs, "https://github.com/someone/b");
        Assert.True(Yes(jobs, await Until(jobs, b, ModuleJobStates.AskBuild)));

        if (parallel)
        {
            await Eventually(() => holds.Count == 2, "2 つ目の build が、1 つ目の終わりを待った");
            Assert.Equal((ModuleJobStates.Building, ModuleJobStates.Building), (State(jobs, a), State(jobs, b)));
        }
        else
        {
            var queued = await Until(jobs, b, ModuleJobStates.Queued);
            Assert.Equal("someone/a", queued.WaitingFor);
            Assert.Equal(1, SourcesAsked);

            Assert.True(holds.TryDequeue(out var first));
            first.SetResult();
            await Until(jobs, a, ModuleJobStates.Failed);
            await Eventually(() => holds.Count == 1, "前の build が終わったのに、待っていた build が始まらなかった");
            Assert.Equal(ModuleJobStates.Building, State(jobs, b));
        }

        while (holds.TryDequeue(out var hold)) hold.SetResult();
        await Until(jobs, a, ModuleJobStates.Failed);
        await Until(jobs, b, ModuleJobStates.Failed);
        Assert.Equal(2, SourcesAsked);
    }

    /// <summary>並列を有効に戻したら、待っていた件はすぐ始まる（走っている build の終わりを待たない。待たせたままだと、あとから来た件に追い抜かれる）。</summary>
    [Fact]
    public async Task TurningParallelBuildsBackOnStartsTheWaitingBuildAtOnce()
    {
        NoRelease();
        PutADotnetSdkOnThisPc();
        _config.ParallelModuleBuilds = false;
        var holds = new ConcurrentQueue<TaskCompletionSource>();
        _github.BeforeSource = () =>
        {
            var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            holds.Enqueue(hold);
            return hold.Task;
        };
        var jobs = Make();

        var a = Start(jobs, "https://github.com/someone/a");
        Assert.True(Yes(jobs, await Until(jobs, a, ModuleJobStates.AskBuild)));
        await Eventually(() => holds.Count == 1, "1 つ目の build が始まらなかった");
        var b = Start(jobs, "https://github.com/someone/b");
        Assert.True(Yes(jobs, await Until(jobs, b, ModuleJobStates.AskBuild)));
        await Until(jobs, b, ModuleJobStates.Queued);

        // 関係ない設定が変わっても、待っている件はそのまま。
        jobs.OnConfigChanged(ConfigTesting.Changed(_config, nameof(HostConfig.CheckModuleUpdates)));
        Assert.Equal(ModuleJobStates.Queued, State(jobs, b));

        _config.ParallelModuleBuilds = true;
        jobs.OnConfigChanged(ConfigTesting.Changed(_config, nameof(HostConfig.ParallelModuleBuilds)));

        await Eventually(() => holds.Count == 2, "並列を有効に戻しても、待っていた build が始まらなかった");
        Assert.Equal((ModuleJobStates.Building, ModuleJobStates.Building), (State(jobs, a), State(jobs, b)));

        while (holds.TryDequeue(out var hold)) hold.SetResult();
        await Until(jobs, a, ModuleJobStates.Failed);
        await Until(jobs, b, ModuleJobStates.Failed);
        Assert.Equal(2, SourcesAsked);
    }

    [Fact]
    public async Task TheSameRepositoryIsNotAddedTwice()
    {
        NoRelease();
        _github.CommitStatus = HttpStatusCode.NotFound;
        var jobs = Make();

        var id = Start(jobs);
        Assert.Equal(id, Start(jobs, "https://github.com/Someone/Sample"));
        Assert.Single(Jobs(jobs));
        var failed = await Until(jobs, id, ModuleJobStates.Failed);
        Assert.Equal("install", failed.FailedAt);
        Assert.Equal("someone/sample has no published release (or the repository is private or does not exist)", failed.Error);

        // 直った（リリースが出た）。もう一度同じ URL を入れると、残っていた件が調べ直す。
        PublishARelease();
        Assert.Equal(id, Start(jobs));
        await Until(jobs, id, ModuleJobStates.AskInstall);
        Assert.Single(Jobs(jobs));
    }

    [Fact]
    public async Task AFailedCardStaysUntilItIsClosed()
    {
        var jobs = Make();

        var id = Start(jobs, "https://example.com/someone/module\u202E");
        var failed = await Until(jobs, id, ModuleJobStates.Failed);

        Assert.Equal("https://example.com/someone/module?", failed.Title);
        Assert.Equal("install", failed.FailedAt);
        Assert.Contains("only github.com repositories are accepted", failed.Error);
        Assert.Empty(_github.AskedPaths());

        await Task.Delay(100);
        Assert.Equal(ModuleJobStates.Failed, State(jobs, id));

        Assert.True(jobs.Cancel(id));
        Assert.Empty(Jobs(jobs));
        Assert.False(jobs.Retry(id), "閉じた件を調べ直せた");
    }

    [Fact]
    public async Task AnInstallThatCannotBeQueuedKeepsAsking()
    {
        PublishARelease();
        var jobs = Make();
        var id = Start(jobs);
        var first = await Until(jobs, id, ModuleJobStates.AskInstall);
        Assert.Null(first.Note);

        // 入れかけの置き場の名前に、ファイルを置いておく（フォルダを作れない）。
        var pending = Path.Combine(User, "modules", ModuleInstaller.PendingDirectoryName);
        File.WriteAllText(pending, "in the way");
        Assert.True(Yes(jobs, first));
        await Eventually(() => Job(jobs, id) is { } job && job.Note == "confirmFailed" && job.Ask > 0,
            "入れかけにできなかったのに、そう出なかった");
        var second = Job(jobs, id)!;

        Assert.Equal(ModuleJobStates.AskInstall, second.State);
        Assert.NotEqual(first.Ask, second.Ask);

        File.Delete(pending);
        Assert.True(Yes(jobs, second));
        await UntilGone(jobs, id);
        Assert.Single(_installer.Pending());
    }

    [Fact]
    public async Task CancellingAtTheInstallQuestionRemovesWhatWasStaged()
    {
        PublishARelease();
        var jobs = Make();
        var id = Start(jobs);
        await Until(jobs, id, ModuleJobStates.AskInstall);
        var staging = Path.Combine(User, "modules", ModuleInstaller.StagingDirectoryName);
        Assert.Single(Directory.GetDirectories(staging));

        Assert.True(jobs.Cancel(id));
        await UntilGone(jobs, id);

        await Eventually(() => Directory.GetDirectories(staging).Length == 0, "止めたのに、仮置きが残った");
        Assert.Empty(_installer.Pending());
    }

    [Fact]
    public async Task MoreCardsThanTheLimitAreRefused()
    {
        NoRelease();
        var jobs = Make();
        for (var i = 0; i < ModuleInstallJobs.MaxJobs; i++) Start(jobs, $"https://github.com/someone/repo{i}");

        var refused = jobs.Start("https://github.com/someone/one-too-many");

        Assert.False(refused.Ok);
        Assert.Equal("12 modules are already being installed; finish or cancel one first", refused.Error);
        Assert.Equal(ModuleInstallJobs.MaxJobs, Jobs(jobs).Length);

        foreach (var job in Jobs(jobs)) await Until(jobs, job.Id!, ModuleJobStates.AskBuild);
    }

    [Fact]
    public async Task TheUrlStepIsAnsweredWithoutMakingACard()
    {
        NoRelease();
        var jobs = Make();

        var elsewhere = jobs.Check("https://example.com/someone/module");
        Assert.False(elsewhere.Ok);
        Assert.Equal(ModuleUrlCheck.UrlProblem, elsewhere.Problem);
        Assert.Equal("only github.com repositories are accepted", elsewhere.Error);
        Assert.Equal(("not a URL", ModuleUrlCheck.UrlProblem), (jobs.Check("someone/sample").Error, jobs.Check("someone/sample").Problem));

        var fine = jobs.Check("  https://github.com/someone/sample/releases  ");
        Assert.True(fine.Ok, fine.Error);
        Assert.Equal("someone/sample", fine.Repository);
        Assert.Null(fine.Problem);
        Assert.True(fine.ShowAgreement);
        Assert.Empty(Jobs(jobs));
        Assert.Empty(_github.AskedPaths());

        _config.ShowModuleAgreement = false;
        Assert.False(jobs.Check(Sample).ShowAgreement);
        Assert.False(jobs.Check("not a url").ShowAgreement);

        for (var i = 0; i < ModuleInstallJobs.MaxJobs; i++) Start(jobs, $"https://github.com/someone/repo{i}");
        var full = jobs.Check("https://github.com/someone/one-too-many");
        Assert.False(full.Ok);
        Assert.Equal(ModuleUrlCheck.BusyProblem, full.Problem);
        Assert.Equal("12 modules are already being installed; finish or cancel one first", full.Error);
        Assert.True(jobs.Check("https://github.com/someone/repo3").Ok, "同じ repo の件があるのに断った");
        Assert.Equal(ModuleInstallJobs.MaxJobs, Jobs(jobs).Length);

        foreach (var job in Jobs(jobs)) await Until(jobs, job.Id!, ModuleJobStates.AskBuild);
        jobs.Dispose();
        var closing = jobs.Check(Sample);
        Assert.Equal((false, ModuleUrlCheck.BusyProblem, "The app is closing"), (closing.Ok, closing.Problem, closing.Error));
    }

    /// <summary>変わるたびに、全部の面へ配る（<c>moduleJobs</c>）。番号は増えていき、画面は古い方を捨てる。</summary>
    [Fact]
    public async Task EveryChangeIsPushedWithARevisionThatOnlyGrows()
    {
        PublishARelease();
        var jobs = Make();
        var id = Start(jobs);
        Assert.True(Yes(jobs, await Until(jobs, id, ModuleJobStates.AskInstall)));
        await UntilGone(jobs, id);

        var now = jobs.Snapshot();
        await Eventually(
            () => _broadcast.Pushed.Any(push => push.Channel == ModuleInstallJobs.Channel && ((ModuleJobsView)push.Payload).Revision == now.Revision),
            "いまの台帳が配られていない");

        var pushed = _broadcast.Pushed.Where(push => push.Channel == ModuleInstallJobs.Channel).Select(push => (ModuleJobsView)push.Payload).ToArray();
        var latest = pushed.MaxBy(payload => payload.Revision)!;
        Assert.Equal(UiBroadcast.Json(now), UiBroadcast.Json(latest));
        Assert.Contains(pushed, payload => payload.Jobs.Any(job => job.State == ModuleJobStates.Downloading));
        Assert.Contains(pushed, payload => payload.Jobs.Any(job => job.State == ModuleJobStates.AskInstall));
    }

    /// <summary>ダウンロードの量の進みは 100 ms に 1 回までしか配らない（配るたびに、全部の面 — VR の窓も — で台帳を描き直す。.NET SDK は 1 MB ごとに知らせるので、速い回線では 1 秒に 100 回近くになる）。間引いた分も、台帳はいまの量を持つ。</summary>
    [Fact]
    public async Task DownloadProgressIsPushedAtMostEveryTenthOfASecond()
    {
        // 縮まない中身で 3 MB ほど（知らせは 256 KB ごとなので、12 回ほど）。
        var filler = Convert.ToBase64String(RandomNumberGenerator.GetBytes(4 * 1024 * 1024));
        _github.Status = HttpStatusCode.OK;
        _github.Zip = FakeGitHub.ZipOf(
            ("module.json", JsonSerializer.Serialize(new { id = "sample", name = "Sample module", version = "1.2.0" })), ("sample.dll", filler)).ToArray();
        var reports = _github.Zip.Length / (256 * 1024);
        var jobs = Make();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var id = Start(jobs);
        var asking = await Until(jobs, id, ModuleJobStates.AskInstall);
        clock.Stop();

        var downloading = _broadcast.Pushed.Where(push => push.Channel == ModuleInstallJobs.Channel)
            .Select(push => ((ModuleJobsView)push.Payload).Jobs.SingleOrDefault(job => job.Id == id))
            .Count(job => job is { State: ModuleJobStates.Downloading });
        // 配ってよいのは、始まり（0 バイト）・最初の進み・落とし終えて確かめたものを置いたとき（状態はまだダウンロード中）、そのあとは 100 ms に 1 回。
        var allowed = 3 + (int)(clock.ElapsedMilliseconds / 100);
        Assert.True(allowed < reports, $"落とすのに {clock.ElapsedMilliseconds} ms かかり、間引いたかを見分けられない（知らせは {reports} 回）");
        Assert.InRange(downloading, 1, allowed);
        Assert.Equal(ModuleJobStates.AskInstall, asking.State);
    }

    /// <summary>画面へは push でも橋でも同じ JSON で渡す（<see cref="UiBroadcast.Json"/>）。画面（<c>web/index.html</c> のカード）は camelCase の名前で読むので、record の名前を変えたら画面も変える。</summary>
    [Fact]
    public async Task ThePageReadsTheLedgerAsCamelCaseJson()
    {
        PublishARelease();
        var jobs = Make();
        var id = Start(jobs);
        await Until(jobs, id, ModuleJobStates.AskInstall);

        using var json = JsonDocument.Parse(UiBroadcast.Json(jobs.Snapshot()));
        var ledger = json.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(["revision", "dotnetMajor", "jobs"], ledger);

        var job = json.RootElement.GetProperty("jobs")[0];
        var names = job.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(
            [
                "id", "title", "repository", "commit", "state", "ask", "askKind", "step", "steps", "phase", "since", "tag", "asset",
                "bytes", "total", "sdkVersion", "waitingFor", "failedAt", "error", "note", "staged",
            ],
            names);

        var staged = job.GetProperty("staged").EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(
            [
                "token", "id", "name", "version", "kind", "repository", "tag", "asset", "sha256",
                "built", "replaces", "replacesVersion", "replacesBundled",
            ],
            staged);
        Assert.Equal(DotnetSdks.Major, json.RootElement.GetProperty("dotnetMajor").GetInt32());

        using var started = JsonDocument.Parse(UiBroadcast.Json(jobs.Start("https://github.com/someone/sample")));
        var answer = started.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(["ok", "id", "error"], answer);
    }

    [Fact]
    public async Task ClosingTheAppStopsEverything()
    {
        NoRelease();
        PutADotnetSdkOnThisPc();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _github.BeforeSource = () =>
        {
            reached.TrySetResult();
            return Task.Delay(Timeout.Infinite);
        };
        var jobs = Make();
        var id = Start(jobs);
        Assert.True(Yes(jobs, await Until(jobs, id, ModuleJobStates.AskBuild)));
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(30));

        jobs.Dispose();

        await UntilGone(jobs, id);
        Assert.False(jobs.Start("https://github.com/someone/later").Ok);
    }
}
