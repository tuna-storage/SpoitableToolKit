using System.Reflection;
using SpoitableToolKit.Abstractions.Config.Attributes;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Tests.Fakes;
using Xunit;

namespace SpoitableToolKit.Tests.Settings;

/// <summary>ホスト本体の設定の既定値。e2e からは見えない: アプリを共有する試験が <c>setHiddenKinds</c> で上書きしていくので、e2e の「既定で畳まれている」は他の試験が書いた値を読むだけになる。</summary>
public sealed class HostConfigTests
{
    /// <summary><c>ApiRequest</c> と <c>raw</c> は既定で畳む。どちらも件数が桁違いで他を押し流す（<c>ApiRequest</c> は 1 セッションで 1000 件超、生行は 15,000 行超。出したままだと join/leave や動画の失敗が 1 画面も保たない）。</summary>
    [Fact]
    public void NoisyKindsAreCollapsedByDefault()
    {
        Assert.Equal(["ApiRequest", "raw"], new HostConfig().HiddenKinds);
    }

    /// <summary>API の呼び出しは既定では残さない（記録の出来事の 95% が ApiRequest で、容量を圧迫する）。e2e では見られない — アプリを共有しているので、ほかの試験が先に切り替えて保存し直すと「既定」ではなくなる。</summary>
    [Fact]
    public void ApiRequestsAreNotStoredByDefault()
    {
        Assert.False(new HostConfig().StoreApiRequests);
    }

    /// <summary>VR オーバーレイ（手首の帯と VR ウィンドウ）も SteamVR のダッシュボードのパネルも、既定で使う（入れる前と同じ動き）。</summary>
    [Fact]
    public void TheVrOverlayAndTheDashboardAreOnByDefault()
    {
        var config = new HostConfig();

        Assert.True(config.VrOverlay);
        Assert.True(config.VrDashboard);
    }

    [Fact]
    public void AppUpdatesLookAtStableByThemselvesByDefault()
    {
        var config = new HostConfig();

        Assert.Equal(UpdateChannel.Stable, config.UpdateChannel);
        Assert.True(config.CheckAppUpdates);
        Assert.Equal("", config.SkippedAppVersion);
        Assert.False(config.UpdateChannelChosen);
    }

    /// <summary>閉じるボタンは初めて押したときに聞く。起動はふつうに窓を開き、Windows の起動時には起動しない（選んだ人だけ）。</summary>
    [Fact]
    public void TheStartupAndExitSettingsStartFromAskingAndOff()
    {
        var config = new HostConfig();

        Assert.Equal(CloseButtonAction.Ask, config.CloseButton);
        Assert.False(config.StartInTray);
        Assert.False(config.StartWithWindows);
    }

    private static string[] Rows(HostConfig config, System.Text.Json.Nodes.JsonObject? pending = null)
    {
        var binding = new SpoitableToolKit.Host.Config.ModuleConfigBinding(config, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var items = pending is null ? binding.Describe(new FakeLocalizer()) : binding.DescribeWith(pending, new FakeLocalizer());
        return [.. items.Select(item => item.GetType().GetProperty("name")!.GetValue(item)!.ToString()!)];
    }

    /// <summary>
    /// Windows の起動時の起動は、登録できる形（インストール版）でだけ行を出す。開発の bin では出さない（押しても登録しないので、押せる行を見せない）。
    /// 下見（ほかの値を打っている途中）でも同じ — 下見は写しで描くが、出すかは本物に聞く（写しには実行時の印が付かない）。
    /// </summary>
    [Fact]
    public void TheStartWithWindowsRowIsShownOnlyWhereItCanBeRegistered()
    {
        var pending = new System.Text.Json.Nodes.JsonObject { [nameof(HostConfig.StartInTray)] = true };

        var unavailable = new HostConfig();
        Assert.DoesNotContain(nameof(HostConfig.StartWithWindows), Rows(unavailable));
        Assert.DoesNotContain(nameof(HostConfig.StartWithWindows), Rows(unavailable, pending));
        Assert.Contains(nameof(HostConfig.StartInTray), Rows(unavailable));

        var available = new HostConfig { StartWithWindowsAvailable = () => true };
        Assert.Contains(nameof(HostConfig.StartWithWindows), Rows(available));
        Assert.Contains(nameof(HostConfig.StartWithWindows), Rows(available, pending));
    }

    /// <summary>画面が自分で持つ状態は設定画面に並べない。属性を落としても普通に動き、項目が 1 つ増えるだけ（翻訳が無いのでキーがそのまま出る）なので、ここで止める。</summary>
    [Theory]
    [InlineData(nameof(HostConfig.HiddenKinds))]
    [InlineData(nameof(HostConfig.ToolVerifiedAt))]
    [InlineData(nameof(HostConfig.WristOffsets))]
    [InlineData(nameof(HostConfig.WristWidth))]
    [InlineData(nameof(HostConfig.SkippedAppVersion))]
    [InlineData(nameof(HostConfig.UpdateChannelChosen))]
    public void ScreenStateDoesNotAppearInTheSettings(string member)
    {
        var field = typeof(HostConfig).GetField(member);

        Assert.NotNull(field);
        Assert.NotNull(field.GetCustomAttribute<SpoitableSettingsHiddenAttribute>());
    }
}
