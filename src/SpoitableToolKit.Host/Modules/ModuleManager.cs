using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Clipboard;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Abstractions.Osc;
using SpoitableToolKit.Abstractions.Tools;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.Config;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Localization;
using SpoitableToolKit.Host.ExternalModules;
using SpoitableToolKit.Host.Modules.Loading;
using SpoitableToolKit.Host.Modules.Loading.Models;
using SpoitableToolKit.Host.Modules.Packaging;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Shortcuts;
using SpoitableToolKit.Host.Ui.Push;
using SpoitableToolKit.Host.Ui.Shell;

namespace SpoitableToolKit.Host.Modules;

/// <summary>
/// モジュールの読み込みとライフサイクル。
/// <para>
/// フェーズは<b>全モジュールを揃えてから</b>次へ進む（<see cref="ISpoitableModule"/> のコメントが 3 段階の関門を約束している）。
/// </para>
/// <code>
/// 全員の OnInit → 全員の OnPostInit → 全員の OnAllModulesLoaded
/// </code>
/// <para>
/// 1 つのモジュールの失敗で他を巻き添えにしない。0 個でも異常ではない（モジュール無しでも成立する常駐アプリ）。
/// </para>
/// <para>
/// ここが持つのは<b>読み込みの段取りと、読み込んだものの状態</b>。読む前の中身（<see cref="ModuleUnitReader"/>）・
/// 型を探して作る（<see cref="ModuleActivator"/>）・パネルのページ（<see cref="ModulePanelEntries"/>）は係に渡す。
/// </para>
/// </summary>
internal sealed class ModuleManager(
    IServiceProvider services,
    IVrcLogBus vrcLog,
    IVrcLogStore vrcLogStore,
    IExternalToolProvider tools,
    INotificationManager notify,
    IClipboard clipboard,
    IOscBus osc,
    HostEvents hostEvents,
    UiBroadcast broadcast,
    ModuleConfigStore configs,
    ExternalModuleRegistry externalModules,
    Localizer localizer,
    HostConfig hostConfig,
    ILoggerFactory loggerFactory,
    IReadOnlyList<string> roots,
    string moduleDataDirectory,
    Func<bool> isVRChatRunning)
{
    private readonly ILogger<ModuleManager> _logger = loggerFactory.CreateLogger<ModuleManager>();
    private readonly SharedAssemblies _shared = new(loggerFactory);
    private readonly List<LoadedModule> _modules = [];
    private readonly List<SkippedModule> _skipped = [];
    private readonly List<ModuleCandidate> _libraries = [];

    /// <summary>
    /// 読み込む前に入口の dll の中身を読む口（試験から「開けなかった」を作るため。開けないと null）。
    /// </summary>
    internal Func<string, (string Name, Version Version, IReadOnlyList<AssemblyReference> References)?> ReadEntryMetadata { get; init; } =
        AssemblyMetadata.Read;

    /// <summary>
    /// VR へ頼む口の差し込み口（<c>host.Ui.ToggleVrPanel</c> の行き先）。VR はモジュールより後に立つので、ここには口が後から挿さる。
    /// 画面の窓口（<see cref="Ui.Shell.UiBridge.Vr"/>）と同じものを渡す（Program）。
    /// <b>既定の値を持たせない</b> — 渡し忘れても試験は緑のままで、VR でだけ手首から開けなくなる（<c>SPOITABLE_NO_VR</c> の e2e では見えない）。
    /// </summary>
    internal required VrControlsSlot Vr { get; init; }
    private IReadOnlyDictionary<string, SharedFile> _sharedFiles = new Dictionary<string, SharedFile>();
    private bool _shutdown;

    public IReadOnlyList<LoadedModule> Modules => _modules;

    /// <summary>
    /// 本体と、読み込んだモジュールの辞書（読み込んだ後・言語を変えたときの読み直しに渡す）。
    /// <b>モジュールの分には持ち主（Id）が付く</b> — 読み直しでも、自分の Id で始まるキーしか入らない。
    /// </summary>
    public IReadOnlyList<LocaleSource> LocaleSources =>
        [LocaleSource.Host, .. _modules.Select(module => new LocaleSource(module.Candidate.Directory, module.Id))];

    public IReadOnlyList<SkippedModule> Skipped => _skipped;

    public IReadOnlyList<ModuleCandidate> Libraries => _libraries;

    public IReadOnlyDictionary<string, SharedFile> SharedFiles => _sharedFiles;

    public void LoadAll()
    {
        var disabled = new HashSet<string>(
            hostConfig.DisabledModules,
            StringComparer.OrdinalIgnoreCase);

        var discovery = ModuleDiscovery.Discover(roots, disabled, _logger);
        _skipped.AddRange(discovery.Skipped);

        // **読み込む前に**、動けるものと載せる共有の dll を決める（dll の参照を読むだけ。読み込みはしない）。
        // 共有が先。モジュールの ALC はここで控えたものを最優先で見る。
        var resolvers = new Dictionary<string, AssemblyDependencyResolver?>(StringComparer.OrdinalIgnoreCase);
        var byId = discovery.Candidates.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        var reader = new ModuleUnitReader(_logger, ReadEntryMetadata);
        var resolution = ModuleDependencies.Resolve(
            [.. discovery.Candidates.Select(reader.TryRead)],
            ModuleUnitReader.HostResolves,
            (id, reference) => ModuleUnitReader.OwnFolderHas(byId[id], reference, resolvers),
            ModuleUnitReader.HostVersion,
            ModuleManifest.MinimumSdk);

        foreach (var warning in resolution.Warnings) _logger.LogWarning("{Warning}", warning);
        _sharedFiles = resolution.Shared;
        _shared.Register(resolution.Shared.Values);
        _shared.InstallResolvingHook();

        foreach (var candidate in discovery.Candidates)
        {
            if (resolution.Failures.TryGetValue(candidate.Id, out var problem))
            {
                RefuseToLoad(candidate, problem);
                continue;
            }

            if (candidate.IsLibrary)
            {
                _libraries.Add(candidate);
                _logger.LogInformation("Library {Id} <- {Directory}", candidate.Id, candidate.Directory);
                continue;
            }

            TryLoad(candidate, disabled);
        }

        foreach (var module in Snapshot()) Invoke(module, nameof(ISpoitableModule.OnPostInit), module.Instance.OnPostInit);
        foreach (var module in Snapshot()) Invoke(module, nameof(ISpoitableModule.OnAllModulesLoaded), module.Instance.OnAllModulesLoaded);

        var running = _modules.Count(p => p.State == ModuleState.Running);
        _logger.LogInformation("modules: {Running} running / {Total} discovered", running, _modules.Count);
    }

    /// <summary>逆順に畳む。提供していた契約はホストが外す（モジュールは自分で外さなくてよい）。</summary>
    public void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true;

        for (var i = _modules.Count - 1; i >= 0; i--)
        {
            var module = _modules[i];
            if (module.State != ModuleState.Running) continue;

            try
            {
                module.Instance.OnShutdown();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Id}: OnShutdown threw", module.Id);
            }

            externalModules.RevokeAll(module.Instance);
            hostEvents.Retire(module.Instance);
            configs.Remove(module.Id);
            module.State = ModuleState.Stopped;
        }
    }

    /// <summary>
    /// モジュールの logger のカテゴリ（<see cref="IModuleHost.Logger"/>）。画面のログ（<c>web/index.html</c> の <c>sourceOf</c>）は、
    /// この頭のあとを Id として読む（Id は点を含みうるので、頭のあとは切らない）。
    /// <b>小文字にそろえる</b>: 画面は大文字の入ったカテゴリを型の完全名の形として読むので、フォルダ名と大文字小文字だけ違う Id
    /// （読み込みは大文字小文字を問わずに受ける）でも、出どころを取り違えない。
    /// </summary>
    internal static string LogCategoryOf(string id) => "SpoitableToolKit.Modules." + id.ToLowerInvariant();

    /// <summary>依存が足りないので読まない。<b>利用者に届くようにバナーも出す</b>（読めなかったモジュールと同じ）。</summary>
    private void RefuseToLoad(ModuleCandidate candidate, DependencyProblem problem)
    {
        _logger.LogError("Not loading {Id}: {Problem}", candidate.Id, problem);
        _skipped.Add(new SkippedModule(candidate.Id, candidate.Directory, candidate.Root, SkipReason.MissingDependency, problem));
        notify.PushBanner(localizer.Format("host.module.failed", candidate.Id), Describe(problem));
    }

    public string Describe(DependencyProblem problem)
    {
        var text = problem.Kind switch
        {
            DependencyProblemKind.TooOld => localizer.Format("host.module.dependency.tooOld",
                problem.Needed.ToString(), problem.Available?.ToString(), problem.Provider),
            DependencyProblemKind.HostTooOld => localizer.Format("host.module.dependency.hostTooOld",
                problem.Needed.ToString(), problem.Available?.ToString()),
            DependencyProblemKind.SdkTooOld => localizer.Format("host.module.dependency.sdkTooOld",
                problem.Needed.Version.ToString(3), problem.Available?.ToString(3)),
            _ => localizer.Format("host.module.dependency.missing", problem.Needed.ToString()),
        };

        return problem.From is { } from ? text + localizer.Format("host.module.dependency.via", from) : text;
    }

    /// <summary>
    /// UI からの呼び出しを宛先のモジュールへ渡す。
    /// <para>
    /// ここがホスト側の検査点。UI は「どのモジュールに」を必ず名指しし、
    /// モジュール同士が互いの口を叩ける経路は作らない。
    /// </para>
    /// </summary>
    public Task<string> CallUiAsync(string moduleId, string method, string payload, CancellationToken cancellationToken = default)
    {
        var module = _modules.FirstOrDefault(p =>
                         p.State == ModuleState.Running
                         && string.Equals(p.Id, moduleId, StringComparison.OrdinalIgnoreCase))
                     ?? throw new InvalidOperationException($"Module '{moduleId}' is not running");

        if (module.Ui.Endpoint is not { } endpoint)
            throw new InvalidOperationException(
                $"Module '{moduleId}' takes no calls from its pages: it registered no endpoint. " +
                "Call host.Ui.RegisterEndpoint(...) in OnInit with the class that implements IUiEndpoint.");

        return endpoint.HandleUiCallAsync(method, payload, cancellationToken);
    }

    /// <summary>
    /// 右クリックの項目を足す口（<c>host.Ui.RegisterContextMenu</c> で登録したもの）。<b>動いているモジュールだけ</b>を引く
    /// （落ちたモジュールの項目がメニューに残らないように）。登録が無ければ null。
    /// </summary>
    public IContextMenuSource? ContextMenuOf(string moduleId) =>
        _modules.FirstOrDefault(p => p.State == ModuleState.Running && p.Id == moduleId)?.Ui.ContextMenu;

    /// <summary>
    /// モジュールが自分の設定をコードから書き換えた（<see cref="IModuleHost.SaveConfig"/>）。保存して、画面に知らせる。
    /// <b>設定を持たないモジュールなら何もしない。</b> 知らせは設定画面の「適用」と同じ <c>configChanged</c> —
    /// 開いている設定画面は、触りかけの変更が無ければ読み直して新しい値を出す。
    /// </summary>
    private void SaveConfigOf(ISpoitableModule instance)
    {
        if (instance.Config is null) return;

        configs.Save(instance.Id);
        broadcast.Push(ConfigChangedPush.Channel, new ConfigChangedPush(Language: false));
    }

    /// <summary>
    /// <c>spoitable://&lt;Id&gt;/</c> の配信元を引く。<b>出すページが無いモジュール</b>（パネルも、手首のタイルのページも無い）には何も出さない。
    /// </summary>
    public string? ResolveWebRoot(string moduleId) =>
        _modules.FirstOrDefault(p =>
            p.State == ModuleState.Running
            && (p.PanelEntry is not null || p.ServesTilePages)
            && string.Equals(p.Id, moduleId, StringComparison.OrdinalIgnoreCase))?.WebRoot;

    private LoadedModule[] Snapshot() => [.. _modules.Where(p => p.State == ModuleState.Running)];

    private void TryLoad(ModuleCandidate candidate, IReadOnlySet<string> disabled)
    {
        // ライブラリ（入口の dll が無いもの）はここへ来ない（LoadAll が先に分ける）。
        if (candidate.EntryAssembly is not { } entry) return;

        LoadedModule? added = null;
        try
        {
            var context = new ModuleLoadContext($"module:{candidate.Id}", entry, candidate.Directory, _shared, _logger);
            var assembly = context.LoadFromFile(entry);

            // 古い SDK の確かめは読み込む前（ReadUnit → Resolve）にもするが、そのとき dll が開けなかった（ほかのプログラムが掴んでいた など）と、
            // 参照が空のまま通る。読み込んだあとの参照でもう一度見る（型を探す・作るより前に。古い SDK の型で落ちる前に、分かる文で断る）。
            if (ModuleDependencies.BuiltWithAnOldSdk(assembly.GetReferencedAssemblies(), ModuleManifest.MinimumSdk) is { } oldSdk)
            {
                RefuseToLoad(candidate, oldSdk);
                return;
            }

            if (ModuleActivator.FindModuleType(assembly, candidate.Id, _logger) is not { } type)
            {
                _skipped.Add(new SkippedModule(candidate.Id, candidate.Directory, candidate.Root, SkipReason.LoadFailed,
                    Detail: $"{Path.GetFileName(entry)} contains no ISpoitableModule implementation"));
                return;
            }

            if (ModuleActivator.Create(type, candidate.Id, _logger) is not { } instance)
            {
                _skipped.Add(new SkippedModule(candidate.Id, candidate.Directory, candidate.Root, SkipReason.LoadFailed,
                    Detail: $"{type.FullName} could not be created"));
                return;
            }

            // 本体が自分のために使っている名前（設定・翻訳キー・spoitable://host/）は、モジュールの Id にできない。
            if (ModuleManifest.IsReservedId(instance.Id))
            {
                _logger.LogWarning("{Directory}: the Id '{Id}' is reserved for the app; not loading it", candidate.Directory, instance.Id);
                _skipped.Add(new SkippedModule(candidate.Id, candidate.Directory, candidate.Root, SkipReason.LoadFailed,
                    Detail: ModuleManifest.ReservedIdProblem(instance.Id)));
                return;
            }

            // フォルダ名と違う Id を名乗るものは読み込まない。入れる・外す・OFF はフォルダ名で、設定・記録・翻訳は名乗った Id で当たるので、
            // 違うと別々の物を指す。
            // build は両方を csproj の同じ値から作るので、外れるのは手でフォルダ名を変えたか、Id を別に書いたとき。
            // 大文字小文字だけの違いは問わない（Windows のフォルダ名は区別しない）。
            if (!string.Equals(instance.Id, candidate.Id, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("The module in {Directory} reports Id '{Id}', not its folder name; not loading it",
                    candidate.Directory, instance.Id);
                _skipped.Add(new SkippedModule(candidate.Id, candidate.Directory, candidate.Root, SkipReason.LoadFailed,
                    Detail: IdMismatchProblem(candidate.Id, instance.Id)));
                return;
            }

            if (disabled.Contains(instance.Id))
            {
                _logger.LogInformation("{Id} is disabled by the 'disabled modules' setting", instance.Id);
                _skipped.Add(new SkippedModule(candidate.Id, candidate.Directory, candidate.Root, SkipReason.Disabled));
                return;
            }

            if (_modules.Any(p => string.Equals(p.Id, instance.Id, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("Duplicate Id '{Id}'; skipping {Directory}", instance.Id, candidate.Directory);
                _skipped.Add(new SkippedModule(candidate.Id, candidate.Directory, candidate.Root, SkipReason.Shadowed));
                return;
            }

            var dataDirectory = Directory.CreateDirectory(
                Path.Combine(moduleDataDirectory, SanitizeId(instance.Id))).FullName;

            var webRoot = Path.Combine(candidate.Directory, "web");

            // 画面まわり（host.Ui）と手首のショートカット（host.Shortcuts）。どちらも OnInit の中で登録され（受ける間は 1 つを分け合う）、
            // 何を出すかは OnInit の後に決める。
            var window = new RegistrationWindow(instance.Id);
            var ui = new ModuleUi(instance.Id, new PanelChannel(instance.Id, broadcast, Vr, _logger), window);
            var shortcuts = new ModuleShortcuts(instance.Id, window);

            // 設定は OnInit より前に読み込む。IModuleConfig のコメントで
            // 「OnInit の時点で保存済みの値が入っている」と約束しているため。設定の口（host.Config）も OnInit の前に繋ぐ。
            // 変更を契機に動きたいなら、モジュールが OnInit で host.Config.AddListener<IConfigEventListener<TConfig>>(this, ...) を呼ぶ。
            // ここで勝手に登録しない（実装しているかどうかで登録の有無が変わると、listener を別クラスに分けた途端に
            // 登録の仕方が変わってしまう。登録は 1 通りだけにする）。
            var config = instance.Config is { } own
                ? configs.Add(instance.Id, instance.DisplayName, instance.Icon, own, dataDirectory)
                : ConfigChannel.None(instance.Id);

            var module = new LoadedModule
            {
                Candidate = candidate,
                Instance = instance,
                Host = new ModuleHost(services, dataDirectory, vrcLog, vrcLogStore, tools, notify, clipboard,
                    osc, config, hostEvents.For(instance), ui, shortcuts, externalModules, localizer, loggerFactory.CreateLogger(LogCategoryOf(instance.Id)), isVRChatRunning, () => SaveConfigOf(instance)),
                Ui = ui,
                Shortcuts = shortcuts,
                WebRoot = webRoot,
            };
            _modules.Add(module);
            added = module;

            // **辞書を先に足す。** OnInit で引いた文言がキーのまま残らないように
            // （全部の辞書を読み直すのは、全部を読み込んだ後の Program でもう一度やる）。
            localizer.Merge(new LocaleSource(candidate.Directory, instance.Id));

            // OnInit の中で口を作り、listener を足すので、その前に owner として受けると知らせる（畳むときは Fail か Shutdown が外す）。
            hostEvents.Admit(instance);

            // 画面まわりの登録は OnInit の間だけ受ける（あとから来たものは、出すかを決めたあとなので断る）。
            bool initialized;
            ui.OpenRegistration();
            try
            {
                initialized = instance.OnInit(module.Host);
            }
            catch (Exception ex)
            {
                Fail(module, $"OnInit threw: {ex.GetType().Name}", ex);
                return;
            }
            finally
            {
                ui.CloseRegistration();
            }

            if (!initialized)
            {
                Fail(module, "OnInit returned false", exception: null);
                return;
            }

            module.PanelEntry = ModulePanelEntries.Panel(instance.Id, ui.Panel, webRoot, _logger);
            module.VrPanelEntry = ModulePanelEntries.VrPanel(instance.Id, ui.VrPanel, webRoot, module.PanelEntry, _logger);
            ui.PanelDecided(module.PanelEntry is not null);

            module.State = ModuleState.Running;
            module.Phase = nameof(ISpoitableModule.OnInit);
            _logger.LogInformation("Loaded {Id} ({Name}) <- {Directory}",
                instance.Id, instance.DisplayName, candidate.Directory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load {Id} ({Entry})", candidate.Id, candidate.EntryAssembly);

            // 並べたあと（設定の読み込みなど、OnInit の前）で落ちたものは外す。残すと Initializing のまま並び、管理画面で「動いています」に見える。
            // （ここへ来るのは OnInit の前に落ちたもの。owner として受けると知らせるのは OnInit の直前なので、口から外すものは無い。）
            if (added is not null) _modules.Remove(added);
            _skipped.Add(new SkippedModule(candidate.Id, candidate.Directory, candidate.Root, SkipReason.LoadFailed,
                Detail: $"{ex.GetType().Name}: {ex.Message}"));

            // **ログだけでは利用者に届かない。** WinExe なのでコンソールが無く、
            // 読み込めなかったモジュールは画面にも出ない = 「なぜか動かない」だけが残る。
            notify.PushBanner(
                localizer.Format("host.module.failed", candidate.Id), $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static string IdMismatchProblem(string folder, string id) =>
        $"The module in the folder '{folder}' reports the Id '{id}'. The folder name and ISpoitableModule.Id must be the same " +
        "(the app finds a module's settings, records and translations by its Id, and turns it off or removes it by its folder). " +
        "ISpoitableModule.Id returns the Id the SDK's build wrote into the dll from <SpoitableModuleId> in the csproj, which also names the folder: " +
        "do not write Id in the module class, and do not rename the folder by hand.";

    private void Invoke(LoadedModule module, string phase, Action call)
    {
        try
        {
            call();
            module.Phase = phase;
            _logger.LogDebug("{Id}: {Phase}", module.Id, phase);
        }
        catch (Exception ex)
        {
            Fail(module, $"{phase} threw: {ex.GetType().Name}", ex);
        }
    }

    /// <summary>
    /// 失敗したモジュールを止める。登録済みの契約を外し、OnInit で掴んだものを離せるよう
    /// OnShutdown も一度だけ呼ぶ（そこでまた投げても記録に留める）。
    /// </summary>
    private void Fail(LoadedModule module, string reason, Exception? exception)
    {
        module.State = ModuleState.Failed;

        // 管理画面の行には、**例外の文まで**出す（モジュール間の API の取り違えなどは、文に直し方が書いてある。型の名前だけでは直せない）。
        // バナーは短いまま（型まで。続きは管理画面とアプリのログ）。
        module.Failure = exception is null ? reason : $"{reason}: {exception.Message}";

        if (exception is null) _logger.LogError("{Id}: {Reason}", module.Id, reason);
        else _logger.LogError(exception, "{Id}: {Reason}", module.Id, reason);

        // 読み込みには成功したが、途中で落ちた。**画面には出ないのに設定には並ぶ**ので、
        // 出しておかないと「設定はあるのに動かない」になる。
        notify.PushBanner(localizer.Format("host.module.failed", module.Id), reason);

        externalModules.RevokeAll(module.Instance);

        // OnInit を通ったあとに後続フェーズで落ちることがある。外さないと、
        // 死んだモジュールの listener がイベントを受け取り続ける（作った口も閉じる）。
        hostEvents.Retire(module.Instance);

        try
        {
            module.Instance.OnShutdown();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Id}: OnShutdown threw as well after the failure", module.Id);
        }
    }

    /// <summary>Id はディレクトリ名になるので、パスを壊す文字を落とす。</summary>
    private static string SanitizeId(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat(id.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c));
        return safe.Length == 0 ? "unnamed" : safe;
    }
}
