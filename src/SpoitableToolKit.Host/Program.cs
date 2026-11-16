using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Events.Config;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Host.App;
using SpoitableToolKit.Host.AppLog;
using SpoitableToolKit.Host.Clipboard;
using SpoitableToolKit.Host.Config;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Events.AppLog;
using SpoitableToolKit.Host.Localization;
using SpoitableToolKit.Host.ExternalModules;
using SpoitableToolKit.Host.Notifications;
using SpoitableToolKit.Host.Osc;
using SpoitableToolKit.Host.Modules;
using SpoitableToolKit.Host.Modules.Build;
using SpoitableToolKit.Host.Modules.GitHub;
using SpoitableToolKit.Host.Modules.Management;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Shortcuts;
using SpoitableToolKit.Host.Shortcuts.Gestures;
using SpoitableToolKit.Host.Status;
using SpoitableToolKit.Host.Theming;
using SpoitableToolKit.Host.Tools;
using SpoitableToolKit.Host.Ui.Browser;
using SpoitableToolKit.Host.Ui.ContextMenu;
using SpoitableToolKit.Host.Ui.Desktop;
using SpoitableToolKit.Host.Ui.Push;
using SpoitableToolKit.Host.Ui.Shell;
using SpoitableToolKit.Host.Ui.Vr;
using SpoitableToolKit.Host.Updates;
using SpoitableToolKit.Host.VRChat;
using SpoitableToolKit.Host.Video;
using SpoitableToolKit.Host.VrcLog;
using SpoitableToolKit.Host.VrcLog.Store;
using Velopack;

namespace SpoitableToolKit.Host;

internal static class Program
{
    /// <summary>アプリログを残す本数（今の 1 本を含む）。</summary>
    private const int AppLogKeep = 5;

    [STAThread]
    private static int Main(string[] args)
    {
        // **単一起動の Mutex より前** — 動いているアプリが居ても、Update.exe の呼び出しには答える必要がある。
        // Velopack が入れた形でなければ呼ばない（VelopackUpdateSource.IsVelopackLayout に理由）。
        if (VelopackUpdateSource.IsVelopackLayout(AppContext.BaseDirectory)) VelopackApp.Build().Run();

        // 「再起動」で立ち上がったときは、前のアプリが抜けるまで待つ（CEF の置き場・SQLite・心拍計・ポートを取り合わない）。
        AppRestart.WaitForPrevious(args);

        // WinExe なので既定では親コンソールのコードページになり、日本語のログが化ける。
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (IOException) { /* コンソール無し */ }

        // 置き場のファイル（記録・設定・ログ）を開く前に取り、記録を書き切ったあとに離す（using で一番先に宣言しているので、畳むのは一番あと）。
        using var single = SingleInstance.TryAcquire(AppPaths.DataDirectory, AppRestart.IsRestart(args) ? AppRestart.WaitLimit : TimeSpan.Zero);
        if (single is null)
        {
            SingleInstance.AskTheRunningOneToShow(AppPaths.DataDirectory);
            Console.Error.WriteLine($"SpoitableToolKit is already running with {AppPaths.DataDirectory}; asked it to come to the front.");
            return SingleInstance.AlreadyRunningExitCode;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        // --replay <log> [--import] : 既存の output_log を頭からパースする。UI は開かない。
        // --import を付けるとイベントをストアへ取り込む（old_logs の救出用）。
        if (Array.IndexOf(args, "--replay") is var replayIndex and >= 0 && replayIndex + 1 < args.Length)
            return Replay(args[replayIndex + 1], args.Contains("--import"));

        // --resolve <url> : UI を開かずに videourl の解決だけ走らせる。
        // モジュールの読み込み → envelope → IExternalToolProvider → yt-dlp までを一度に通せる。
        var resolveUrl = Array.IndexOf(args, "--resolve") is var resolveIndex and >= 0 && resolveIndex + 1 < args.Length
            ? args[resolveIndex + 1]
            : null;

        // WinExe なので配布した状態ではコンソールが無い。loggerFactory より先に作る（作った瞬間から拾うため）。
        // 記録（applog.db）も同じ時に開く。番号は記録の続きから振るので、最初の 1 行より前に要る。
        // 開けなくても止まらない（この起動の分だけの台帳になる）。
        var appLogRun = DateTimeOffset.Now;
        using var appLog = new AppLogSink(
            new AppLogFile(AppPaths.AppLogDirectory, AppLogKeep, appLogRun),
            AppLogStore.TryOpen(Path.Combine(AppPaths.DataDirectory, "applog.db")),
            appLogRun.ToUnixTimeMilliseconds());

        using var loggerFactory = LoggerFactory.Create(builder => builder
            .AddSimpleConsole(options => options.SingleLine = true)
            .AddProvider(new AppLogProvider(appLog))
            .SetMinimumLevel(MinimumLogLevel(LogLevel.Information)));

        // 警告にはしない: e2e はアプリを止めて起動し直すたびにこうなる（落ちた理由は、落ちた側の記録にある）。
        if (single.PreviousEndedAbruptly)
            loggerFactory.CreateLogger("SpoitableToolKit.Host").LogInformation(
                "The previous run of the app ended without letting go of its single-instance lock (it crashed or was stopped)");

        var store = new SqliteVrcLogStore(Path.Combine(AppPaths.DataDirectory, "vrclog.db"), loggerFactory);
        // ログを読み始める前に作る（読む側が行を配る先）。
        var hostEvents = new HostEvents(loggerFactory);
        var logBus = new VrcLogBus(loggerFactory, hostEvents.VrcLog);
        var broadcast = new UiBroadcast();

        // リトライ回数のような、複数イベントにまたがる状態は VideoEventProvider が持つ。
        using var videoEvents = new VideoEventProvider(logBus, hostEvents.Videos);

        var hostConfig = new HostConfig();

        // 配色。**同梱テーマを置き直すのは設定を読むより先**（候補を引く時点で並んでいてほしい）。
        var themes = new HostThemes(AppPaths.DataDirectory, loggerFactory.CreateLogger<HostThemes>());
        themes.Ensure();
        hostConfig.ThemeNames = themes.Names;

        // 画面の文言。**モジュールの辞書は読み込んだ後で合成し直す**（置き場所が分かるのがその後）。
        var text = new Localizer(loggerFactory.CreateLogger<Localizer>());
        text.Load(hostConfig.Language.ToString(), [LocaleSource.Host]);
        var notify = new NotificationManager(broadcast, hostConfig, loggerFactory);

        // ホストが最初の住人で、モジュールはその後ろに読み込み順で並ぶ。
        var status = new StatusBoard(hostConfig, broadcast);
        hostConfig.StatusItems = status.Names;

        // VRChat が動いているか。ここで一度確かめる（タイトルバーもモジュールも、読み込みの時点で正しい値を読む）。
        // 見張りを始めるのはモジュールを全部読み終えてから（下の presence.Begin。理由は VRChatPresence）。
        var presence = new VRChatPresence(VRChatProbe(loggerFactory), hostEvents.VRChat, loggerFactory.CreateLogger<VRChatPresence>());
        presence.CheckOnce();

        var reading = new LogReadingStatus(() => logBus.CurrentLogFile, text, () => presence.IsRunning);
        status.Register(HostConfig.Id, reading.Status);

        // 本体の分も状態の口に流すので、ダッシュボードと手首の帯は同じ形で読める。
        using var instance = new InstanceTracker(logBus, store, text, loggerFactory.CreateLogger<InstanceTracker>());
        status.Register(InstanceTracker.WorldId, instance.World);
        status.Register(InstanceTracker.PeopleId, instance.People);
        status.Register(InstanceTracker.VideoId, instance.Video);

        // モジュールへ流れるのは、本体のあと。
        VRChatPresence.Connect(hostEvents.VRChat.Source, instance, reading, notify, text);

        // 手首の帯の時刻。画面へ送るのは**分が変わったときだけ**（状態の口は周ごとに比べる）なので、10 秒ごとに書いてよい。
        var clock = new ModuleStatus { State = StatusState.Ok };
        status.Register(HostConfig.ClockId, clock);
        using var clockTimer = new System.Threading.Timer(_ => clock.Text = DateTime.Now.ToString("HH:mm"), null, 0, 10_000);

        // タイトルバーには「読んでいるか」だけ。**読むファイルが変わったときに書く**（LogReadingStatus に理由）。
        // 言語を変えたら、本体が文字にして持っている状態を書き直す（StatusRouting に理由）。
        StatusRouting.Connect(logBus.FileChanges, text.Reloaded, reading, instance);

        // 手首のショートカットの台帳。**中身は状態の口と同じ周期で配る**ので pump より先に作り、埋めるのはモジュールを読んだ後。
        var shortcutLogger = loggerFactory.CreateLogger<ShortcutBoard>();
        var shortcuts = new ShortcutBoard(broadcast, notify, text, shortcutLogger);

        // 件数を数えるのは UiPump（バッチが通る場所）。件数はログ画面の足元に出す（logStats）。
        var pump = new UiPump(broadcast, status, (eventCount, lineCount) =>
            broadcast.Push("logStats", new { file = reading.FileName, events = eventCount, lines = lineCount }), shortcuts);

        // アプリログを画面へ。**pump より先に出た分は届かない**ので、
        // 画面は getStatus（＝台帳）で追いついてから push を受ける。
        appLog.Written.AddListener<IAppLogEventListener>(HostOwner.Instance, pump);

        // e2e はクリップボードをアプリの中だけで持つ（手元のクリップボードと履歴を荒らさない。HostClipboard.KeepInMemory に理由）。
        if (Environment.GetEnvironmentVariable("SPOITABLE_CLIPBOARD") is "memory")
        {
            HostClipboard.KeepInMemory();
            loggerFactory.CreateLogger("SpoitableToolKit.Host").LogInformation(
                "SPOITABLE_CLIPBOARD=memory; the clipboard stays inside the app and Windows' clipboard is left alone");
        }

        var clipboard = new HostClipboardService();

        // VRChat への OSC。ポートは送るたびに hostConfig から読むので、設定を変えたらその場で効く。
        using var osc = new OscBus(hostConfig, loggerFactory);

        // モジュールから見える Core サービスの解決先（IModuleHost.Services）。
        using var services = new ServiceCollection()
            .AddSingleton(loggerFactory)
            .AddSingleton(typeof(ILogger<>), typeof(Logger<>))
            .BuildServiceProvider();

        var configs = new ModuleConfigStore(hostEvents.Hubs, loggerFactory);

        // 設定画面の動きの行（SpoitableSettingsAction）が変わったら、その 1 行だけを全部の面へ流す。モジュールが結ぶ（OnInit）より先に挿す。
        configs.ActionSink = new ConfigActionPusher(broadcast, configs, text);

        var hostConfigEvents = configs.Add(HostConfig.Id, "Host", "⚙", hostConfig, AppPaths.DataDirectory,
            displayNameKey: "host.settings.title");

        // 前の起動までのアプリログで台帳を埋める。残す期間は設定を読んでからでないと分からないので、受け口を作った時ではなくここで。
        // 読み戻してから消す。消すのはキューの先で、読み戻しと競う。先に消すと負けたときだけ古い行が台帳に紛れる。
        // 読み戻しが先なら、古い行を外すのはいつも期間で切る側になる。
        var appLogRetention = new AppLogRetention(hostConfig, appLog);
        appLog.Restore(appLogRetention.Since);
        appLogRetention.Apply();
        hostConfigEvents.AddListener<IConfigEventListener<HostConfig>>(HostOwner.Instance, appLogRetention);

        var moduleRegistry = new ExternalModuleRegistry(loggerFactory);
        var tools = new ExternalToolProvider(hostConfig, configs, notify, text, loggerFactory);
        // 管理画面で決めたモジュールの入れ替えは、読み込む前に片付ける（まだ何もファイルを掴んでいない）。
        // 入れる方が先（前の版を .trash へ移す）。.trash を空にするのは、外す方の最後。
        var installLogger = loggerFactory.CreateLogger(typeof(ModuleInstaller).FullName!);
        PendingInstalls.Apply(AppPaths.DataDirectory, AppPaths.BundledRoot, installLogger);
        PendingRemovals.Apply(AppPaths.DataDirectory, AppPaths.BundledRoot, loggerFactory.CreateLogger(typeof(PendingRemovals).FullName!));

        // モジュールの作業領域。前の名前（plugin-data）のままなら 1 回だけ移す。
        // モジュールを読む前、Mutex を取ったあと（前のアプリが畳み終わってから）。
        var moduleData = ModuleDataLocation.Resolve(AppPaths.DataDirectory, loggerFactory.CreateLogger(typeof(ModuleDataLocation).FullName!));

        // VR へ頼む口を挿す所。**挿すのは VR の周を立てたとき 1 回**で、モジュール（host.Ui.ToggleVrPanel）と画面の窓口が同じものを読む。
        var vrControls = new VrControlsSlot();

        // 手首のドラッグだけのタイルの、押した・動いた・離したの受け口（押すだけのタイルは click で台帳の PressAsync へ行く）。
        var gestures = new ShortcutGestures(shortcuts, () => vrControls.Current?.InLayout == true, shortcutLogger);

        var modules = new ModuleManager(services, logBus, store, tools, notify, clipboard, osc, hostEvents, broadcast, configs, moduleRegistry,
            text, hostConfig, loggerFactory, AppPaths.ModuleRoots, moduleData, () => presence.IsRunning)
        {
            Vr = vrControls,
        };

        using var moduleHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
        var gitHubApi = GitHubReleases.ApiFromEnvironment();
        var github = new GitHubReleases(moduleHttp, gitHubApi);

        // リリースの無い repo は、利用者に聞いてから、この PC で build して入れる。SDK のパッケージはアプリに同梱したもの（sdk/）だけを使う。
        var buildSetup = new ModuleBuildSetup(
            new DotnetSdks(moduleHttp, DotnetSdks.MetadataFrom(Environment.GetEnvironmentVariable("SPOITABLE_DOTNET_RELEASES")),
                AppPaths.ToolsDirectory, loggerFactory.CreateLogger(typeof(DotnetSdks).FullName!),
                DotnetSdks.CandidatesOverride(Environment.GetEnvironmentVariable("SPOITABLE_DOTNET"))),
            Path.Combine(AppPaths.BundledRoot, "sdk"),
            ModuleBuildSetup.PackageFeedFor(gitHubApi));
        // 前の回に .NET SDK を落としている最中に閉じた残り（落としかけ 290 MB・展開しかけ）を、裏で消す（起動は待たせない）。
        _ = buildSetup.Sdks.ClearLeftoversAsync();
        var moduleInstaller = new ModuleInstaller(AppPaths.DataDirectory, AppPaths.BundledRoot, github, installLogger);
        var catalog = new ModuleCatalog(modules, hostConfig, configs, broadcast, text,
            moduleInstaller,
            new ModuleUpdates(AppPaths.DataDirectory, github, TimeProvider.System, loggerFactory.CreateLogger<ModuleUpdates>()),
            AppPaths.DataDirectory, AppPaths.BundledRoot, loggerFactory.CreateLogger<ModuleCatalog>());

        // 閉じるときに、走っている build と .NET SDK のダウンロードを止める。
        using var moduleJobs = new ModuleInstallJobs(moduleInstaller, catalog, buildSetup, hostConfig, broadcast,
            loggerFactory.CreateLogger(typeof(ModuleInstallJobs).FullName!));
        hostConfigEvents.AddListener<IConfigEventListener<HostConfig>>(HostOwner.Instance, moduleJobs);
        using var closing = new CancellationTokenSource();

        var updateLogger = loggerFactory.CreateLogger<AppUpdates>();
        var updates = new AppUpdates(VelopackUpdateSource.Create(AppPaths.DataDirectory, updateLogger), hostConfig, () => configs.Save(HostConfig.Id),
            broadcast, new AppUpdateSchedule(TimeProvider.System, AppUpdateSchedule.FirstDelayFrom(Environment.GetEnvironmentVariable(AppUpdateSchedule.FirstDelayVariable))),
            AppVersion.Text, closing.Token, updateLogger);
        updates.FollowInstalledChannel();
        hostConfigEvents.AddListener<IConfigEventListener<HostConfig>>(HostOwner.Instance, updates);

        // 手首のショートカットに置けるもの。**台帳の名前そのもの**（本体・パネルを持つモジュール・モジュールが名乗ったもの）。
        hostConfig.ShortcutTargets = shortcuts.Keys;

        // 記録と画面の繋ぎ方は LogRouting に置いてある（規則そのものを試験から通せるように）。
        LogRouting.Connect(logBus, store, pump, () => hostConfig.StoreAllLines, () => hostConfig.StoreApiRequests);

        try
        {
            // UI より先に読み込む。OnInit の時点では CEF は未初期化なので、
            // モジュールは UI 無しで成立していること。
            // **読み込みの間は VRChat を見張らない**（読み終えてから見張り始める。途中の起動・終了は最初の見張りで 1 回だけ流れる）。
            presence.Begin(modules.LoadAll);

            // 状態を預かる。**読み込みの後**でないと、落ちたモジュールの分まで並ぶ。
            foreach (var module in modules.Modules)
            {
                if (module.State != ModuleState.Running) continue;
                if (module.Ui.Status is { } moduleStatus) status.Register(module.Id, moduleStatus);
            }

            // 手首のショートカット。**本体が先、モジュールは読み込み順**（設定のモーダルの右の列もこの順）。
            shortcuts.AddHost();
            foreach (var module in modules.Modules)
            {
                if (module.State != ModuleState.Running) continue;

                // 並べるのは OnInit で host.Shortcuts.Register したもの（値は登録のときに読んで検めてある）。
                // タイルのページは web/ にあるものだけ受ける（登録の字だけ信じると、タイルの中が 404 になる）。
                var webRoot = module.WebRoot;
                module.ServesTilePages = shortcuts.AddModule(module.Id, module.Instance.DisplayName, module.Instance.Icon, module.Instance.IconPath,
                    module.PanelEntry is not null, module.Ui.Status, module.Shortcuts.Registered, page => File.Exists(Path.Combine(webRoot, page)));
            }

            // モジュールの辞書を合わせて読み直す。置き場所（modules/<id>/locales）が
            // 分かるのは読み込んだ後なので、ここでもう一度。
            text.Load(hostConfig.Language.ToString(), modules.LocaleSources);

            if (resolveUrl is not null) return Resolve(modules, resolveUrl);

            // シェルにだけ配る token。モジュールの iframe は stkHost を bind できても呼べない。
            // **CEF の初期化より前に作る** — 配るのはスキームハンドラなので、登録時に要る。
            var uiToken = new UiToken();
            var uiLogger = loggerFactory.CreateLogger("SpoitableToolKit.Host.Ui");

            // テーマ名は**引かれるたびに読む**。設定を変えたら、次に配るときから新しい方になる。
            CefRuntime.EnsureInitialized(
                modules.ResolveWebRoot, uiToken, themes, () => hostConfig.Theme, uiLogger);

            var menus = new ContextMenus(modules.ContextMenuOf, clipboard, uiLogger);

            var bridge = new UiBridge(
                logBus, store, hostConfig, modules, notify, configs, broadcast, text, status, appLog, uiToken, menus, shortcuts,
                catalog, moduleJobs, uiLogger)
            {
                VrSlot = vrControls,
                Gestures = gestures,
                Updates = updates,
            };

            using var window = new DesktopWindow(bridge, broadcast, single.ShowRequests, uiLogger);

            // 窓の操作を画面へ開ける。**窓口の方が先に出来る**ので後から挿す。
            bridge.Window = window;

            // 管理画面の「再起動」。新しいアプリを立ててから、この窓を閉じる（閉じると Application.Run が返って、いつもどおり畳む）。
            bridge.Restart = new AppRestart(args, window.CloseWindow, loggerFactory.CreateLogger<AppRestart>());

            // 入れたモジュールに新しい版があるか。起動の邪魔をしないよう、少し経ってから（設定で切れる。1 日に 1 回まで）。
            _ = catalog.CheckUpdatesAfterAsync(TimeSpan.FromSeconds(10), notify, closing.Token);

            // 落とし終えたらこの窓を閉じる（閉じると Update.exe が入れて起動し直す）。
            updates.Close = window.CloseWindow;
            _ = updates.RunAsync(closing.Token);

            // SteamVR を検出するまでは CEF を 1 枚も立てない。
            using var vr = VrLoop.Start(broadcast, bridge, hostConfig, configs, uiLogger);

            Application.Run(window);
        }
        finally
        {
            // 引かれたのに訳が無かったキーを、まとめて 1 回だけ出す。
            // 画面を一通り触ったあとに見ると効く。
            text.ReportMissing();

            // 逆順に畳む。モジュール → pump → bus を止めてから、store のキューを書き切る。
            // 答えを待っている通知は先に畳む（待ち続けたままモジュールを止めない）。
            closing.Cancel();
            notify.CancelAll();
            gestures.Stop();
            shortcuts.Cancel();

            // VRChat の見張りはモジュールより先に止める（畳んだモジュールへ起動・終了を流さない）。
            presence.Dispose();
            modules.Shutdown();
            pump.DisposeAsync().AsTask().GetAwaiter().GetResult();
            logBus.DisposeAsync().AsTask().GetAwaiter().GetResult();
            store.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        return 0;
    }

    /// <summary>
    /// videourl に URL を 1 本渡して結果を出す。UI を通さずに、モジュールの口
    /// （<c>IUiEndpoint</c>）と外部ツールの取得までを確かめるための経路。
    /// </summary>
    private static int Resolve(ModuleManager modules, string url)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new { url });

        string report;
        var failed = false;
        try
        {
            report = modules.CallUiAsync("videourl", "resolve", payload).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            report = $"failed: {ex.Message}";
            failed = true;
        }

        var reportPath = Path.Combine(AppPaths.DataDirectory, "resolve.txt");
        File.WriteAllText(reportPath, report);
        Console.WriteLine(report);
        Console.WriteLine($"(also written to {reportPath})");
        return failed ? 1 : 0;
    }

    /// <summary>
    /// VRChat が動いているかの見方。<b>既定はプロセスを探す</b>（null を返す）。
    /// <para>
    /// <b>e2e は本物の VRChat を見ない</b>: <c>SPOITABLE_VRCHAT_FLAG=&lt;ファイル&gt;</c> なら、そのファイルがあるかで決める
    /// （見ると、試験を走らせている間に手元で VRChat を起動・終了しただけで、タイトルバー・札・通知が変わる）。
    /// </para>
    /// </summary>
    private static Func<bool>? VRChatProbe(ILoggerFactory loggerFactory)
    {
        if (Environment.GetEnvironmentVariable("SPOITABLE_VRCHAT_FLAG") is not { Length: > 0 } flag) return null;

        loggerFactory.CreateLogger("SpoitableToolKit.Host").LogInformation(
            "SPOITABLE_VRCHAT_FLAG is set; VRChat counts as running while {Flag} exists", flag);
        return () => File.Exists(flag);
    }

    /// <summary>
    /// SPOITABLE_LOG_LEVEL で出力レベルを上げ下げできるようにする（Debug / Trace など）。
    /// モジュールの読み込みは「なぜそれが選ばれたか」を追いたくなる場所なので、既定より下も出せること。
    /// </summary>
    private static LogLevel MinimumLogLevel(LogLevel fallback) =>
        Enum.TryParse<LogLevel>(Environment.GetEnvironmentVariable("SPOITABLE_LOG_LEVEL"), ignoreCase: true, out var level)
            ? level
            : fallback;

    /// <summary>
    /// output_log を 1 本パースしてレポートを書き出す。
    /// WinExe なので標準出力が親コンソールに出ない場合があり、ファイルにも残す。
    /// </summary>
    private static int Replay(string path, bool import)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"not found: {path}");
            return 1;
        }

        using var loggerFactory = LoggerFactory.Create(builder => builder
            .AddSimpleConsole(options => options.SingleLine = true)
            .SetMinimumLevel(MinimumLogLevel(LogLevel.Warning)));

        SqliteVrcLogStore? store = null;
        if (import)
            store = new SqliteVrcLogStore(Path.Combine(AppPaths.DataDirectory, "vrclog.db"), loggerFactory);

        var summary = VrcLogReplay.Run(path, store is null ? null : store.Append);
        var report = VrcLogReplay.Describe(summary);

        if (store is not null)
        {
            store.FlushAsync().GetAwaiter().GetResult();
            var readBack = store.QueryRecentAsync(5).GetAwaiter().GetResult();
            report += Environment.NewLine + "stored, read back newest 5:" + Environment.NewLine;
            foreach (var row in readBack)
                report += $"  #{row.Id} {row.Timestamp:yyyy/MM/dd HH:mm:ss} {row.Kind,-20} {row.Payload}{Environment.NewLine}";

            store.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        var reportPath = Path.Combine(AppPaths.DataDirectory, "replay.txt");
        File.WriteAllText(reportPath, report);
        Console.WriteLine(report);
        Console.WriteLine($"(also written to {reportPath})");
        return 0;
    }
}
