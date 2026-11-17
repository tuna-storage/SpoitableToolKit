using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions.Events.Config;
using SpoitableToolKit.Host.Config;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Localization;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Push;
using SpoitableToolKit.Host.Ui.Tray;
using SpoitableToolKit.Host.Ui.Vr;
using SpoitableToolKit.Tests.Fakes;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Tray;

/// <summary>トレイの右クリックのメニュー。中身は WinForms を通さずに組み立てるので、ここで見られる。押したときに何を書くかも、ここで決まる。</summary>
public sealed class TrayMenuTests
{
    private static string[] Shape(IReadOnlyList<TrayMenuItem> items) =>
    [
        .. items.Select(item => item.Separator ? "---"
            : item.Children.Count == 0 ? $"{item.Id}:{item.Label}"
            : $"{item.Id}:{item.Label}[{string.Join(", ", item.Children.Select(child => $"{child.Id}:{child.Label}"))}]"),
    ];

    private static TrayMenuItem Child(IReadOnlyList<TrayMenuItem> items, string id) =>
        items.Single(item => item.Id == TrayMenu.Vr).Children.Single(child => child.Id == id);

    [Fact]
    public void TheMenuOpensTheWindowHasTheVrFeaturesAndExits()
    {
        var items = TrayMenu.Build(new FakeLocalizer(), VrFeatures.All);

        Assert.Equal(
            [
                "open:host.tray.open",
                "vr:host.tray.vr[vr.overlay:host.tray.vrOverlay, vr.dashboard:host.tray.vrDashboard]",
                "---",
                "exit:host.tray.exit",
            ],
            Shape(items));
    }

    /// <summary>左クリックと同じ「開く」を太字にする（Windows のトレイのメニューの決まり）。どの項目も押せる。</summary>
    [Fact]
    public void OpenIsTheDefaultItem()
    {
        var items = TrayMenu.Build(new FakeLocalizer(), VrFeatures.All);

        Assert.Equal([TrayMenu.Open], items.Where(item => item.Default).Select(item => item.Id));
        Assert.All(items.Concat(items.SelectMany(item => item.Children)), item => Assert.True(item.Enabled));
    }

    /// <summary>チェックはメニューを開いたときの設定を写す（設定の画面で変えても、次に開いたメニューはその値）。</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void TheVrChecksShowTheSettings(bool overlay, bool dashboard)
    {
        var items = TrayMenu.Build(new FakeLocalizer(), VrFeatures.Of(new HostConfig { VrOverlay = overlay, VrDashboard = dashboard }));

        Assert.Equal(overlay, Child(items, TrayMenu.VrOverlay).Checked);
        Assert.Equal(dashboard, Child(items, TrayMenu.VrDashboard).Checked);
        Assert.False(items.Single(item => item.Id == TrayMenu.Vr).Checked);
    }

    /// <summary>チェックを押すと入と切を入れ替える。決めるのは押したときの設定（メニューを開いた時点の写しではない）。ほかの項目は設定に触らない。</summary>
    [Fact]
    public void PressingAVrCheckFlipsItsSettingAndNothingElseDoes()
    {
        var config = new HostConfig { VrOverlay = true, VrDashboard = false };

        Assert.Equal(new TraySettingChange(nameof(HostConfig.VrOverlay), false), TrayMenu.SettingFor(TrayMenu.VrOverlay, config));
        Assert.Equal(new TraySettingChange(nameof(HostConfig.VrDashboard), true), TrayMenu.SettingFor(TrayMenu.VrDashboard, config));
        Assert.Null(TrayMenu.SettingFor(TrayMenu.Open, config));
        Assert.Null(TrayMenu.SettingFor(TrayMenu.Vr, config));
        Assert.Null(TrayMenu.SettingFor(TrayMenu.Exit, config));
    }

    /// <summary>
    /// チェックの答えは、設定の画面と同じ適用の道で書く（DesktopApp が <see cref="SettingsApplier.SetHost"/> に渡す）:
    /// config.json に書かれ、listener に知らされ、全部の面の設定の画面へ configChanged が流れる。次に開いたメニューのチェックも変わる。
    /// </summary>
    [Fact]
    public void AVrCheckIsWrittenTheSameWayAsTheSettingsScreen()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "stk-tray", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var config = new HostConfig();
            var store = new ModuleConfigStore(new EventHubs(_ => true, NullLoggerFactory.Instance), NullLoggerFactory.Instance);
            var told = new List<string>();
            store.Add(HostConfig.Id, "Host", null, config, directory)
                .AddListener<IConfigEventListener<HostConfig>>(HostOwner.Instance, new Ear(told));
            var broadcast = new Recording();
            var text = new Localizer(NullLogger<Localizer>.Instance);
            text.Load("ja", [LocaleSource.Host]);
            var applier = new SettingsApplier(store, config, text, () => [LocaleSource.Host], broadcast, NullLogger.Instance);

            var change = TrayMenu.SettingFor(TrayMenu.VrDashboard, config)!;
            Assert.True(applier.SetHost(change.Key, JsonValue.Create(change.Value)));

            Assert.False(config.VrDashboard);
            Assert.False(JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "config.json")))![nameof(HostConfig.VrDashboard)]!.GetValue<bool>());
            Assert.Equal([nameof(HostConfig.VrDashboard)], told);
            Assert.Single(broadcast.On(ConfigChangedPush.Channel));
            Assert.False(Child(TrayMenu.Build(text, VrFeatures.Of(config)), TrayMenu.VrDashboard).Checked);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    private sealed class Ear(List<string> told) : IConfigEventListener<HostConfig>
    {
        public void OnConfigChanged(ConfigChangedEventParams<HostConfig> e) => told.AddRange(e.Changes.Select(c => c.Name));
    }

    /// <summary>
    /// 開くたびに組み立て直すので、言語を変えると次に開いたメニューから変わる（起動し直さなくてよい）。
    /// 同じ辞書を読み直したあとに組むと、新しい言語の字になっている。
    /// </summary>
    [Fact]
    public void BuildingAgainAfterTheLanguageChangesUsesTheNewLanguage()
    {
        var text = new Localizer(NullLogger<Localizer>.Instance);

        text.Load("ja", [LocaleSource.Host]);
        var japanese = Shape(TrayMenu.Build(text, VrFeatures.All));
        text.Load("en", [LocaleSource.Host]);
        var english = Shape(TrayMenu.Build(text, VrFeatures.All));

        Assert.Equal(["open:開く", "vr:VR 機能[vr.overlay:VR オーバーレイ, vr.dashboard:SteamVR ダッシュボード]", "---", "exit:終了"], japanese);
        Assert.Equal(["open:Open", "vr:VR features[vr.overlay:VR overlay, vr.dashboard:SteamVR dashboard]", "---", "exit:Exit"], english);
    }
}
