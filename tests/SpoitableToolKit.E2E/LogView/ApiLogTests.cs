using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.LogView;

public sealed class ApiLogTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private IPage Page => app.Page;

    private ILocator ApiChip => Page.Locator("#filters .fitem[data-kind='ApiRequest']");

    private static readonly Regex Locked = new(@"\blocked\b");

    public async Task InitializeAsync()
    {
        if (await Page.Locator("#modal").IsVisibleAsync()) await Page.Locator("#modalNo").ClickAsync();
        await ShowVrcLaneAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task ShowVrcLaneAsync()
    {
        if (await Page.Locator("#filterModal").IsVisibleAsync()) await Page.Locator("#filterClose").ClickAsync();
        await Page.EvaluateAsync("() => { selectView('log'); selectSource('vrc'); }");
    }

    /// <summary>API の行を流し、後ろの見張りが画面に出るまで待つ。記録は 1 本のキューで書くので、見張りが出れば前の行は処理済み。</summary>
    private async Task AppendApiAsync(string user)
    {
        app.AppendLog($"[API] Requesting Get users/{user}", $"[Behaviour] OnPlayerJoined {user}-sentinel");
        // 生行（raw）にも見張りの行が来るので、出来事の行に絞る。
        await Assertions.Expect(Page.Locator("#rowsVrc .row[data-kind='PlayerJoined']", new() { HasText = $"{user}-sentinel" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // 生行は出来事とは別の道を通るので、出来事の見張りだけでは前の生行が処理済みとは言えない。生行は既定で畳んでいる（display: none）ので、数で待つ。
        await Assertions.Expect(RawRow($"{user}-sentinel")).ToHaveCountAsync(1, new() { Timeout = 10_000 });
    }

    /// <summary>その文字を含む生行（raw）。生行は既定で畳んでいるので、見えるかではなく数で見る。</summary>
    private ILocator RawRow(string text) => Page.Locator("#rowsVrc .row[data-kind='raw']", new() { HasText = text });

    private Task<int> StoredApiAsync(string user) => Page.EvaluateAsync<int>(
        """
        async user => {
          const result = await bridge.queryLog(JSON.stringify({
            limit: 500, beforeId: null, linesBefore: null, excludeKinds: [], from: null, to: null, includeLines: false,
          }));
          return (result.rows ?? []).filter(r => r.kind === 'ApiRequest' && JSON.stringify(r).includes(user)).length;
        }
        """, user);

    private ILocator ApiRow(string user) => Page.Locator("#rowsVrc .row[data-kind='ApiRequest']", new() { HasText = $"users/{user}" });

    /// <summary>設定画面で切り替えて「適用」。画面が状態を拾う（5 秒ごとの拾い直し）まで待つ。</summary>
    private async Task SetStoreApiAsync(bool on)
    {
        await app.OpenSettings("log");
        var sw = Page.Locator("#viewSettings .cfg-group[data-module='host'] .cfg-item[data-key='StoreApiRequests'] input[type=checkbox]");
        await Assertions.Expect(sw).ToBeVisibleAsync();
        if (await sw.IsCheckedAsync() != on)
        {
            await sw.ClickAsync();
            await Page.Locator("#cfgCommit").ClickAsync();
        }

        await Page.WaitForFunctionAsync($"() => storeApiRequests === {(on ? "true" : "false")}", null, new() { Timeout = 15_000 });

        // 面は選び直さない（selectSource は畳みを付け直すので、設定変更時の付け直しが壊れていても隠れる）。selectView は畳みにも記録にも触らない。
        await Page.EvaluateAsync("() => selectView('log')");
    }

    [Fact]
    public async Task WhileOffApiRequestsAreNeitherStoredNorShown()
    {
        await SetStoreApiAsync(false);

        await AppendApiAsync("usr_e2e_api_off");

        Assert.Equal(0, await StoredApiAsync("usr_e2e_api_off"));
        Assert.Equal(0, await ApiRow("usr_e2e_api_off").CountAsync());

        // 生行の [API] も来ない（見張りの生行は来ている）。
        Assert.Equal(0, await RawRow("users/usr_e2e_api_off").CountAsync());

        await Page.Locator("#logFilter").ClickAsync();
        await Assertions.Expect(ApiChip).ToHaveClassAsync(Locked);
        await Assertions.Expect(ApiChip.Locator("input.spoitable-switch")).ToBeDisabledAsync();
        await Assertions.Expect(ApiChip.Locator(".desc")).ToContainTextAsync("保存と表示が行われません");
        await Page.Locator("#filterClose").ClickAsync();
    }

    [Fact]
    public async Task TurnedOnTheyAreKeptAndTurnedOffTheyHideButStay()
    {
        try
        {
            await SetStoreApiAsync(true);
            await AppendApiAsync("usr_e2e_api_on");
            Assert.Equal(1, await StoredApiAsync("usr_e2e_api_on"));

            // 入なら生行の [API] も来る。切 → 入の引き直しで、保存していない生行は一覧から抜けるので、ここで見る。
            Assert.Equal(1, await RawRow("users/usr_e2e_api_on").CountAsync());

            await Page.Locator("#logFilter").ClickAsync();
            await Assertions.Expect(ApiChip).Not.ToHaveClassAsync(Locked);
            await Assertions.Expect(ApiChip.Locator("input.spoitable-switch")).ToBeEnabledAsync();
            await ApiChip.Locator("input.spoitable-switch").ClickAsync();
            await Page.Locator("#filterClose").ClickAsync();
            await Assertions.Expect(ApiRow("usr_e2e_api_on")).ToBeVisibleAsync(new() { Timeout = 10_000 });

            await SetStoreApiAsync(false);
            await Assertions.Expect(ApiRow("usr_e2e_api_on")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
            Assert.Equal(1, await StoredApiAsync("usr_e2e_api_on"));
            Assert.False(await Page.EvaluateAsync<bool>("() => hidden.has('ApiRequest')"), "設定で閉じたのが利用者の畳みに混ざった");

            // 新しい行は来ないので、引き直さないと戻らない。
            await SetStoreApiAsync(true);
            await Assertions.Expect(ApiRow("usr_e2e_api_on")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        }
        finally
        {
            await SetStoreApiAsync(false);
            await Page.EvaluateAsync(
                "() => { hidden.add('ApiRequest'); bridge.setHiddenKinds(JSON.stringify([...hidden])); applyFilter(); updateFilterSummary(); }");
        }
    }
}
