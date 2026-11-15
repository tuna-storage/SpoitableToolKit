using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Clipboard;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Abstractions.Modules;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Abstractions.Osc;
using SpoitableToolKit.Abstractions.Shortcuts;
using SpoitableToolKit.Abstractions.Tools;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.VRChat;

namespace SpoitableToolKit.Host.Modules;

/// <summary>
/// モジュール 1 つに渡す <see cref="IModuleHost"/>。
/// データディレクトリと設定はモジュールごとに分ける（他のモジュールの設定を壊せないように）。
/// </summary>
internal sealed class ModuleHost(
    IServiceProvider services,
    string dataDirectory,
    IVrcLogBus vrcLog,
    IVrcLogStore vrcLogStore,
    IExternalToolProvider tools,
    INotificationManager notify,
    IClipboard clipboard,
    IOscBus osc,
    IEventSource config,
    ModuleEvents moduleEvents,
    IModuleUi ui,
    IModuleShortcuts shortcuts,
    IExternalModuleRegistry externalModules,
    ILocalizer localizer,
    ILogger logger,
    Func<bool> isVRChatRunning,
    Action? saveConfig = null) : IModuleHost
{
    /// <summary>ホストの見張り（<see cref="VRChatPresence"/>）が持つ値を、読むたびに引く。</summary>
    public bool IsVRChatRunning => isVRChatRunning();

    /// <summary>保存と設定画面への知らせはホストの持ち物なので、組み立てる側（ModuleManager）から挿す。</summary>
    public void SaveConfig() => saveConfig?.Invoke();

    public IServiceProvider Services { get; } = services;

    public ILocalizer Text { get; } = localizer;

    public ILogger Logger { get; } = logger;

    public string DataDirectory { get; } = dataDirectory;

    public IVrcLogBus VrcLog { get; } = moduleEvents.LogOf(vrcLog);

    public IVrcLogStore VrcLogStore { get; } = vrcLogStore;

    public IExternalToolProvider ExternalTools { get; } = tools;

    public INotificationManager Notifications { get; } = notify;

    public IClipboard Clipboard { get; } = clipboard;

    public IOscBus Osc { get; } = osc;

    public IEventSource Config { get; } = moduleEvents.ConfigOf(config);

    public IEventHubs Events { get; } = moduleEvents.Hubs;

    public IEventSource VRChat { get; } = moduleEvents.VRChat;

    public IEventSource Videos { get; } = moduleEvents.Videos;

    public IModuleUi Ui { get; } = ui;

    public IModuleShortcuts Shortcuts { get; } = shortcuts;

    public IExternalModuleRegistry ExternalModules { get; } = externalModules;
}
