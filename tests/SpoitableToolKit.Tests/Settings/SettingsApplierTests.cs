using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.Events.Config;
using SpoitableToolKit.Host.Config;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Localization;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Push;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Settings;

/// <summary>
/// 画面の外で選んだ値（閉じるときの問いの答え・トレイのメニュー）も、設定の画面の「適用」と同じ道を通る:
/// config.json に書かれ、listener が聞かれて知らされ、全部の面の設定の画面へ configChanged が流れる。
/// </summary>
public sealed class SettingsApplierTests : IDisposable
{
    private readonly string _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "stk-applier", Guid.NewGuid().ToString("N"))).FullName;
    private readonly HostConfig _config = new();
    private readonly Recording _broadcast = new();
    private readonly Localizer _text = new(NullLogger<Localizer>.Instance);
    private readonly ModuleConfigStore _store = new(new EventHubs(_ => true, NullLoggerFactory.Instance), NullLoggerFactory.Instance);
    private readonly IEventSource _events;

    public SettingsApplierTests()
    {
        _text.Load("ja", [LocaleSource.Host]);
        _events = _store.Add(HostConfig.Id, "Host", null, _config, _directory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private SettingsApplier Applier() => new(_store, _config, _text, () => [LocaleSource.Host], _broadcast, NullLogger.Instance);

    private sealed class Listener(Func<ConfigChangingEventParams<HostConfig>, EventResult>? answer = null) : IConfigEventListener<HostConfig>
    {
        public List<string> Told { get; } = [];

        public EventResult OnConfigChanging(ConfigChangingEventParams<HostConfig> e) => answer?.Invoke(e) ?? EventResult.Continue;

        public void OnConfigChanged(ConfigChangedEventParams<HostConfig> e) => Told.AddRange(e.Changes.Select(c => c.Name));
    }

    [Fact]
    public void AHostValueIsWrittenSavedToldAndShown()
    {
        var listener = new Listener();
        _events.AddListener<IConfigEventListener<HostConfig>>(HostOwner.Instance, listener);

        Assert.True(Applier().SetHost(nameof(HostConfig.CloseButton), JsonValue.Create("Tray")));

        Assert.Equal(CloseButtonAction.Tray, _config.CloseButton);
        Assert.Equal("Tray", JsonNode.Parse(File.ReadAllText(Path.Combine(_directory, "config.json")))![nameof(HostConfig.CloseButton)]!.GetValue<string>());
        Assert.Equal([nameof(HostConfig.CloseButton)], listener.Told);

        var pushed = Assert.Single(_broadcast.On(ConfigChangedPush.Channel));
        Assert.Equal(new ConfigChangedPush(Language: false), pushed.Payload);
    }

    /// <summary>断られたら前の値のまま。書かず、知らせず、画面にも流さない。</summary>
    [Fact]
    public void ARefusedValueStaysAsItWas()
    {
        var listener = new Listener(e =>
        {
            e.Reject(c => c.CloseButton, "no");
            return EventResult.Continue;
        });
        _events.AddListener<IConfigEventListener<HostConfig>>(HostOwner.Instance, listener);

        Assert.False(Applier().SetHost(nameof(HostConfig.CloseButton), JsonValue.Create("Exit")));

        Assert.Equal(CloseButtonAction.Ask, _config.CloseButton);
        Assert.Empty(listener.Told);
        Assert.Empty(_broadcast.On(ConfigChangedPush.Channel));
    }

    [Fact]
    public void AnUnknownValueIsRefused()
    {
        Assert.False(Applier().SetHost(nameof(HostConfig.CloseButton), JsonValue.Create("Somewhere")));
        Assert.False(Applier().SetHost("NoSuchSetting", JsonValue.Create(true)));

        Assert.Equal(CloseButtonAction.Ask, _config.CloseButton);
    }

    /// <summary>言語が変わったら、画面へ流す前に辞書を読み直す（画面はこのあと新しい言語で組み直す）。</summary>
    [Fact]
    public void ChangingTheLanguageReloadsTheStringsBeforeTellingTheScreens()
    {
        _config.Language = UiLanguage.Ja;

        Assert.True(Applier().SetHost(nameof(HostConfig.Language), JsonValue.Create("En")));

        Assert.Equal("en", _text.Language);
        var pushed = Assert.Single(_broadcast.On(ConfigChangedPush.Channel));
        Assert.Equal(new ConfigChangedPush(Language: true), pushed.Payload);
    }
}
