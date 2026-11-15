using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Clipboard;
using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.Events.Config;
using SpoitableToolKit.Abstractions.Events.VRChat;
using SpoitableToolKit.Abstractions.Events.Video;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Abstractions.Modules;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Abstractions.Osc;
using SpoitableToolKit.Abstractions.Shortcuts;
using SpoitableToolKit.Abstractions.Tools;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.Config;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Modules;
using SpoitableToolKit.Host.Shortcuts;
using SpoitableToolKit.Tests.Helpers;

namespace SpoitableToolKit.Tests.Fakes;

internal sealed class FakeModuleHost : IModuleHost
{
    public int ConfigSaves { get; private set; }

    public void SaveConfig() => ConfigSaves++;

    /// <summary>既定は空。ログを見たい試験は <see cref="LoggingTo"/> で <see cref="Logs"/> を渡す。</summary>
    public IServiceProvider Services { get; init; } = new Empty();

    public static FakeModuleHost LoggingTo(Logs logs) => new() { Services = new Serving(logs) };

    private sealed class Serving(Logs logs) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(ILoggerFactory) ? logs : null;
    }

    public string DataDirectory { get; } =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "stk-host", Guid.NewGuid().ToString("N"))).FullName;

    public ILocalizer Text { get; } = new FakeLocalizer();

    /// <summary>カテゴリは本物と同じ。<see cref="LoggingTo"/> で渡した <see cref="Logs"/> に書く（渡さなければ捨てる）。</summary>
    public ILogger Logger => (Services.GetService(typeof(ILoggerFactory)) as ILoggerFactory)?.CreateLogger("SpoitableToolKit.Modules.test") ?? NullLogger.Instance;

    public IVrcLogBus VrcLog { get; } = new FakeVrcLogBus();

    public IVrcLogStore VrcLogStore { get; } = new FakeVrcLogStore();

    public IExternalToolProvider ExternalTools { get; } = new FakeExternalToolProvider();

    public INotificationManager Notifications { get; } = new FakeNotifications();

    public IClipboard Clipboard { get; } = new FakeClipboard();

    public FakeOscBus Osc { get; } = new();

    IOscBus IModuleHost.Osc => Osc;

    public FakePanelChannel Panel { get; } = new();

    /// <summary>本物の <see cref="ModuleUi"/>。登録の窓は開けたままで、「OnInit の後は断る」は <c>ModuleLoaderTests</c> で見る。</summary>
    public ModuleUi Ui => _ui ??= OpenUi();

    private ModuleUi? _ui;

    IModuleUi IModuleHost.Ui => Ui;

    public ModuleShortcuts Shortcuts => _shortcuts ??= new ModuleShortcuts("test", Window);

    private ModuleShortcuts? _shortcuts;

    IModuleShortcuts IModuleHost.Shortcuts => Shortcuts;

    /// <summary>登録を受ける間。開けたまま。パネルがあることにしてある（<c>ToggleVrPanel</c> が窓へ頼む）。</summary>
    private RegistrationWindow Window => _window ??= OpenWindow();

    private RegistrationWindow? _window;

    private static RegistrationWindow OpenWindow()
    {
        var window = new RegistrationWindow("test");
        window.Open();
        return window;
    }

    private ModuleUi OpenUi()
    {
        var ui = new ModuleUi("test", Panel, Window);
        ui.PanelDecided(true);
        return ui;
    }

    /// <summary>
    /// 既定は本物の置き場に繋がない口で、足せるのは <c>IConfigEventListener&lt;T&gt;</c> だけ（T がモジュールの設定の型かは見ない。それは本物の側の試験で見る）。
    /// 設定の変更を本物の順で流す試験は <see cref="WithConfig"/> で繋ぐ。
    /// </summary>
    public IEventSource Config => _config ??= new AnyConfig(Hubs);

    private IEventSource? _config;

    public ModuleConfigStore? Configs { get; private set; }

    /// <summary>
    /// 本物の設定の置き場に繋ぐ。OnInit の前に呼ぶ。
    /// </summary>
    public FakeModuleHost WithConfig(string moduleId, IModuleConfig config)
    {
        Configs = new ModuleConfigStore(Hubs, NullLoggerFactory.Instance);
        _config = Configs.Add(moduleId, moduleId, null, config, DataDirectory);
        return this;
    }

    /// <summary>owner はどれでも通す（試験はモジュールを読み込まないので名簿に載らない）。</summary>
    public EventHubs Hubs { get; } = new(_ => true, NullLoggerFactory.Instance);

    public EventHub VRChatHub => _vrchat ??= Hubs.CreateHub(HostOwner.Instance, [typeof(IVRChatEventListener)]);

    public EventHub VideoHub => _videos ??= Hubs.CreateHub(HostOwner.Instance, [typeof(IVideoEventListener)]);

    private EventHub? _vrchat;
    private EventHub? _videos;

    public IEventHubs Events => Hubs;

    public IEventSource VRChat => VRChatHub.Source;

    public IEventSource Videos => VideoHub.Source;

    public bool IsVRChatRunning { get; set; }

    public IExternalModuleRegistry ExternalModules { get; } = new NoModules();

    private sealed class Empty : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>
    /// 本物の置き場に繋がない設定の口。受けるのは <c>IConfigEventListener&lt;T&gt;</c> だけ（本物と同じ。何でも受けると、本物のホストに断られる形を unit で見逃す）。
    /// 最初に足された型で本物の口を作るので、そのあと違う型を足すと断る。
    /// </summary>
    private sealed class AnyConfig(EventHubs hubs) : IEventSource
    {
        private EventHub? _hub;

        public void AddListener<TListener>(ISpoitableModule owner, IEventListener listener) where TListener : class, IEventListener
        {
            if (!typeof(TListener).IsGenericType || typeof(TListener).GetGenericTypeDefinition() != typeof(IConfigEventListener<>))
                throw new ArgumentException($"host.Config takes IConfigEventListener<TConfig>, not {typeof(TListener).Name} (the real host refuses it too)");

            _hub ??= hubs.CreateHub(HostOwner.Instance, [typeof(TListener)]);
            _hub.Source.AddListener<TListener>(owner, listener);
        }

        public void RemoveListener<TListener>(ISpoitableModule owner, IEventListener listener) where TListener : class, IEventListener =>
            _hub?.Source.RemoveListener<TListener>(owner, listener);
    }

    private sealed class NoModules : IExternalModuleRegistry
    {
        public void Register<T>(ISpoitableModule owner, string name, T implementation) where T : class { }

        public IExternalModuleRef<T> GetRequired<T>(string name) where T : class => throw new InvalidOperationException();

        public IExternalModuleRef<T>? GetOptional<T>(string name) where T : class => null;

        public IReadOnlyDictionary<string, IExternalModuleRef<T>> GetAll<T>() where T : class => new Dictionary<string, IExternalModuleRef<T>>();
    }
}
