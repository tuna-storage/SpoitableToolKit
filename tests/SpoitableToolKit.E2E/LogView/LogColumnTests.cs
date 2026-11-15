using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.LogView;

/// <summary>
/// 列の幅は字の大きさ（em）で決めているので、見出しの入れ物だけ字を小さくすると列ごと縮み、見出しが中身より左へずれる。
/// </summary>
public sealed class LogColumnTests(AppFixture app) : IClassFixture<AppFixture>
{
    private IPage Page => app.Page;

    [Theory]
    [InlineData("vrc")]
    [InlineData("app")]
    public async Task TheHeadingsLineUpWithTheColumnsBelow(string source)
    {
        await Page.Locator("#sidebar .item[data-id='log']").ClickAsync();
        await Page.Locator($"#logTabs .spoitable-tab[data-source='{source}']").ClickAsync();

        // VRChat の面は、流した行が出るまで空のまま。アプリの面は起動の行がもう並んでいる。
        if (source == "vrc") app.AppendLog("[Behaviour] OnPlayerJoined e2e-columns");

        var lane = source == "vrc"
            ? Page.Locator("#rowsVrc .row[data-kind='PlayerJoined']", new() { HasText = "e2e-columns" }).Locator(".line")
            : Page.Locator("#rowsApp .row .line >> visible=true").First;
        await Assertions.Expect(lane).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // 比べるのは中身が始まる所（枠の左 + 縁 + 内側の余白）。
        const string contentLeft = """
            node => {
              const style = getComputedStyle(node);
              return node.getBoundingClientRect().left + parseFloat(style.borderLeftWidth) + parseFloat(style.paddingLeft);
            }
            """;
        var heads = await Page.Locator("#logHead > span").EvaluateAllAsync<double[]>(
            $"spans => spans.map({contentLeft})");
        var cells = await lane.EvaluateAsync<double[]>(
            $"line => [...line.children].map({contentLeft})");

        Assert.Equal(3, heads.Length);
        Assert.Equal(3, cells.Length);
        for (var column = 0; column < 3; column++)
        {
            Assert.True(Math.Abs(heads[column] - cells[column]) < 0.5,
                $"{source} の {column + 1} 列目: 見出し {heads[column]:0.##} px / 行 {cells[column]:0.##} px");
        }
    }
}
