using System.Reflection;
using SpoitableToolKit.Abstractions.Config.Attributes;
using SpoitableToolKit.Host.Settings;
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

    [Fact]
    public void AppUpdatesLookAtStableByThemselvesByDefault()
    {
        var config = new HostConfig();

        Assert.Equal(UpdateChannel.Stable, config.UpdateChannel);
        Assert.True(config.CheckAppUpdates);
        Assert.Equal("", config.SkippedAppVersion);
        Assert.False(config.UpdateChannelChosen);
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
