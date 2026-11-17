using SpoitableToolKit.Host.App;
using SpoitableToolKit.Host.Config;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Localization;
using SpoitableToolKit.Host.ExternalModules;
using SpoitableToolKit.Host.Modules;
using SpoitableToolKit.Host.Modules.Build;
using SpoitableToolKit.Host.Modules.Files;
using SpoitableToolKit.Host.Modules.GitHub;
using SpoitableToolKit.Host.Modules.Management;
using SpoitableToolKit.Host.Modules.Management.Models;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Push;
using SpoitableToolKit.Tests.Fakes;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Modules.Management;

// 読み込みを伴うので、ローダの試験と同じ列で直列に流す（既定のコンテキストはプロセスで 1 つ）。
[Collection("module-loader")]
public sealed class ModuleCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stk-tests", Guid.NewGuid().ToString("N"));
    private readonly HostEvents _hostEvents = new(NullLoggerFactory.Instance);
    private readonly HostConfig _config = new();
    private readonly RecordingBroadcast _broadcast = new();
    private readonly FakeGitHub _github = new();
    private readonly ManualClock _clock = new();
    private readonly FakeNotifications _notify = new();

    /// <summary>試験の見本のモジュールが build された版（repo の版と同じ）。直書きすると、版を上げるたびに試験が落ちる。</summary>
    private static readonly string BuiltVersion =
        typeof(ModuleCatalog).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    private string User => Path.Combine(_root, "user");

    private string Bundled => Path.Combine(_root, "app");

    private readonly List<string> _links = [];

    public void Dispose()
    {
        // リンクは先にリンクだけを外す（中を辿って消さない）。
        foreach (var link in _links)
        {
            try { if (Directory.Exists(link)) Directory.Delete(link); } catch (IOException) { }
        }

        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    // junction を作る（シンボリックリンクは権限が要るので使わない）。先は必ずこの試験の置き場の中にする（守りが壊れていたら、リンクの先のフォルダが本当に消える）。
    private void Junction(string link, string target)
    {
        Assert.StartsWith(_root, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Directory.CreateDirectory(target);
        using var made = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        made.WaitForExit();
        Assert.True(Directory.Exists(link), $"junction を作れなかった: {made.StandardError.ReadToEnd()}");
        _links.Add(link);
    }

    private sealed class RecordingBroadcast : UiBroadcast
    {
        public List<string> Channels { get; } = [];

        public override void Push(string channel, object payload) => Channels.Add(channel);
    }

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private string Install(string fixture, string destination)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "fixtures", fixture);
        var target = Path.GetFullPath(Path.Combine(_root, destination));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy, overwrite: true);
        }

        return target;
    }

    // ユーザー側（user）→ 同梱（app）の順（本物と同じ並び）。
    private (ModuleManager Manager, ModuleCatalog Catalog) Load(string? userRoot = null)
    {
        var user = userRoot ?? User;
        var configs = new ModuleConfigStore(_hostEvents.Hubs, NullLoggerFactory.Instance);
        Directory.CreateDirectory(user);
        configs.Add(HostConfig.Id, "Host", null, _config, user);

        // 依存の理由は本体の辞書で文にする（材料の名前が入るかを見るので、日本語の辞書を読む）。
        var text = new Localizer(NullLogger<Localizer>.Instance);
        text.Load("ja", [LocaleSource.Host]);

        var manager = new ModuleManager(new NoServices(), new FakeVrcLogBus(), new FakeVrcLogStore(), new FakeExternalToolProvider(),
            new FakeNotifications(), new FakeClipboard(), new FakeOscBus(), _hostEvents, new UiBroadcast(), configs,
            new ExternalModuleRegistry(NullLoggerFactory.Instance), text, _config,
            NullLoggerFactory.Instance, [user, Bundled], Path.Combine(_root, "module-data"), () => false) { Vr = new() };
        manager.LoadAll();

        _installer = Installer(user);
        var catalog = new ModuleCatalog(manager, _config, configs, _broadcast, new FakeLocalizer(), _installer, Updates(user), user, Bundled,
            NullLogger<ModuleCatalog>.Instance);
        return (manager, catalog);
    }

    private ModuleInstaller _installer = null!;

    // 更新は、リリースではなく、その repo の先頭の commit と比べる（同じなら出ない。進んだら、その commit）。
    [Fact]
    public async Task AModuleBuiltFromSourceIsListedAsBuiltOnThisPc()
    {
        Install("shared/TestContract", "user/modules/provider/shared");
        var provider = Install("modules/provider", "user/modules/provider");
        new ModuleSource(ModuleSource.GitHubSourceKind, "someone/sample", _github.Commit, string.Empty, "00", DateTimeOffset.UnixEpoch).WriteTo(provider);
        var consumer = Install("modules/consumer", "user/modules/consumer");
        new ModuleSource(ModuleSource.GitHubKind, "someone/other", "v1.0.0", "a.zip", "00", DateTimeOffset.UnixEpoch).WriteTo(consumer);
        var (_, catalog) = Load();

        await catalog.CheckUpdatesAsync(force: true, default);

        var built = Entry(catalog, "provider");
        Assert.True(built.GetProperty("built").GetBoolean());
        Assert.Equal("someone/sample", built.GetProperty("source").GetString());
        Assert.Equal(_github.Commit, built.GetProperty("sourceTag").GetString());
        Assert.Equal(JsonValueKind.Null, built.GetProperty("update").ValueKind);
        Assert.False(Entry(catalog, "consumer").GetProperty("built").GetBoolean());
        Assert.Equal("v1.2.0", Entry(catalog, "consumer").GetProperty("update").GetString());

        _github.Commit = "89abcdef0123456789abcdef0123456789abcdef";
        await catalog.CheckUpdatesAsync(force: true, default);

        Assert.Equal("89abcdef0123456789abcdef0123456789abcdef", Entry(catalog, "provider").GetProperty("update").GetString());
    }

    // 確かめたあとで入れ元が先へ進み、それを入れたとき、次の起動で、確かめたときに見た古い物を「更新がある」と出さない。
    [Fact]
    public async Task AConfirmedInstallIsRememberedAsTheNewestOfItsSource()
    {
        var old = Path.Combine(User, "modules", "old");
        Directory.CreateDirectory(old);
        new ModuleSource(ModuleSource.GitHubKind, "someone/sample", "v1.0.0", "a.zip", "00", DateTimeOffset.UnixEpoch).WriteTo(old);
        Publish("sample", "1.2.0", "v1.2.0");
        var (_, catalog) = Load();
        await catalog.CheckUpdatesAsync(force: true, default);

        Publish("sample", "1.3.0", "v1.3.0");
        await Install(catalog);

        var installed = new ModuleSource(ModuleSource.GitHubKind, "someone/sample", "v1.3.0", "sample-module.zip", "00", DateTimeOffset.UnixEpoch);
        Assert.Null(Updates().LatestFor(installed));
    }

    // ソースから Build した物の更新は、module.json の版が変わらないことが多いので、次の起動で入る行に commit も出す（pendingCommit）。リリースから入れる物には出さない。
    [Fact]
    public void AnUpdateBuiltFromSourceCarriesItsCommitUntilTheNextStart()
    {
        Install("shared/TestContract", "user/modules/provider/shared");
        Install("modules/provider", "user/modules/provider");
        var pending = Install("modules/provider", $"user/modules/{ModuleInstaller.PendingDirectoryName}/provider");
        File.WriteAllText(Path.Combine(pending, "module.json"), """{ "id": "provider", "name": "Provider", "version": "1.0.0" }""");
        new ModuleSource(ModuleSource.GitHubSourceKind, "someone/sample", "89abcdef0123456789abcdef0123456789abcdef", string.Empty, "00",
            DateTimeOffset.UnixEpoch).WriteTo(pending);
        var (_, catalog) = Load();

        var entry = Entry(catalog, "provider");
        Assert.Equal(JsonValueKind.String, entry.GetProperty("pending").ValueKind);
        Assert.Equal("89abcdef0123456789abcdef0123456789abcdef", entry.GetProperty("pendingCommit").GetString());

        new ModuleSource(ModuleSource.GitHubKind, "someone/sample", "v2.0.0", "a.zip", "00", DateTimeOffset.UnixEpoch).WriteTo(pending);
        Assert.Equal(JsonValueKind.Null, Entry(Load().Catalog, "provider").GetProperty("pendingCommit").ValueKind);
    }

    // 行の開発元は、次の起動を待たずに入れ直した方になる（動いている物のままだと、入れ直したのに前の commit が次の起動まで残って見える）。動いている物が出す版と状態は、そのまま。
    [Fact]
    public void AnInstallFromAnotherSourceShowsThatSourceOnTheRowRightAway()
    {
        Install("shared/TestContract", "user/modules/provider/shared");
        var installed = Install("modules/provider", "user/modules/provider");
        new ModuleSource(ModuleSource.GitHubSourceKind, "someone/sample", "9d4c4230123456789abcdef0123456789abcdef0", string.Empty, "00",
            DateTimeOffset.UnixEpoch).WriteTo(installed);
        var before = Entry(Load().Catalog, "provider");
        Assert.True(before.GetProperty("built").GetBoolean());

        var pending = Install("modules/provider", $"user/modules/{ModuleInstaller.PendingDirectoryName}/provider");
        File.WriteAllText(Path.Combine(pending, "module.json"), """{ "id": "provider", "name": "Provider", "version": "2.0.0" }""");
        new ModuleSource(ModuleSource.GitHubKind, "someone/released", "v2.0.0", "a.zip", "00", DateTimeOffset.UnixEpoch).WriteTo(pending);

        var entry = Entry(Load().Catalog, "provider");

        Assert.Equal("running", entry.GetProperty("state").GetString());
        Assert.Equal(before.GetProperty("version").GetString(), entry.GetProperty("version").GetString());
        Assert.Equal("2.0.0", entry.GetProperty("pending").GetString());
        Assert.Equal("someone/released", entry.GetProperty("source").GetString());
        Assert.Equal("v2.0.0", entry.GetProperty("sourceTag").GetString());
        Assert.False(entry.GetProperty("built").GetBoolean());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("pendingCommit").ValueKind);

        // 入れ元の跡が無い入れかけ（手で置いた）なら、動いている物の入れ元のまま。
        File.Delete(Path.Combine(pending, ModuleSource.FileName));
        var unknown = Entry(Load().Catalog, "provider");
        Assert.Equal(("someone/sample", "9d4c4230123456789abcdef0123456789abcdef0", true),
            (unknown.GetProperty("source").GetString(), unknown.GetProperty("sourceTag").GetString(), unknown.GetProperty("built").GetBoolean()));
    }

    // 画面は、橋でも push でも同じ JSON（camelCase）で中身を読む。record の名前を変えたら、画面（web/index.html）も変える。置き場のフォルダは渡さない（画面は使わない）。
    [Fact]
    public void ThePageReadsTheCatalogAsCamelCaseJson()
    {
        Install("modules/consumer", "user/modules/consumer");
        var catalog = Load().Catalog;

        Assert.Equal(["restartNeeded", "updatesCheckedAt", "entries"], Snapshot(catalog).EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            [
                "id", "name", "kind", "version", "bundled", "state", "reason", "shared", "enabled", "enabledAtStart", "removing", "canRemove",
                "canToggle", "overrides", "source", "sourceTag", "update", "pending", "built", "pendingCommit",
            ],
            Entry(catalog, "consumer").EnumerateObject().Select(property => property.Name));
    }

    // 次の起動の頭は入れる方が先で、印の付いた前の版ごと入れ替わる。印が残るのは、入れかけにしたときに消せなかったときだけ。
    [Fact]
    public void AnUpdateThatIsQueuedIsNotShownAsARemovalEvenIfTheMarkStayed()
    {
        Install("shared/TestContract", "user/modules/provider/shared");
        var installed = Install("modules/provider", "user/modules/provider");
        File.WriteAllText(Path.Combine(installed, ModuleCatalog.RemoveMarker), string.Empty);
        Assert.True(Entry(Load().Catalog, "provider").GetProperty("removing").GetBoolean());

        var pending = Install("modules/provider", $"user/modules/{ModuleInstaller.PendingDirectoryName}/provider");
        File.WriteAllText(Path.Combine(pending, "module.json"), """{ "id": "provider", "name": "Provider", "version": "1.0.0" }""");

        var entry = Entry(Load().Catalog, "provider");
        Assert.False(entry.GetProperty("removing").GetBoolean());
        Assert.Equal("1.0.0", entry.GetProperty("pending").GetString());
    }

    // GitHub の行き先が手元の差し替え（試験）のときは、build に nuget.org を入れ元として渡さない（試験から本物の nuget.org へ出ない）。ふだんは nuget.org（SDK のパッケージは、いつも同梱のものだけ）。
    [Theory]
    [InlineData("http://127.0.0.1:5000/", null)]
    [InlineData("http://localhost:5000/", null)]
    [InlineData("https://api.github.com/", "https://api.nuget.org/v3/index.json")]
    public void TestsDoNotReachTheRealPackageFeed(string gitHubApi, string? expected) =>
        Assert.Equal(expected, ModuleBuildSetup.PackageFeedFor(new Uri(gitHubApi)));

    private GitHubReleases GitHub() => new(new HttpClient(_github), new Uri("http://127.0.0.1:1/"));

    private ModuleInstaller Installer(string? user = null) => new(user ?? User, Bundled, GitHub(), NullLogger.Instance);

    private ModuleUpdates Updates(string? user = null) => new(user ?? User, GitHub(), _clock, NullLogger.Instance);

    private void Publish(string id, string version, string tag)
    {
        _github.Tag = tag;
        _github.Zip = FakeGitHub.ZipOf(
            ("module.json", JsonSerializer.Serialize(new { id, name = "Sample module", version })), ($"{id}.dll", "x")).ToArray();
    }

    private async Task<StagedModuleView> Stage(ModuleCatalog catalog) =>
        catalog.Describe(await _installer.StageFromGitHubAsync("https://github.com/someone/sample", default));

    private async Task Install(ModuleCatalog catalog) => Assert.True(catalog.ConfirmInstall((await Stage(catalog)).Token));

    private static JsonElement Snapshot(ModuleCatalog catalog)
    {
        using var json = JsonDocument.Parse(UiBroadcast.Json(catalog.Snapshot()));
        return json.RootElement.Clone();
    }

    private static JsonElement Entries(ModuleCatalog catalog) => Snapshot(catalog).GetProperty("entries");

    private static JsonElement Entry(ModuleCatalog catalog, string id) =>
        Entries(catalog).EnumerateArray().Single(e => e.GetProperty("id").GetString() == id);

    private static bool RestartNeeded(ModuleCatalog catalog) => Snapshot(catalog).GetProperty("restartNeeded").GetBoolean();

    private string SavedDisabledModules() =>
        string.Join(",", JsonDocument.Parse(File.ReadAllText(Path.Combine(User, "config.json")))
            .RootElement.GetProperty("DisabledModules").EnumerateArray().Select(e => e.GetString()));

    [Fact]
    public void EachKindOfFolderIsListedWithItsState()
    {
        Install("shared/TestContract", "app/modules/contracts/shared");
        Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "user/modules/consumer");
        Directory.CreateDirectory(Path.Combine(User, "modules", "empty"));
        File.WriteAllText(Path.Combine(User, "modules", "empty", "notes.txt"), "no dll");
        _config.DisabledModules = ["provider"];

        var (_, catalog) = Load();

        Assert.Equal("library", Entry(catalog, "contracts").GetProperty("kind").GetString());
        Assert.Equal("running", Entry(catalog, "contracts").GetProperty("state").GetString());
        Assert.Equal([$"TestContract {BuiltVersion}"], Entry(catalog, "contracts").GetProperty("shared").EnumerateArray().Select(e => e.GetString()));

        var provider = Entry(catalog, "provider");
        Assert.Equal("off", provider.GetProperty("state").GetString());
        Assert.False(provider.GetProperty("enabled").GetBoolean());
        Assert.True(provider.GetProperty("bundled").GetBoolean());
        Assert.False(provider.GetProperty("canRemove").GetBoolean());

        // consumer は provider が居ないので OnAllModulesLoaded で落ちる（契約はライブラリが持ってくるので読めはする）。
        var consumer = Entry(catalog, "consumer");
        Assert.Equal("failed", consumer.GetProperty("state").GetString());
        Assert.False(consumer.GetProperty("bundled").GetBoolean());

        // 理由には例外の文まで出る（型の名前だけでは直せない。文に、取ろうとした名前と直し方がある）。
        var reason = consumer.GetProperty("reason").GetString();
        Assert.Contains("OnAllModulesLoaded threw: InvalidOperationException: No module is registered as 'provider.greeter'", reason);
        Assert.Contains("the module 'provider' is not installed, is turned off, or failed to start", reason);
        Assert.True(consumer.GetProperty("canRemove").GetBoolean());
        Assert.Equal(BuiltVersion, consumer.GetProperty("version").GetString());

        Assert.Equal("broken", Entry(catalog, "empty").GetProperty("state").GetString());
        Assert.False(RestartNeeded(catalog));
    }

    [Fact]
    public void ARefusedModuleShowsWhyAndADependencyItNeeds()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        var provider = Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");
        File.WriteAllText(Path.Combine(provider, ".disabled"), string.Empty);

        var (_, catalog) = Load();

        var consumer = Entry(catalog, "consumer");
        Assert.Equal("refused", consumer.GetProperty("state").GetString());
        Assert.Contains("TestContract", consumer.GetProperty("reason").GetString());

        // 同梱の場所に置かれた印は消せないので、ON にできない。
        Assert.False(Entry(catalog, "provider").GetProperty("canToggle").GetBoolean());
        Assert.False(catalog.SetEnabled("provider", true));
    }

    [Fact]
    public void TurningOffIsSavedAndNeedsARestartUntilItIsTurnedBackOn()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        Install("modules/provider", "app/modules/provider");

        var (_, catalog) = Load();

        Assert.True(catalog.SetEnabled("provider", false));
        Assert.Equal(["provider"], _config.DisabledModules);
        Assert.Equal("provider", SavedDisabledModules());
        Assert.True(RestartNeeded(catalog));
        Assert.True(catalog.RestartNeeded);
        Assert.Contains("modulesChanged", _broadcast.Channels);

        Assert.True(catalog.SetEnabled("provider", true));
        Assert.Empty(_config.DisabledModules);
        Assert.Equal(string.Empty, SavedDisabledModules());
        Assert.False(RestartNeeded(catalog));
    }

    [Fact]
    public void TurningOnAModuleStoppedByItsMarkerRemovesTheMarkerInTheUsersFolder()
    {
        Install("shared/TestContract", "user/modules/provider/shared");
        var provider = Install("modules/provider", "user/modules/provider");
        File.WriteAllText(Path.Combine(provider, ".disabled"), string.Empty);

        var (_, catalog) = Load();
        Assert.Equal("off", Entry(catalog, "provider").GetProperty("state").GetString());

        Assert.True(catalog.SetEnabled("provider", true));

        Assert.False(File.Exists(Path.Combine(provider, ".disabled")));
        Assert.True(RestartNeeded(catalog));
    }

    [Fact]
    public void TurningOffAnIdThatIsNotListedIsRefused()
    {
        var (_, catalog) = Load();

        Assert.False(catalog.SetEnabled("nothing", false));
        Assert.Empty(_config.DisabledModules);
    }

    [Fact]
    public void OnlyAModuleInTheUsersFolderCanBeMarkedForRemoval()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        Install("modules/provider", "app/modules/provider");
        var consumer = Install("modules/consumer", "user/modules/consumer");

        var (_, catalog) = Load();

        Assert.False(catalog.SetRemoval("provider", true));
        Assert.False(File.Exists(Path.Combine(Bundled, "modules", "provider", ModuleCatalog.RemoveMarker)));

        Assert.True(catalog.SetRemoval("consumer", true));
        Assert.True(File.Exists(Path.Combine(consumer, ModuleCatalog.RemoveMarker)));
        Assert.True(Entry(catalog, "consumer").GetProperty("removing").GetBoolean());
        Assert.True(RestartNeeded(catalog));

        Assert.True(catalog.SetRemoval("consumer", false));
        Assert.False(File.Exists(Path.Combine(consumer, ModuleCatalog.RemoveMarker)));
        Assert.False(RestartNeeded(catalog));
    }

    [Fact]
    public void WhenTheUsersFolderIsTheBundledOneNothingCanBeRemoved()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        Install("modules/provider", "app/modules/provider");
        var configs = new ModuleConfigStore(_hostEvents.Hubs, NullLoggerFactory.Instance);
        configs.Add(HostConfig.Id, "Host", null, _config, Bundled);
        var manager = new ModuleManager(new NoServices(), new FakeVrcLogBus(), new FakeVrcLogStore(), new FakeExternalToolProvider(),
            new FakeNotifications(), new FakeClipboard(), new FakeOscBus(), _hostEvents, new UiBroadcast(), configs,
            new ExternalModuleRegistry(NullLoggerFactory.Instance), new Localizer(NullLogger<Localizer>.Instance), _config,
            NullLoggerFactory.Instance, [Bundled], Path.Combine(_root, "module-data"), () => false) { Vr = new() };
        manager.LoadAll();
        var catalog = new ModuleCatalog(manager, _config, configs, _broadcast, new FakeLocalizer(), Installer(Bundled), Updates(Bundled),
            Bundled, Bundled, NullLogger<ModuleCatalog>.Instance);

        Assert.False(Entry(catalog, "provider").GetProperty("canRemove").GetBoolean());
        Assert.False(catalog.SetRemoval("provider", true));
    }

    // 字が違うだけで同じフォルダなので、外せると言ってしまうと、同梱のモジュールが次の起動で消える。
    [Fact]
    public void AnotherSpellingOfTheBundledFolderIsStillTheBundledOne()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        var provider = Install("modules/provider", "app/modules/provider");
        var alias = Path.Combine(_root, "alias");
        Junction(alias, Bundled);

        Assert.True(ModulePaths.SameDirectory(alias, Bundled));
        Assert.False(ModulePaths.SameDirectory(alias, Path.Combine(Bundled, "modules")));
        Assert.False(ModulePaths.SameDirectory(Path.Combine(_root, "nowhere"), Bundled));

        var (_, catalog) = Load(alias);

        var entry = Assert.Single(Entries(catalog).EnumerateArray(), e => e.GetProperty("id").GetString() == "provider");
        Assert.True(entry.GetProperty("bundled").GetBoolean());
        Assert.False(entry.GetProperty("canRemove").GetBoolean());
        Assert.False(entry.GetProperty("overrides").GetBoolean());
        Assert.False(catalog.SetRemoval("provider", true));

        // 印が（手で）置かれていても、起動の頭の片付けは同梱のフォルダに触らない。
        File.WriteAllText(Path.Combine(provider, ModuleCatalog.RemoveMarker), string.Empty);
        Assert.Empty(PendingRemovals.Apply(alias, Bundled, NullLogger.Instance));
        Assert.True(File.Exists(Path.Combine(provider, "ProviderModule.dll")));
    }

    [Fact]
    public void AModuleThatBreaksBeforeItsOnInitIsListedAsNotLoaded()
    {
        Install("modules/brokenconfig", "user/modules/brokenconfig");

        var (manager, catalog) = Load();

        Assert.DoesNotContain(manager.Modules, p => p.Id == "brokenconfig");
        var entry = Entry(catalog, "brokenconfig");
        Assert.Equal("broken", entry.GetProperty("state").GetString());
        Assert.Contains("the config could not be built", entry.GetProperty("reason").GetString());
        Assert.Matches(@"^\d+\.\d+", entry.GetProperty("version").GetString());
    }

    [Fact]
    public async Task AConfirmedInstallIsListedAsPendingUntilTheNextStart()
    {
        Publish("sample", "1.2.0", "v1.2.0");
        var (_, catalog) = Load();

        var staged = await Stage(catalog);
        Assert.Equal(("sample", "Sample module", "1.2.0", "module"), (staged.Id, staged.Name, staged.Version, staged.Kind));
        Assert.Equal(("someone/sample", "v1.2.0"), (staged.Repository, staged.Tag));
        Assert.Equal(64, staged.Sha256.Length);
        Assert.False(staged.Built);
        Assert.False(staged.Replaces);
        Assert.Null(staged.ReplacesVersion);
        Assert.False(staged.ReplacesBundled);
        Assert.Empty(Entries(catalog).EnumerateArray());
        Assert.False(RestartNeeded(catalog));
        Assert.Empty(_broadcast.Channels);

        Assert.True(catalog.ConfirmInstall(staged.Token));

        var entry = Entry(catalog, "sample");
        Assert.Equal("pending", entry.GetProperty("state").GetString());
        Assert.Equal("1.2.0", entry.GetProperty("pending").GetString());
        Assert.Equal("someone/sample", entry.GetProperty("source").GetString());
        Assert.False(entry.GetProperty("canToggle").GetBoolean());
        Assert.False(entry.GetProperty("canRemove").GetBoolean());
        Assert.True(RestartNeeded(catalog));
        Assert.Equal(["modulesChanged"], _broadcast.Channels);
        Assert.False(catalog.SetEnabled("sample", false), "まだ入っていないものは切り替えられない");

        Assert.True(catalog.CancelPending("sample"));
        Assert.Empty(Entries(catalog).EnumerateArray());
        Assert.False(RestartNeeded(catalog));
    }

    [Fact]
    public async Task AnInstallOverAnExistingModuleShowsOnItsRow()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        Install("modules/provider", "app/modules/provider");
        Publish("provider", "9.9.9", "v9.9.9");
        var (_, catalog) = Load();

        var staged = await Stage(catalog);
        Assert.True(staged.Replaces);
        Assert.True(staged.ReplacesBundled);
        Assert.Equal(BuiltVersion, staged.ReplacesVersion);
        Assert.True(catalog.ConfirmInstall(staged.Token));

        var entry = Assert.Single(Entries(catalog).EnumerateArray(), e => e.GetProperty("id").GetString() == "provider");
        Assert.Equal("running", entry.GetProperty("state").GetString());
        Assert.Equal("9.9.9", entry.GetProperty("pending").GetString());
        Assert.True(RestartNeeded(catalog));
    }

    // 更新を入れかけにしたあとで「外す」と、入れかけは取り消される（残ると、外したはずのものが次の起動で新しい版になって入る）。
    [Fact]
    public async Task RemovingTakesBackAQueuedUpdate()
    {
        Install("shared/TestContract", "user/modules/provider/shared");
        Install("modules/provider", "user/modules/provider");
        Publish("provider", "9.9.9", "v9.9.9");
        var (_, catalog) = Load();
        await Install(catalog);
        Assert.Equal("9.9.9", Entry(catalog, "provider").GetProperty("pending").GetString());

        Assert.True(catalog.SetRemoval("provider", true));

        var entry = Entry(catalog, "provider");
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("pending").ValueKind);
        Assert.True(entry.GetProperty("removing").GetBoolean());
        Assert.False(Directory.Exists(Path.Combine(User, "modules", ModuleInstaller.PendingDirectoryName, "provider")));

        await Install(catalog);
        entry = Entry(catalog, "provider");
        Assert.False(entry.GetProperty("removing").GetBoolean());
        Assert.Equal("9.9.9", entry.GetProperty("pending").GetString());
    }

    [Fact]
    public async Task AnInstalledModuleShowsWhereItCameFromAndWhetherThereIsAnUpdate()
    {
        Install("shared/TestContract", "user/modules/provider/shared");
        var provider = Install("modules/provider", "user/modules/provider");
        new ModuleSource(ModuleSource.GitHubKind, "someone/sample", "v1.0.0", "a.zip", "00", DateTimeOffset.UnixEpoch).WriteTo(provider);
        _github.Tag = "v1.2.0";
        var (_, catalog) = Load();

        var before = Entry(catalog, "provider");
        Assert.Equal("someone/sample", before.GetProperty("source").GetString());
        Assert.Equal("v1.0.0", before.GetProperty("sourceTag").GetString());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("update").ValueKind);

        var check = await catalog.CheckUpdatesAsync(force: true, default);

        Assert.Equal(["someone/sample v1.2.0"], check.Found);
        Assert.Equal("v1.2.0", Entry(catalog, "provider").GetProperty("update").GetString());
        Assert.Equal(["modulesChanged"], _broadcast.Channels);
        Assert.False(RestartNeeded(catalog), "更新があるだけでは、起動し直しは要らない");
    }

    [Fact]
    public void ABrokenSourceFileDoesNotTakeTheListDown()
    {
        Install("shared/TestContract", "user/modules/provider/shared");
        var provider = Install("modules/provider", "user/modules/provider");
        File.WriteAllText(Path.Combine(provider, ModuleSource.FileName), "{}");

        var (_, catalog) = Load();

        var entry = Entry(catalog, "provider");
        Assert.Equal("running", entry.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("source").ValueKind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdatesAreCheckedAfterStartingUnlessTurnedOff(bool enabled)
    {
        Install("shared/TestContract", "user/modules/provider/shared");
        var provider = Install("modules/provider", "user/modules/provider");
        new ModuleSource(ModuleSource.GitHubKind, "someone/sample", "v1.0.0", "a.zip", "00", DateTimeOffset.UnixEpoch).WriteTo(provider);
        _config.CheckModuleUpdates = enabled;
        var (_, catalog) = Load();

        await catalog.CheckUpdatesAfterAsync(TimeSpan.Zero, _notify, default);

        Assert.Equal(enabled ? 1 : 0, _github.Releases);
        Assert.Equal(enabled ? 1 : 0, _notify.Toasts.Count);
        if (enabled) Assert.Contains("someone/sample v1.2.0", _notify.Toasts[0].Body);
        Assert.False(Directory.Exists(Path.Combine(User, "modules", ModuleInstaller.PendingDirectoryName)));
    }

    [Fact]
    public void AUserCopyThatShadowsTheBundledOneSaysSo()
    {
        Install("shared/TestContract", "user/modules/provider/shared");
        Install("modules/provider", "user/modules/provider");
        Install("modules/provider", "app/modules/provider");

        var (_, catalog) = Load();

        var provider = Assert.Single(Entries(catalog).EnumerateArray(), e => e.GetProperty("id").GetString() == "provider");
        Assert.True(provider.GetProperty("overrides").GetBoolean());
        Assert.False(provider.GetProperty("bundled").GetBoolean());
    }

    [Theory]
    [InlineData(@"modules\a", true)]
    [InlineData(@"modules\a\b", true)]
    [InlineData(@"modules", false)]
    [InlineData(@"modules\..\a", false)]
    [InlineData(@"modules-other\a", false)]
    public void IsUnderOnlyAcceptsPathsInsideTheFolder(string relative, bool inside)
    {
        Assert.Equal(inside, ModulePaths.IsUnder(Path.Combine(_root, relative), Path.Combine(_root, "modules")));
    }

    [Fact]
    public void PendingRemovalsTakeAwayOnlyMarkedFoldersInTheUsersModules()
    {
        var marked = Path.Combine(User, "modules", "gone");
        var kept = Path.Combine(User, "modules", "kept");
        var bundled = Path.Combine(Bundled, "modules", "bundled");
        foreach (var directory in new[] { marked, kept, bundled }) Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(marked, ModuleCatalog.RemoveMarker), string.Empty);
        File.WriteAllText(Path.Combine(marked, "gone.dll"), "x");
        File.WriteAllText(Path.Combine(bundled, ModuleCatalog.RemoveMarker), string.Empty);

        var removed = PendingRemovals.Apply(User, Bundled, NullLogger.Instance);

        Assert.Equal(["gone"], removed);
        Assert.False(Directory.Exists(marked));
        Assert.True(Directory.Exists(kept));
        Assert.True(Directory.Exists(bundled), "同梱の場所は印があっても消さない");
        Assert.Empty(Directory.GetDirectories(Path.Combine(User, "modules", PendingRemovals.TrashDirectoryName)));
    }

    [Fact]
    public void PendingRemovalsDoNothingWhenTheUsersFolderIsTheBundledOne()
    {
        var marked = Path.Combine(Bundled, "modules", "gone");
        Directory.CreateDirectory(marked);
        File.WriteAllText(Path.Combine(marked, ModuleCatalog.RemoveMarker), string.Empty);

        Assert.Empty(PendingRemovals.Apply(Bundled, Bundled, NullLogger.Instance));
        Assert.True(Directory.Exists(marked));
    }

    // リンクの先は置き場の外のフォルダで、そのまま片付けると、そこにあるフォルダを全部消してしまう。
    [Fact]
    public void ATrashFolderThatIsALinkIsLeftAlone()
    {
        var marked = Path.Combine(User, "modules", "gone");
        Directory.CreateDirectory(marked);
        File.WriteAllText(Path.Combine(marked, ModuleCatalog.RemoveMarker), string.Empty);
        var outside = Path.Combine(_root, "outside");
        var precious = Path.Combine(outside, "precious");
        Directory.CreateDirectory(precious);
        File.WriteAllText(Path.Combine(precious, "keep.txt"), "x");
        Junction(Path.Combine(User, "modules", PendingRemovals.TrashDirectoryName), outside);

        var removed = PendingRemovals.Apply(User, Bundled, NullLogger.Instance);

        Assert.Empty(removed);
        Assert.True(File.Exists(Path.Combine(precious, "keep.txt")), "リンクの先のフォルダを消した");
        Assert.True(Directory.Exists(marked), "リンクの先へ移した");
        Assert.Equal(["precious"], Directory.GetDirectories(outside).Select(Path.GetFileName));
    }

    [Fact]
    public void TheTrashFolderIsNotTakenForAModule()
    {
        Install("shared/TestContract", "user/modules/.trash/old-123/shared");

        var (manager, _) = Load();

        Assert.Empty(manager.Libraries);
        Assert.Empty(manager.Skipped);
    }

    [Fact]
    public void TheRestartPassesTheSameArgumentsAndWhomToWaitFor()
    {
        Assert.Equal(["--x", "--after-pid", "42"], AppRestart.ArgumentsFor(["--x"], 42));
        Assert.Equal(["--x", "--after-pid", "7"], AppRestart.ArgumentsFor(["--after-pid", "3", "--x"], 7));
    }

    /// <summary>e2e のための口: 起動し直す代わりに引数をファイルへ書き、このアプリは閉じない。</summary>
    [Fact]
    public void TheRestartCanBeRecordedInsteadOfStarted()
    {
        var record = Path.Combine(_root, "restart.txt");
        Directory.CreateDirectory(_root);
        var closed = false;
        Environment.SetEnvironmentVariable("SPOITABLE_RESTART_RECORD", record);
        try
        {
            Assert.True(new AppRestart(["--a"], () => closed = true, NullLogger.Instance).Request());
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPOITABLE_RESTART_RECORD", null);
        }

        Assert.Equal(["--a", "--after-pid", Environment.ProcessId.ToString()], File.ReadAllLines(record));
        Assert.False(closed);
    }

    // 番号は使い回されるので、別のプログラムの PID なら待たない。
    [Fact]
    public void WaitingForAnotherProgramReturnsAtOnce()
    {
        using var other = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 6 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            var watch = Stopwatch.StartNew();
            AppRestart.WaitForPrevious(["--after-pid", other.Id.ToString()]);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"waited {watch.Elapsed}");
        }
        finally
        {
            other.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void WaitingForThePreviousAppLastsUntilItHasExited()
    {
        using var previous = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            AppRestart.WaitForPrevious(["--after-pid", previous.Id.ToString()], "cmd", TimeSpan.FromSeconds(30));

            Assert.True(previous.HasExited, "前のアプリが抜ける前に戻った");
        }
        finally
        {
            if (!previous.HasExited) previous.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void WaitingForThePreviousAppGivesUpAfterTheLimit()
    {
        using var previous = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            var watch = Stopwatch.StartNew();
            AppRestart.WaitForPrevious(["--after-pid", previous.Id.ToString()], "cmd", TimeSpan.FromMilliseconds(300));

            Assert.False(previous.HasExited);
            Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(10));
        }
        finally
        {
            previous.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void WaitingWithoutTheArgumentOrForAGoneProcessReturnsAtOnce()
    {
        var watch = Stopwatch.StartNew();
        AppRestart.WaitForPrevious([]);
        AppRestart.WaitForPrevious(["--after-pid"]);
        AppRestart.WaitForPrevious(["--after-pid", "not-a-number"]);
        AppRestart.WaitForPrevious(["--after-pid", int.MaxValue.ToString()]);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
    }
}
