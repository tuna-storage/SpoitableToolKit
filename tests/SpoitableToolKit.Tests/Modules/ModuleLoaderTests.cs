using SpoitableToolKit.Abstractions.Events.Video;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Host.Config;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using BlobBuilder = System.Reflection.Metadata.BlobBuilder;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Localization;
using SpoitableToolKit.Host.ExternalModules;
using SpoitableToolKit.Host.Modules;
using SpoitableToolKit.Host.Modules.Loading;
using SpoitableToolKit.Host.Modules.Loading.Models;
using SpoitableToolKit.Host.Modules.Packaging;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Push;
using SpoitableToolKit.Host.Ui.Shell;
using SpoitableToolKit.Tests.Fakes;
using Xunit;

namespace SpoitableToolKit.Tests.Modules;

[Collection("module-loader")]
public sealed class ModuleLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "stk-tests", Guid.NewGuid().ToString("N"));

    private readonly HostEvents _hostEvents = new(NullLoggerFactory.Instance);

    private readonly FakeNotifications _notify = new();

    public void Dispose()
    {
        // 読み込んだ dll はメモリへ載せている（ファイルをロックしない）ので、そのまま消せる。
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>見本の <c>OnShutdown</c> が呼び戻す合図を <see cref="Action{T}"/> で出す（BCL の型なので ALC をまたいでも同一。試験用の契約を <c>shared/</c> に置くと、どちらのコピーが使われるかが実行順で変わる）。</summary>
    private sealed class NoServices(Action<string>? onModuleShutdown = null) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(Action<string>) ? onModuleShutdown : null;
    }

    /// <summary>棚を外から読む口が無いので、畳む手順の順番は「その瞬間に配ったら届くか」で見る。</summary>
    private sealed class DispatchDuringShutdown(HostEvents events)
    {
        public List<string> Calls { get; } = [];

        public Action<string> Probe => moduleId =>
        {
            Calls.Add(moduleId);
            FireVideoFailed(events);
        };
    }

    private string Install(string fixture, string destination)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "fixtures", fixture);

        // ローダが返すパスは区切りが '\' なので、比較できるようにここで正規化しておく。
        var target = Path.GetFullPath(Path.Combine(_root, destination));

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy, overwrite: true);
        }

        return target;
    }

    private ModuleManager NewManager(
        IReadOnlyList<string>? roots = null, HostConfig? config = null, Action<string>? probe = null, Func<bool>? vrchatRunning = null,
        Localizer? text = null,
        Func<string, (string Name, Version Version, IReadOnlyList<AssemblyReference> References)?>? readEntryMetadata = null,
        VrControlsSlot? vr = null) =>
        new(new NoServices(probe),
            new FakeVrcLogBus(),
            new FakeVrcLogStore(),
            new FakeExternalToolProvider(),
            _notify,
            new FakeClipboard(),
            new FakeOscBus(),
            _hostEvents,

            // push の宛先。繋がっているサーフェスが 0 個なら黙って捨てられるので、素で渡してよい。
            new UiBroadcast(),
            new ModuleConfigStore(_hostEvents.Hubs, NullLoggerFactory.Instance),
            new ExternalModuleRegistry(NullLoggerFactory.Instance),

            // 文言は読み込まない（辞書を置いていないので、キーがそのまま返る）。
            text ?? new Localizer(NullLogger<Localizer>.Instance),
            config ?? new HostConfig(),
            NullLoggerFactory.Instance,
            roots ?? [Path.Combine(_root, "app")],
            Path.Combine(_root, "module-data"),
            vrchatRunning ?? (() => false))
        {
            ReadEntryMetadata = readEntryMetadata ?? AssemblyMetadata.Read,
            Vr = vr ?? new(),
        };

    private string ModuleData(string id) => Path.Combine(_root, "module-data", id);

    [Fact]
    public void AContractMatchesAcrossLoadContexts()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");

        var manager = NewManager();
        manager.LoadAll();

        // 別々の ALC から見た IGreeter が同一の型なら、consumer は provider の契約を取れる。
        var greeting = Path.Combine(ModuleData("consumer"), "greeting.txt");
        Assert.True(File.Exists(greeting), "consumer が契約を取れていない");
        Assert.Equal("hello from provider via TestContract", File.ReadAllText(greeting));

        Assert.All(manager.Modules, p => Assert.Equal(ModuleState.Running, p.State));
        Assert.Equal(2, manager.Modules.Count);
    }

    [Fact]
    public void EachPhaseFinishesForEveryModuleBeforeTheNext()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");

        var manager = NewManager();
        manager.LoadAll();

        // consumer は provider より先に読み込まれる（ディレクトリ名の順）。それでも
        // OnAllModulesLoaded の時点では provider の OnPostInit が済んでいる、というのが約束。
        Assert.Equal("consumer", manager.Modules[0].Id);
        Assert.All(manager.Modules, p => Assert.Equal(nameof(ISpoitableModule.OnAllModulesLoaded), p.Phase));
    }

    [Fact]
    public void OneFailingModuleLeavesTheOthersRunning()
    {
        // provider だけ無効化する。契約はライブラリが持ってくるので consumer は読めるが、
        // 必須依存を取れずに OnAllModulesLoaded で落ちるはず。
        Install("shared/TestContract", "app/modules/contracts/shared");
        var provider = Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");
        File.WriteAllText(Path.Combine(provider, ".disabled"), string.Empty);

        var manager = NewManager();
        manager.LoadAll();

        var consumer = Assert.Single(manager.Modules);
        Assert.Equal("consumer", consumer.Id);
        Assert.Equal(ModuleState.Failed, consumer.State);
        Assert.Contains("OnAllModulesLoaded", consumer.Failure);
    }

    [Fact]
    public void AModuleCanBeDisabledInTheSettings()
    {
        // 同梱のモジュールは読み取り専用の場所にありうるので、.disabled を置けないことがある。
        Install("shared/TestContract", "app/modules/provider/shared");
        Install("modules/provider", "app/modules/provider");

        var manager = NewManager(config: new HostConfig { DisabledModules = ["provider"] });
        manager.LoadAll();

        Assert.Empty(manager.Modules);
    }

    [Fact]
    public void WhenAnIdExistsTwiceTheUsersCopyWins()
    {
        Install("shared/TestContract", "user/modules/provider/shared");
        var bundled = Install("modules/provider", "app/modules/provider");
        var user = Install("modules/provider", "user/modules/provider");

        // 実行時と同じ順（ユーザー → 同梱）で渡す。
        var manager = NewManager([Path.Combine(_root, "user"), Path.Combine(_root, "app")]);
        manager.LoadAll();

        var module = Assert.Single(manager.Modules);
        Assert.Equal(user, module.Candidate.Directory);
        Assert.NotEqual(bundled, module.Candidate.Directory);
    }

    [Fact]
    public void AMissingPanelFileFallsBackToNoPanel()
    {
        // consumer は PanelEntry を宣言しているが web/ を同梱していない。
        // 宣言だけ信じると、サイドバーに項目が出るのに中身が 404 になる。
        Install("shared/TestContract", "app/modules/provider/shared");
        Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");

        var manager = NewManager();
        manager.LoadAll();

        var consumer = manager.Modules.Single(p => p.Id == "consumer");
        Assert.Null(consumer.PanelEntry);
        Assert.Null(manager.ResolveWebRoot("consumer"));
    }

    /// <summary>受けたかどうかは台帳が決めて、読み込みの後に立てる（<see cref="SpoitableToolKit.Host.Modules.LoadedModule.ServesTilePages"/>）。</summary>
    [Fact]
    public void AModuleWithoutAPanelIsServedOnceItsTilePageIsTaken()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        Install("modules/provider", "app/modules/provider");
        var consumer = Install("modules/consumer", "app/modules/consumer");

        var manager = NewManager();
        manager.LoadAll();

        var module = manager.Modules.Single(p => p.Id == "consumer");
        Assert.Null(module.PanelEntry);
        Assert.Null(manager.ResolveWebRoot("consumer"));

        module.ServesTilePages = true;

        Assert.Equal(Path.Combine(consumer, "web"), manager.ResolveWebRoot("consumer"));
    }

    /// <summary>読み込んだあとに変わっても、次に読めば新しい値。</summary>
    [Fact]
    public void AModuleReadsWhetherVRChatIsRunningFromItsHost()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");
        var running = true;

        var manager = NewManager(vrchatRunning: () => running);
        manager.LoadAll();
        var host = manager.Modules.Single(p => p.Id == "consumer").Host;

        Assert.True(host.IsVRChatRunning);
        running = false;
        Assert.False(host.IsVRChatRunning);
    }

    [Fact]
    public void APresentPanelFileResolvesItsOrigin()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        Install("modules/provider", "app/modules/provider");
        var consumer = Install("modules/consumer", "app/modules/consumer");
        Directory.CreateDirectory(Path.Combine(consumer, "web"));
        File.WriteAllText(Path.Combine(consumer, "web", "index.html"), "<!doctype html>");

        var manager = NewManager();
        manager.LoadAll();

        Assert.Equal("index.html", manager.Modules.Single(p => p.Id == "consumer").PanelEntry);
        Assert.Equal(Path.Combine(consumer, "web"), manager.ResolveWebRoot("consumer"));
    }

    [Fact]
    public void APresentVrPanelFileIsListed()
    {
        var consumer = InstallConsumerWith("index.html", "vr.html");

        var manager = NewManager();
        manager.LoadAll();

        var module = manager.Modules.Single(p => p.Id == "consumer");
        Assert.Equal("index.html", module.PanelEntry);
        Assert.Equal("vr.html", module.VrPanelEntry);
        Assert.Equal(Path.Combine(consumer, "web"), manager.ResolveWebRoot("consumer"));
    }

    /// <summary>宣言だけで実体が無いときに、中身が 404 の窓にしない。</summary>
    [Fact]
    public void AMissingVrPanelFileFallsBackToTheUsualPanel()
    {
        InstallConsumerWith("index.html");

        var manager = NewManager();
        manager.LoadAll();

        var module = manager.Modules.Single(p => p.Id == "consumer");
        Assert.Equal("index.html", module.PanelEntry);
        Assert.Null(module.VrPanelEntry);
    }

    /// <summary>サイドバーに項目が出ないので、空間の窓でも開く口が無い。</summary>
    [Fact]
    public void AVrPanelWithoutTheUsualPanelIsNotListed()
    {
        InstallConsumerWith("vr.html");

        var manager = NewManager();
        manager.LoadAll();

        var module = manager.Modules.Single(p => p.Id == "consumer");
        Assert.Null(module.PanelEntry);
        Assert.Null(module.VrPanelEntry);
    }

    /// <summary>consumer は <c>RegisterPanel("index.html")</c> するが、<c>web/index.html</c> が無ければ本体はパネル無しに倒すので、頼まない（中が 404 の窓を開かない）。頼む先は、アプリが VR の周を立てたときに挿す差し込み口（<see cref="VrControlsSlot"/>）。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TogglingTheVrPanelAsksTheWindowOnlyWhenThePanelIsShown(bool panelFilePresent)
    {
        if (panelFilePresent) InstallConsumerWith("index.html");
        else InstallConsumerWith();

        var vr = new RecordingVrControls();
        var manager = NewManager(vr: new VrControlsSlot { Current = vr });
        manager.LoadAll();

        var ui = manager.Modules.Single(p => p.Id == "consumer").Ui;
        ui.ToggleVrPanel();
        ui.ToggleVrPanel("stats");

        string[] expected = panelFilePresent ? ["consumer", "consumer#stats"] : [];
        Assert.Equal(expected, vr.Toggled);
    }

    private sealed class RecordingVrControls : IVrControls
    {
        public List<string> Toggled { get; } = [];

        public void ToggleWindow(string view) => Toggled.Add(view);

        public void CloseWindow() => throw new NotSupportedException();

        public void WindowViewing(string view) => throw new NotSupportedException();

        public string? WindowView => null;

        public void ResetWristPosition() => throw new NotSupportedException();

        public void SetWristLayout(bool on) => throw new NotSupportedException();

        public bool InLayout => false;

        public void ResizeWrist(int steps) => throw new NotSupportedException();
    }

    private string InstallConsumerWith(params string[] pages)
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        Install("modules/provider", "app/modules/provider");
        var consumer = Install("modules/consumer", "app/modules/consumer");

        Directory.CreateDirectory(Path.Combine(consumer, "web"));

        foreach (var page in pages) File.WriteAllText(Path.Combine(consumer, "web", page), "<!doctype html>");

        return consumer;
    }

    [Fact]
    public async Task ACallFromTheUiReachesItsTargetModule()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");

        var manager = NewManager();
        manager.LoadAll();

        // consumer は呼ばれる口を別のクラスにして登録している（モジュールのクラスは IUiEndpoint を実装していない）。
        var result = await manager.CallUiAsync("consumer", "ping", """{"n":1}""");
        Assert.Equal("""{"method":"ping","payload":{"n":1}}""", result);

        // provider は IUiEndpoint を実装しているが登録していないので、呼べない。何をすればよいかが文に出る。
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.CallUiAsync("provider", "ping", "{}"));
        Assert.Equal(
            "Module 'provider' takes no calls from its pages: it registered no endpoint. " +
            "Call host.Ui.RegisterEndpoint(...) in OnInit with the class that implements IUiEndpoint.", error.Message);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CallUiAsync("nope", "ping", "{}"));
    }

    /// <summary>本体が使うのは OnInit で <c>host.Ui</c> に登録したものだけ。provider は <c>IUiEndpoint</c>・<c>IContextMenuSource</c> を実装していて <c>web/index.html</c> も持っているが、何も登録していないので、パネルも右クリックも様子も無い。</summary>
    [Fact]
    public void AModuleThatOnlyImplementsTheUiInterfacesGetsNothing()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        var provider = Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");
        Directory.CreateDirectory(Path.Combine(provider, "web"));
        File.WriteAllText(Path.Combine(provider, "web", "index.html"), "<!doctype html>");

        var manager = NewManager();
        manager.LoadAll();

        var module = manager.Modules.Single(p => p.Id == "provider");
        Assert.Equal(ModuleState.Running, module.State);
        Assert.Null(module.PanelEntry);
        Assert.Null(manager.ResolveWebRoot("provider"));
        Assert.Null(manager.ContextMenuOf("provider"));
        Assert.Null(module.Ui.Status);
    }

    /// <summary>右クリックを引く口（<see cref="ModuleManager.ContextMenuOf"/>）は、アプリがメニューに渡しているものと同じ。</summary>
    [Fact]
    public void WhatAModuleRegistersInOnInitIsWhatTheAppUses()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");

        var manager = NewManager();
        manager.LoadAll();

        var menu = manager.ContextMenuOf("consumer");
        Assert.NotNull(menu);
        var item = Assert.Single(menu.GetContextMenu(new ContextMenuTarget { Key = "page" }));
        Assert.Equal("consumer-item", item.Id);

        Assert.Equal("consumer status", manager.Modules.Single(p => p.Id == "consumer").Ui.Status?.Text);
    }

    /// <summary>本体は OnInit が返った直後に何を出すかを決めるので、あとから来たものは出せない。consumer は OnAllModulesLoaded で様子を登録しようとして、断られた文を残す。</summary>
    [Fact]
    public void ARegistrationAfterOnInitIsRefused()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");

        var manager = NewManager();
        manager.LoadAll();

        var refused = File.ReadAllText(Path.Combine(ModuleData("consumer"), "late-register.txt"));
        Assert.StartsWith("consumer called host.Ui.RegisterStatus outside OnInit.", refused);

        var lateShortcut = File.ReadAllText(Path.Combine(ModuleData("consumer"), "late-shortcut.txt"));
        Assert.StartsWith("consumer called host.Shortcuts.Register outside OnInit.", lateShortcut);

        var consumer = manager.Modules.Single(p => p.Id == "consumer");
        Assert.Equal("consumer status", consumer.Ui.Status?.Text);
        Assert.Equal(["ping"], consumer.Shortcuts.Registered.Select(r => r.Id));
    }

    [Fact]
    public void HavingNoModulesAtAllIsNotAnError()
    {
        Directory.CreateDirectory(Path.Combine(_root, "app"));

        var manager = NewManager();
        manager.LoadAll();

        Assert.Empty(manager.Modules);
    }

    // consumer は listener を別クラス（VideoWatcher）にして OnInit で登録している。
    // 届いたことはデータディレクトリのファイルで観測する（別 ALC の中は覗けないため）。

    private static readonly VideoPlaybackInfo Failure = new()
    {
        Url = "https://example.invalid/v",
        AttemptCount = 1,
        Timestamp = DateTimeOffset.UnixEpoch,
    };

    private static void FireVideoFailed(HostEvents events) =>
        events.Videos.Fire<IVideoEventListener>(l => l.OnVideoPlaybackFailed(new VideoPlaybackFailedEventParams { Video = Failure, IsRepeatedFail = false }));

    private bool WasNotified(string id) => File.Exists(Path.Combine(ModuleData(id), "video-failed.txt"));

    [Fact]
    public void AListenerInAnotherClassAlsoReceivesAcrossLoadContexts()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");

        var manager = NewManager();
        manager.LoadAll();
        FireVideoFailed(_hostEvents);

        Assert.True(WasNotified("consumer"));
    }

    [Fact]
    public void StoppingAlsoRemovesTheListeners()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");

        var manager = NewManager();
        manager.LoadAll();
        manager.Shutdown();
        FireVideoFailed(_hostEvents);

        Assert.False(WasNotified("consumer"));
    }

    /// <summary>外さないと、死んだモジュールの listener がイベントを受け取り続ける。</summary>
    [Fact]
    public void AFailedModulesListenersAreRemovedToo()
    {
        // 契約はライブラリが持ってくるので、provider を無効化しても consumer は読める。
        // provider が居ないので、consumer は OnInit を通ったあと OnAllModulesLoaded で落ちる。
        Install("shared/TestContract", "app/modules/contracts/shared");
        var provider = Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");
        File.WriteAllText(Path.Combine(provider, ".disabled"), string.Empty);

        var manager = NewManager();
        manager.LoadAll();
        Assert.Equal(ModuleState.Failed, Assert.Single(manager.Modules).State);

        FireVideoFailed(_hostEvents);

        Assert.False(WasNotified("consumer"));
    }

    /// <summary>畳まれる途中のモジュールは、まだ自分の listener でイベントを受け取れる（後片付けの最中に届いたものを取りこぼさないため）。この順番は <see cref="ModuleManager.Shutdown"/> と失敗時とで意図的に逆にしてある。</summary>
    [Fact]
    public void OnACleanShutdownListenersStayAliveThroughOnShutdown()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");

        var probe = new DispatchDuringShutdown(_hostEvents);
        var manager = NewManager(probe: probe.Probe);
        manager.LoadAll();

        // 畳む前には何も配っていない。ここが偽の緑になっていないことの確認。
        Assert.False(WasNotified("consumer"));

        manager.Shutdown();

        Assert.Equal(["consumer"], probe.Calls);
        Assert.True(WasNotified("consumer"), "OnShutdown の最中なのに listener が既に外れている");
    }

    /// <summary>落ちたのに listener が生きていると、後片付けの最中にまた同じ経路を踏んで二重に落ちる。正常系と逆になっているのがこの仕組みの肝。</summary>
    [Fact]
    public void AFailedModuleGetsNothingEvenDuringOnShutdown()
    {
        // 契約はライブラリが持ってくるので、provider を無効化しても consumer は読める。
        // provider が居ないので、consumer は OnInit を通ったあと OnAllModulesLoaded で落ちる。
        Install("shared/TestContract", "app/modules/contracts/shared");
        var provider = Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");
        File.WriteAllText(Path.Combine(provider, ".disabled"), string.Empty);

        var probe = new DispatchDuringShutdown(_hostEvents);
        var manager = NewManager(probe: probe.Probe);
        manager.LoadAll();

        Assert.Equal(ModuleState.Failed, Assert.Single(manager.Modules).State);

        // OnShutdown 自体は呼ばれている（掴んだものを離す機会は与える）。配られないだけ。
        Assert.Equal(["consumer"], probe.Calls);
        Assert.False(WasNotified("consumer"), "失敗したのに listener がまだ棚に残っている");
    }

    [Fact]
    public void AContractALibraryBringsIsSharedWithEveryModule()
    {
        Install("shared/TestContract", "app/modules/contracts/shared");
        Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");

        var manager = NewManager();
        manager.LoadAll();

        Assert.Equal("hello from provider via TestContract", File.ReadAllText(Path.Combine(ModuleData("consumer"), "greeting.txt")));
        Assert.Equal("contracts", Assert.Single(manager.Libraries).Id);
        Assert.Equal("contracts", manager.SharedFiles["TestContract"].Owner);
        Assert.Equal(2, manager.Modules.Count);
        Assert.All(manager.Modules, p => Assert.Equal(ModuleState.Running, p.State));

        // 共有に載せた dll は既定のコンテキストに居るが、本体の物としては数えない（「アプリが古い」と言う相手ではない）。
        Assert.Contains(System.Runtime.Loader.AssemblyLoadContext.Default.Assemblies, a => a.GetName().Name == "TestContract");
        Assert.Null(ModuleUnitReader.HostVersion("TestContract"));
    }

    /// <summary>契約を持ってくるのが provider だけなら、consumer は読む前に止まり、理由（何が見つからないか）が利用者にバナーで届く。</summary>
    [Fact]
    public void AContractOfADisabledModuleIsNotSharedSoItsUserIsNotLoaded()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        var provider = Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");
        File.WriteAllText(Path.Combine(provider, ".disabled"), string.Empty);

        var text = new Localizer(NullLogger<Localizer>.Instance);
        text.Load("ja", [LocaleSource.Host]);
        var manager = NewManager(text: text);
        manager.LoadAll();

        Assert.Empty(manager.Modules);
        Assert.Empty(manager.SharedFiles);
        Assert.Equal(SkipReason.Disabled, Assert.Single(manager.Skipped, s => s.Id == "provider").Reason);

        var consumer = Assert.Single(manager.Skipped, s => s.Id == "consumer");
        Assert.Equal(SkipReason.MissingDependency, consumer.Reason);
        Assert.Equal(DependencyProblemKind.Missing, consumer.Problem!.Kind);
        Assert.Equal("TestContract", consumer.Problem.Needed.Name);

        var banner = Assert.Single(_notify.Banners);
        Assert.Equal("consumer の読み込みに失敗しました", banner.Title);
        Assert.StartsWith($"TestContract {consumer.Problem.Needed.Version} が見つかりません", banner.Body);
    }

    /// <summary>OnInit の前に足すときも、全部を読み込んだ後の読み直しでも、モジュールの Id を持ち主にする。自分の Id で始まらないキー（本体のキーを含む）は入らない。</summary>
    [Fact]
    public void AModulesDictionaryIsReadUnderItsOwnId()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        var provider = Install("modules/provider", "app/modules/provider");
        Directory.CreateDirectory(Path.Combine(provider, "locales"));
        File.WriteAllText(Path.Combine(provider, "locales", "ja.json"),
            """{ "provider.hello": "こんにちは", "title": "題", "common.ui.close": "とじる" }""");

        var text = new Localizer(NullLogger<Localizer>.Instance);
        text.Load("ja", [LocaleSource.Host]);
        var manager = NewManager(text: text);
        manager.LoadAll();
        Assert.Equal(ModuleState.Running, Assert.Single(manager.Modules).State);

        void OnlyItsOwnKeyWentIn()
        {
            Assert.Equal("こんにちは", text["provider.hello"]);
            Assert.Equal("title", text["title"]);
            Assert.Equal("閉じる", text[CommonPhrases.Ui.Close]);
        }

        OnlyItsOwnKeyWentIn();

        // 読み直し（Program が読み込みの後で、設定の画面が言語を変えたときに呼ぶ）。
        Assert.Equal(
            [LocaleSource.Host, new LocaleSource(provider, "provider")],
            manager.LocaleSources);
        text.Load("ja", manager.LocaleSources);

        OnlyItsOwnKeyWentIn();
    }

    [Fact]
    public void AModuleThatTakesAnIdOfTheAppIsNotLoaded()
    {
        Install("modules/host", "app/modules/host");

        var manager = NewManager();
        manager.LoadAll();

        Assert.Empty(manager.Modules);
        var skipped = Assert.Single(manager.Skipped);
        Assert.Equal(SkipReason.LoadFailed, skipped.Reason);
        Assert.Equal(
            "The Id 'host' is reserved for the app itself ('host' and 'common', and any Id that starts with 'host.' or 'common.'). " +
            "Give the module an Id of its own, for example its name in lower case.",
            skipped.Detail);
    }

    /// <summary>Id と名前は build が作った <c>ModuleInfo</c> から来る。</summary>
    [Fact]
    public void AModuleBuiltByTheSdkLoadsUnderTheIdItsProjectGives()
    {
        Install("sdk/sample", "app/modules/sample");

        var manager = NewManager();
        manager.LoadAll();

        Assert.Empty(manager.Skipped);
        var module = Assert.Single(manager.Modules);
        Assert.Equal(("sample", "Sample", ModuleState.Running), (module.Id, module.Instance.DisplayName, module.State));
    }

    /// <summary>dll はメモリから読むので <c>Assembly.Location</c> は空で、フォルダは読み込んだ <c>AssemblyLoadContext</c> が知っている。入れた先の写しだけを書き換えて、配る前の置き場（fixtures）ではなく読み込んだフォルダから読んだことを見る。</summary>
    [Fact]
    public void AModuleOpensAFileItShipsFromTheFolderItWasLoadedFrom()
    {
        var folder = Install("sdk/sample", "app/modules/sample");
        File.WriteAllText(Path.Combine(folder, "data", "shipped.txt"), "the installed copy");

        var manager = NewManager();
        manager.LoadAll();

        var module = Assert.Single(manager.Modules);
        var read = module.Instance.GetType().GetMethod("ReadShippedFile")!.Invoke(module.Instance, null);
        Assert.Equal("the installed copy", read);
        Assert.Equal("", module.Instance.GetType().Assembly.Location);
    }

    /// <summary>入れる・外す・OFF はフォルダ名で、設定・記録・翻訳は名乗った Id で当たるので、違うと別々の物を指す。置き場（module-data）も作らない。</summary>
    [Fact]
    public void AModuleThatReportsAnIdOtherThanItsFolderNameIsNotLoaded()
    {
        Install("sdk/sample", "app/modules/renamed");

        var manager = NewManager();
        manager.LoadAll();

        Assert.Empty(manager.Modules);
        var skipped = Assert.Single(manager.Skipped);
        Assert.Equal(("renamed", SkipReason.LoadFailed), (skipped.Id, skipped.Reason));
        Assert.Equal(
            "The module in the folder 'renamed' reports the Id 'sample'. The folder name and ISpoitableModule.Id must be the same " +
            "(the app finds a module's settings, records and translations by its Id, and turns it off or removes it by its folder). " +
            "ISpoitableModule.Id returns the Id the SDK's build wrote into the dll from <SpoitableModuleId> in the csproj, which also names the folder: " +
            "do not write Id in the module class, and do not rename the folder by hand.",
            skipped.Detail);
        Assert.False(Directory.Exists(ModuleData("sample")));
    }

    /// <summary>フォルダ名の順で先に来た写しが Id を取り、本物の方が「重なっている」で外れないこと。</summary>
    [Fact]
    public void ACopyLeftUnderAnotherFolderNameDoesNotTakeThePlaceOfTheRealOne()
    {
        Install("sdk/sample", "app/modules/aaa");
        var real = Install("sdk/sample", "app/modules/sample");

        var manager = NewManager();
        manager.LoadAll();

        var module = Assert.Single(manager.Modules);
        Assert.Equal(("sample", real, ModuleState.Running), (module.Id, module.Candidate.Directory, module.State));
        var skipped = Assert.Single(manager.Skipped);
        Assert.Equal(("aaa", SkipReason.LoadFailed), (skipped.Id, skipped.Reason));
    }

    /// <summary>大文字小文字だけの違いは問わない（Windows のフォルダ名は区別しない）。</summary>
    [Fact]
    public void AFolderNameThatDiffersFromTheIdOnlyInCaseIsLoaded()
    {
        Install("sdk/sample", "app/modules/Sample");

        var manager = NewManager();
        manager.LoadAll();

        Assert.Empty(manager.Skipped);
        Assert.Equal(("sample", ModuleState.Running), (Assert.Single(manager.Modules).Id, manager.Modules[0].State));
    }

    /// <summary><see cref="ModuleUnitReader.HostVersion"/> は本体が持っている版を答える（持っていない名前は null → 「見つからない」の方になる）。</summary>
    [Fact]
    public void WhenTheAppIsTooOldForAModuleTheReasonSaysToUpdateTheApp()
    {
        var text = new Localizer(NullLogger<Localizer>.Instance);
        text.Load("ja", [LocaleSource.Host]);
        var manager = NewManager(text: text);

        var reason = manager.Describe(new DependencyProblem(DependencyProblemKind.HostTooOld,
            new AssemblyReference("SpoitableToolKit.Abstractions", new Version(2027, 1, 0, 0)), From: null, new Version(2026, 11, 0, 0)));

        Assert.Equal("SpoitableToolKit.Abstractions 2027.1.0.0が必要です。しかし、このアプリは2026.11.0.0のため、アプリの更新が必要です", reason);
        Assert.Equal(typeof(ISpoitableModule).Assembly.GetName().Version, ModuleUnitReader.HostVersion("SpoitableToolKit.Abstractions"));
        Assert.Null(ModuleUnitReader.HostVersion("No.Such.Assembly.Anywhere"));
    }

    [Fact]
    public void WhenAModuleWasBuiltWithAnOldSdkTheReasonSaysToBuildItAgain()
    {
        var text = new Localizer(NullLogger<Localizer>.Instance);
        text.Load("ja", [LocaleSource.Host]);
        var manager = NewManager(text: text);

        var reason = manager.Describe(new DependencyProblem(DependencyProblemKind.SdkTooOld,
            new AssemblyReference("SpoitableToolKit.Abstractions", new Version(2026, 10, 0, 0)), From: null, new Version(2026, 11, 0, 0)));

        Assert.Equal("このモジュールは古いSDK (2026.10.0) で作成されています。2026.11.0 以降に更新するか、開発元に新しいSDKでの作り直しを依頼してください", reason);
    }

    /// <summary>古い SDK で作った本物の dll は作れないので、参照だけを持つ dll を書き出す（読み込まないので、中身は要らない）。</summary>
    [Fact]
    public void AModuleBuiltWithAnOldSdkIsRefusedBeforeItIsLoaded()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "app", "modules", "oldsdk")).FullName;
        WriteDllReferencing(Path.Combine(folder, "oldsdk.dll"), "oldsdk", new AssemblyReference(ModuleDependencies.AppApi, new Version(2000, 1, 0, 0)));

        var manager = NewManager();
        manager.LoadAll();

        var skipped = Assert.Single(manager.Skipped);
        Assert.Equal(("oldsdk", SkipReason.MissingDependency), (skipped.Id, skipped.Reason));
        Assert.Equal(DependencyProblemKind.SdkTooOld, skipped.Problem!.Kind);
        Assert.Equal(ModuleManifest.MinimumSdk, skipped.Problem.Available);
        Assert.Empty(manager.Modules);
    }

    /// <summary>読み込む前に dll が開けなかった（ほかのプログラムが掴んでいたなど）ときも、古い SDK で作ったものは読み込んだあとの参照で断る（型を探す・作るより前に）。</summary>
    [Fact]
    public void AModuleBuiltWithAnOldSdkIsRefusedEvenWhenItsFileCouldNotBeReadBeforeLoading()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "app", "modules", "oldsdk")).FullName;
        WriteDllReferencing(Path.Combine(folder, "oldsdk.dll"), "oldsdk", new AssemblyReference(ModuleDependencies.AppApi, new Version(2000, 1, 0, 0)));

        var manager = NewManager(readEntryMetadata: _ => null);
        manager.LoadAll();

        var skipped = Assert.Single(manager.Skipped);
        Assert.Equal(("oldsdk", SkipReason.MissingDependency), (skipped.Id, skipped.Reason));
        Assert.Equal(DependencyProblemKind.SdkTooOld, skipped.Problem!.Kind);
        Assert.Equal(ModuleManifest.MinimumSdk, skipped.Problem.Available);
        Assert.Empty(manager.Modules);
    }

    /// <summary>試験が「開けなかった」を作る口で読む。この口を通らないと、上の試験は「読み込む前に断った」のか「読み込んだあとで断った」のかを見分けられない。</summary>
    [Fact]
    public void TheCheckBeforeLoadingReadsTheEntryThroughItsHook()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "app", "modules", "oldsdk")).FullName;
        var entry = Path.Combine(folder, "oldsdk.dll");
        WriteDllReferencing(entry, "oldsdk", new AssemblyReference(ModuleDependencies.AppApi, new Version(2000, 1, 0, 0)));

        List<string> asked = [];
        var manager = NewManager(readEntryMetadata: path =>
        {
            asked.Add(path);
            return null;
        });
        manager.LoadAll();

        Assert.Contains(asked, path => string.Equals(Path.GetFullPath(path), Path.GetFullPath(entry), StringComparison.OrdinalIgnoreCase));
    }

    private static void WriteDllReferencing(string path, string name, params AssemblyReference[] references)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString(name + ".dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(name), new Version(1, 0, 0, 0), default, default, default, AssemblyHashAlgorithm.Sha1);

        foreach (var reference in references)
            metadata.AddAssemblyReference(metadata.GetOrAddString(reference.Name), reference.Version, default, default, default, default);

        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

        var image = new BlobBuilder();
        new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new MetadataRootBuilder(metadata), new BlobBuilder()).Serialize(image);

        using var file = File.Create(path);
        image.WriteContentTo(file);
    }

    [Fact]
    public void TheOldSharedFolderAtTheRootIsNotRead()
    {
        Install("shared/TestContract", "app/shared/TestContract");
        Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");

        var manager = NewManager();
        manager.LoadAll();

        Assert.Empty(manager.Modules);
        Assert.Empty(manager.SharedFiles);
        Assert.All(manager.Skipped, s => Assert.Equal(SkipReason.MissingDependency, s.Reason));
        Assert.Equal(["consumer", "provider"], manager.Skipped.Select(s => s.Id).Order());
    }

    /// <summary>黙ってライブラリとして読まない（<c>shared/</c> があっても、その共有の dll は載らない）。</summary>
    [Fact]
    public void AFolderWithDllsButNoEntryIsNotTakenForALibrary()
    {
        var broken = Install("shared/TestContract", "app/modules/broken/shared");
        File.Copy(Path.Combine(broken, "TestContract.dll"), Path.Combine(_root, "app", "modules", "broken", "Something.dll"));

        var manager = NewManager();
        manager.LoadAll();

        Assert.Empty(manager.Libraries);
        Assert.Empty(manager.SharedFiles);
        Assert.Equal(SkipReason.NoEntry, Assert.Single(manager.Skipped).Reason);
    }

    [Fact]
    public void AnAssemblysReferencesAreReadWithoutLoadingIt()
    {
        var consumer = Install("modules/consumer", "app/modules/consumer");

        var metadata = AssemblyMetadata.Read(Path.Combine(consumer, "ConsumerModule.dll"));

        Assert.NotNull(metadata);
        Assert.Equal("ConsumerModule", metadata.Value.Name);
        Assert.Contains(metadata.Value.References, r => r.Name == "TestContract");
        Assert.Contains(metadata.Value.References, r => r.Name == "SpoitableToolKit.Abstractions");
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(),
            a => a.GetName().Name == "ConsumerModule" && a.Location == Path.Combine(consumer, "ConsumerModule.dll"));
    }

    /// <summary>本体の方が使われる。版の検査も本体の版で。</summary>
    [Fact]
    public void ADllTheHostAlreadyHasIsNotShared()
    {
        var shared = Install("shared/TestContract", "app/modules/provider/shared");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "SpoitableToolKit.Abstractions.dll"),
            Path.Combine(shared, "SpoitableToolKit.Abstractions.dll"));
        Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");

        var manager = NewManager();
        manager.LoadAll();

        Assert.Equal(["TestContract"], manager.SharedFiles.Keys);
        Assert.Equal(2, manager.Modules.Count);
        Assert.All(manager.Modules, p => Assert.Equal(ModuleState.Running, p.State));
    }

    /// <summary>読む前の検査は全部のフォルダを先に読むので、1 つの失敗で読み込みごと止まりうる。</summary>
    [Fact]
    public void ADllThatCannotBeOpenedIsLeftOutAndNothingElseStops()
    {
        Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");
        var shared = Install("shared/TestContract", "app/modules/contracts/shared");
        var held = Path.Combine(shared, "Held.dll");
        File.Copy(Path.Combine(shared, "TestContract.dll"), held);

        var manager = NewManager();
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
            manager.LoadAll();

        Assert.Equal(2, manager.Modules.Count);
        Assert.All(manager.Modules, p => Assert.Equal(ModuleState.Running, p.State));
        Assert.Equal(Path.Combine(shared, "TestContract.dll"), manager.SharedFiles["TestContract"].Path);
    }

    [Fact]
    public void ANativeDllIsNotReadAsAnAssembly()
    {
        var path = Path.Combine(_root, "native.dll");
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(path, [0x4D, 0x5A, 0, 0, 1, 2, 3, 4]);

        Assert.Null(AssemblyMetadata.Read(path));
    }

    [Fact]
    public void StoppingUnregistersTheContracts()
    {
        Install("shared/TestContract", "app/modules/provider/shared");
        Install("modules/provider", "app/modules/provider");
        Install("modules/consumer", "app/modules/consumer");

        var manager = NewManager();
        manager.LoadAll();
        manager.Shutdown();

        Assert.All(manager.Modules, p => Assert.Equal(ModuleState.Stopped, p.State));
    }
}
