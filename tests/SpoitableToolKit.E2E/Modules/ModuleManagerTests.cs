using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Modules;

/// <summary>
/// このアプリには同梱の hrs・videourl と、ユーザー側の置き場に<b>わざと読み込めない</b> <c>modules/broken/</c>（<see cref="AppFixture"/>）がある。
/// 「再起動」は本当には起動し直さず、渡す引数を <see cref="AppFixture.RestartRecord"/> に書かせる。
/// </summary>
public sealed class ModuleManagerTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private readonly ModuleScreen _screen = new(app);

    private IPage Page => app.Page;

    private ILocator Row(string id) => _screen.Row(id);

    private ILocator RestartBar => _screen.RestartBar;

    public async Task InitializeAsync() => await _screen.OpenAsync();

    /// <summary>変えたものを戻す（このクラスのアプリは 1 つなので、後の試験に持ち越さない）。</summary>
    public async Task DisposeAsync()
    {
        await Page.EvaluateAsync(
            "async () => { dismissMenu?.(); await bridge.setModuleEnabled('videourl', true); await bridge.setModuleRemoval('broken', false); }");
        File.Delete(app.RestartRecord);
        await Assertions.Expect(RestartBar).ToBeHiddenAsync();
    }

    [Fact]
    public async Task TheBundledModulesAndTheOneThatCouldNotLoadAreListed()
    {
        await Assertions.Expect(Row("hrs").Locator(".pl-title .tag")).ToHaveTextAsync("バンドル版");
        await Assertions.Expect(Row("hrs").Locator(".pl-state")).ToHaveTextAsync("動作中");
        await Assertions.Expect(Row("videourl").Locator(".pl-state")).ToHaveTextAsync("動作中");
        await Assertions.Expect(Row("videourl").Locator(".pl-title .ver")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^\d+\.\d+\.\d+"));

        await Assertions.Expect(Row("broken").Locator(".pl-title .tag")).ToHaveCountAsync(0);
        await Assertions.Expect(Row("broken").Locator(".pl-state")).ToHaveTextAsync("読み込み失敗");
        await Assertions.Expect(Row("broken").Locator(".pl-reason")).ToContainTextAsync("BadImageFormatException");

        await Assertions.Expect(Row("broken").Locator(".pl-actions > *")).ToHaveCountAsync(1);
        await Assertions.Expect(Row("broken").Locator(".pl-actions .pl-more")).ToHaveAttributeAsync("aria-label", "その他の操作");

        await Assertions.Expect(RestartBar).ToBeHiddenAsync();
    }

    /// <summary>一覧を空にしてから開き直して、並ぶのを見る（ほかの試験が配った中身で描かれていると、取り直さなくても並んで見える）。</summary>
    [Fact]
    public async Task OpeningThePageFetchesTheList()
    {
        await Page.EvaluateAsync("() => { el.plList.replaceChildren(); selectView('log'); }");
        await Assertions.Expect(Row("hrs")).ToHaveCountAsync(0);

        await _screen.OpenAsync();

        await Assertions.Expect(Row("hrs")).ToHaveCountAsync(1);
        await Assertions.Expect(Row("broken")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task TurningAModuleOffIsSavedAndAsksForARestartUntilTurnedBackOn()
    {
        await _screen.PickAsync(Row("videourl"), "disable");

        await Assertions.Expect(RestartBar).ToBeVisibleAsync();
        await Assertions.Expect(RestartBar).ToContainTextAsync("再起動するまで適用されません");
        await Assertions.Expect(Row("videourl").Locator(".pl-next")).ToHaveTextAsync("次の起動で OFF");
        await Assertions.Expect(Row("videourl").Locator(".pl-state")).ToHaveTextAsync("動作中");
        await ExpectDisabledModules(["videourl"]);

        await _screen.OpenMenuAsync(Row("videourl"));
        await Assertions.Expect(_screen.Menu.Locator("button[data-id='enable']")).ToHaveTextAsync("有効化");
        await Assertions.Expect(_screen.Menu.Locator("button[data-id='disable']")).ToHaveCountAsync(0);
        await _screen.Menu.Locator("button[data-id='enable']").ClickAsync();

        await Assertions.Expect(RestartBar).ToBeHiddenAsync();
        await Assertions.Expect(Row("videourl").Locator(".pl-next")).ToHaveCountAsync(0);
        await ExpectDisabledModules([]);
    }

    /// <summary>VR の窓とデスクトップの窓の 2 つの面があるので、変えた側だけ描き直すのでは足りない。画面を通さずに変えて、配られた中身で描き変わるのを見る。</summary>
    [Fact]
    public async Task AChangeMadeElsewhereReachesThePage()
    {
        await Page.EvaluateAsync("() => { bridge.setModuleEnabled('videourl', false); }");

        await Assertions.Expect(Row("videourl").Locator(".pl-next")).ToHaveTextAsync("次の起動で OFF");
        await Assertions.Expect(RestartBar).ToBeVisibleAsync();
    }

    [Fact]
    public async Task OnlyAnAddedModuleCanBeRemovedAndTheRemovalCanBeTakenBack()
    {
        foreach (var bundled in new[] { "hrs", "videourl" })
        {
            await _screen.OpenMenuAsync(Row(bundled));
            var items = await _screen.MenuItemsAsync();
            Assert.Equal(["disable", "remove"], items);
            await Assertions.Expect(_screen.Menu.Locator("button[data-id='remove']")).ToBeDisabledAsync();
            await Assertions.Expect(_screen.Menu.Locator("button[data-id='disable']")).ToBeEnabledAsync();
            await _screen.DismissMenuAsync();
        }

        var marker = Path.Combine(app.DataDirectory, "modules", "broken", ".remove");
        await _screen.OpenMenuAsync(Row("broken"));
        await Assertions.Expect(_screen.Menu.Locator("button[data-id='remove']")).ToHaveTextAsync("削除");
        await _screen.Menu.Locator("button[data-id='remove']").ClickAsync();

        await Assertions.Expect(Row("broken").Locator(".pl-next")).ToContainTextAsync("次の起動で削除されます");
        await Assertions.Expect(RestartBar).ToBeVisibleAsync();
        Assert.True(File.Exists(marker), "削除の印が置かれていない");

        await _screen.OpenMenuAsync(Row("broken"));
        await Assertions.Expect(_screen.Menu.Locator("button[data-id='keep']")).ToHaveTextAsync("削除をキャンセル");
        await _screen.Menu.Locator("button[data-id='keep']").ClickAsync();

        await Assertions.Expect(RestartBar).ToBeHiddenAsync();
        await Assertions.Expect(Row("broken").Locator(".pl-next")).ToHaveCountAsync(0);
        Assert.False(File.Exists(marker), "削除をキャンセルしたのに印が残っている");
        await _screen.OpenMenuAsync(Row("broken"));
        await Assertions.Expect(_screen.Menu.Locator("button[data-id='remove']")).ToBeEnabledAsync();
        await _screen.DismissMenuAsync();
    }

    [Fact]
    public async Task TheRowMenuOpensUnderItsButton()
    {
        var button = Row("videourl").Locator(".pl-more");
        await _screen.OpenMenuAsync(Row("videourl"));

        var under = await button.BoundingBoxAsync() ?? throw new InvalidOperationException("ボタンの箱が無い");
        var menu = await _screen.Menu.BoundingBoxAsync() ?? throw new InvalidOperationException("メニューの箱が無い");
        Assert.True(Math.Abs(menu.X + menu.Width - (under.X + under.Width)) < 2, $"右端が {under.X + under.Width} ではなく {menu.X + menu.Width}");
        Assert.True(menu.Y >= under.Y + under.Height, $"メニュー（{menu.Y}）がボタンの下（{under.Y + under.Height}）に無い");

        await button.ClickAsync();
        await Assertions.Expect(_screen.Menu).ToBeHiddenAsync();

        await _screen.OpenMenuAsync(Row("videourl"));
        await _screen.OpenMenuAsync(Row("hrs"));
        var hrs = await Row("hrs").Locator(".pl-more").BoundingBoxAsync() ?? throw new InvalidOperationException("ボタンの箱が無い");
        var moved = await _screen.Menu.BoundingBoxAsync() ?? throw new InvalidOperationException("メニューの箱が無い");
        Assert.True(Math.Abs(moved.Y - (hrs.Y + hrs.Height)) < 8, $"hrs の「…」の下（{hrs.Y + hrs.Height}）ではなく {moved.Y} に出た");

        await Page.Locator("#viewModuleManager h1").ClickAsync();
        await Assertions.Expect(_screen.Menu).ToBeHiddenAsync();
        await Assertions.Expect(RestartBar).ToBeHiddenAsync();
    }

    [Fact]
    public async Task TheRestartButtonStartsTheAppAgainWaitingForThisOne()
    {
        await _screen.PickAsync(Row("videourl"), "disable");
        await Assertions.Expect(RestartBar).ToBeVisibleAsync();

        await Page.Locator("#plRestartBtn").ClickAsync();

        // アプリが書いている最中に開くと、掴まれていて読めない（IOException）。書き終わるまで待ち直す。
        string[]? written = null;
        for (var attempt = 0; attempt < 50 && written is null; attempt++)
        {
            try
            {
                if (File.Exists(app.RestartRecord)) written = await File.ReadAllLinesAsync(app.RestartRecord);
            }
            catch (IOException)
            {
                written = null;
            }

            if (written is null) await Task.Delay(100);
        }

        Assert.NotNull(written);
        Assert.Equal(["--after-pid", app.ProcessId.ToString()], written[^2..]);
    }

    /// <summary>その文は押したその時の知らせで、次に中身が変わったら、いつもの「変更は再起動するまで適用されません」に戻る。</summary>
    [Fact]
    public async Task AFailedRestartSaysSoOnlyUntilTheNextChange()
    {
        await _screen.PickAsync(Row("videourl"), "disable");
        await Assertions.Expect(RestartBar).ToBeVisibleAsync();

        await Page.EvaluateAsync("() => { window.__e2eRestartApp = bridge.restartApp; bridge.restartApp = async () => false; }");
        try
        {
            await Page.Locator("#plRestartBtn").ClickAsync();
            await Assertions.Expect(RestartBar).ToContainTextAsync("再起動に失敗したため");
            await Assertions.Expect(Page.Locator("#plRestartBtn")).ToBeEnabledAsync();

            await _screen.PickAsync(Row("broken"), "remove");

            await Assertions.Expect(Row("broken").Locator(".pl-next")).ToContainTextAsync("次の起動で削除されます");
            await Assertions.Expect(RestartBar).ToContainTextAsync("変更は再起動するまで適用されません");
        }
        finally
        {
            await Page.EvaluateAsync("() => { bridge.restartApp = window.__e2eRestartApp; }");
        }
    }

    private async Task ExpectDisabledModules(string[] expected)
    {
        var file = Path.Combine(app.DataDirectory, "config.json");

        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (Read(file) is { } saved && saved.SequenceEqual(expected)) return;
            await Task.Delay(100);
        }

        Assert.Fail($"DisabledModules が期待どおりにならなかった: {(Read(file) is { } last ? string.Join(", ", last) : "(読めない)")}");

        static string[]? Read(string file)
        {
            try
            {
                return JsonNode.Parse(File.ReadAllText(file))?["DisabledModules"]?.Deserialize<string[]>() ?? [];
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return null;
            }
        }
    }
}
