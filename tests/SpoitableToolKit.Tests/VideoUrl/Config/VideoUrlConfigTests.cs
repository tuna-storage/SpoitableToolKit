using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Config;
using SpoitableToolKit.Modules.VideoUrl;
using SpoitableToolKit.Modules.VideoUrl.Config;
using SpoitableToolKit.Tests.Config;
using SpoitableToolKit.Tests.Fakes;
using Xunit;

namespace SpoitableToolKit.Tests.VideoUrl.Config;

/// <summary>videourl の設定そのもの。画面に組み上がるところまでを見る（設定の仕組み自体は <see cref="ModuleConfigTests"/> と <see cref="NestedConfigTests"/>）。</summary>
public sealed class VideoUrlConfigTests
{
    private static object[] Describe() =>
        new ModuleConfigBinding(new VideoUrlConfig(), NullLogger.Instance).Describe(new FakeLocalizer());

    private static object Item(string key) =>
        Describe().First(i => (string)i.GetType().GetProperty("key")!.GetValue(i)! == key);

    private static string[]? Suggestions(object item) =>
        (string[]?)item.GetType().GetProperty("suggestions")?.GetValue(item);

    /// <summary>クライアントの欄の候補は選択肢ではなく、ここに無い名前も打てる。宛先は繋いだ名前（<c>Clients.Hls</c>）で届く。<c>nameof</c> は最後の名前しか返さないので、片方だけ書くと一致しないまま静かに候補が消える。</summary>
    [Fact]
    public void ThePlayerClientFieldsOfferKnownNames()
    {
        Assert.Contains("web_safari", Suggestions(Item("Clients.Hls"))!);
        Assert.Contains("android", Suggestions(Item("Clients.Progressive"))!);
    }

    /// <summary>答えた覚えのない項目に候補が付かないことまで見ないと、「常に返す」に書き換えても落ちない。</summary>
    [Fact]
    public void NothingElseGetsSuggestions()
    {
        Assert.Null(Suggestions(Item("AskOnFailure")));
        Assert.Null(Suggestions(Item("Server.Url")));
    }

    /// <summary>待たせている間の文言が道（手元か外か）で変わる。押した時点で聞くので、開いている間に設定が変わっても追いつく。</summary>
    [Fact]
    public async Task ThePanelCanAskWhichWayTheResolveWillGo()
    {
        var module = new VideoUrlModule();
        Assert.True(module.OnInit(new FakeModuleHost()));

        var config = Assert.IsType<VideoUrlConfig>(module.Config);

        Assert.False(Server(await module.HandleUiCallAsync("getMode", "{}")));

        // 開いたときの値を覚えない。同じパネルのまま設定が変わる。
        config.Server.Enabled = true;
        Assert.True(Server(await module.HandleUiCallAsync("getMode", "{}")));
    }

    private static bool Server(string json) =>
        JsonDocument.Parse(json).RootElement.GetProperty("server").GetBoolean();

    /// <summary>普通は触らない既定があるので、開いたまま置くと本当に触る項目が埋もれる。</summary>
    [Fact]
    public void ThePlayerClientsFoldAway()
    {
        Assert.Equal(true, Item("Clients").GetType().GetProperty("foldable")!.GetValue(Item("Clients")));

        // 外のサーバーの方は畳まない（入/切があり、そちらは触る前提）。
        Assert.Equal(false, Item("Server").GetType().GetProperty("foldable")!.GetValue(Item("Server")));
    }
}
