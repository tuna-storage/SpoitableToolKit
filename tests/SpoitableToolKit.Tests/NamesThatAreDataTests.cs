using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.Modules;
using Xunit;

namespace SpoitableToolKit.Tests;

/// <summary>型や namespace の名前が、そのまま外へ出るデータになっている所。動かすと build は通ったまま黙って変わる。</summary>
public sealed class NamesThatAreDataTests
{
    /// <summary>
    /// ログの意味の分かった行は、型の名前（<see cref="VrcLogEventParams.Kind"/>）で記録（vrclog.db）に入り、画面と設定（隠す種類）もその字で見分ける。
    /// 型の名前を変えると、前の記録が読めなくなり、隠した種類がまた出てくる。
    /// </summary>
    [Fact]
    public void TheKindsOfLogEventsKeepTheirStoredNames()
    {
        var kinds = typeof(VrcLogEventParams).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(VrcLogEventParams)) && !type.IsAbstract)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["ApiRequest", "PlayerJoined", "PlayerLeft", "VideoPlaybackFailed", "VideoPlaybackStarted", "VideoResolved", "WorldJoined"], kinds);
    }

    /// <summary>
    /// モジュールのログのカテゴリは <c>SpoitableToolKit.Modules.&lt;Id&gt;</c>（本体が <see cref="Abstractions.IModuleHost.Logger"/> を作る）。
    /// 画面のログはこの頭のあとで、どのモジュールの行かを見分ける（<c>web/index.html</c> の <c>sourceOf</c>）— 外れると本体の行に見える。
    /// </summary>
    [Fact]
    public void TheModulesLogUnderTheirIds()
    {
        Assert.Equal("SpoitableToolKit.Modules.hrs", ModuleManager.LogCategoryOf("hrs"));
        Assert.Equal("SpoitableToolKit.Modules.my.module", ModuleManager.LogCategoryOf("my.module"));
        Assert.Equal("SpoitableToolKit.Modules.my.module", ModuleManager.LogCategoryOf("My.Module"));
    }
}
