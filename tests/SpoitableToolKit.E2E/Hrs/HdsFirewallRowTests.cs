using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Hrs;

/// <summary>
/// 心拍の設定の「Apple Watch (HDS)」にある「ファイアウォール」の行（値ではない行。SDK の SpoitableSettingsAction の最初の使い手）。
/// 待ち受けポートのすぐ下に出て、状態は裏で分かったらその行だけ描き直され、ボタンは「適用」を待たずにその場で走る。
/// ファイアウォールはファイル（<see cref="HdsFirewallAppFixture"/>）なので、本物の PowerShell も UAC も起きない。
/// </summary>
public sealed class HdsFirewallRowTests(HdsFirewallAppFixture app) : IClassFixture<HdsFirewallAppFixture>, IAsyncLifetime
{
    private IPage Page => app.Page;

    private ILocator Item(string key) => Page.Locator($"#viewSettings .cfg-group[data-module='hrs'] .cfg-item[data-key='{key}']");

    private ILocator Row => Item("Hds.Firewall");

    private ILocator Badge => Row.Locator(".cfg-action-state");

    private ILocator Button => Row.Locator(".cfg-action-run");

    private ILocator ApplyFirst => Row.Locator(".cfg-action-wait");

    /// <summary>この試験より前に「追加」された回数（記録はクラスの中で積もる）。</summary>
    private int _addedBefore;

    private string[] AddedInThisTest() => [.. app.AddedPorts().Skip(_addedBefore)];

    public async Task InitializeAsync()
    {
        if (await Page.Locator("#modal").IsVisibleAsync()) await Page.Locator("#modalNo").ClickAsync();

        await app.OpenSettings("hrs");

        // どの試験も「見当たらない」から始める（前の試験が直した・変えたものを引きずらない）。
        await ResetAsync("missing", "見当たりません");
        _addedBefore = app.AddedPorts().Length;
    }

    public async Task DisposeAsync()
    {
        if (await Page.Locator("#cfgApply").IsVisibleAsync()) await Page.Locator("#cfgCancel").ClickAsync();
        await Page.EvaluateAsync("() => { el.cfgSearch.value = ''; applyCfgSearch(); el.cfgScroll.style.maxWidth = ''; }");
    }

    /// <summary>
    /// ファイアウォールの答えを置き直し、ポートを空いている番号へ変えて適用する（適用すると hrs が見直す）。
    /// そのポートの文が出るまで待つ — 札だけを待つと、前の答えの札のままで通ってしまう。
    /// </summary>
    private async Task ResetAsync(string firewall, string badge)
    {
        app.SetFirewall(firewall);
        app.Port = AppFixture.FreePort();
        await Page.EvaluateAsync("port => bridge.applyConfigChanges(JSON.stringify({ hrs: { 'Hds.Port': port } }))", app.Port);

        await Assertions.Expect(Row.Locator(".cfg-action-detail")).ToContainTextAsync($"TCP {app.Port}");
        await Assertions.Expect(Badge).ToHaveTextAsync(badge);

        // 適用のあとの読み直し（画面ごと描き直す）も済むまで待つ。答えは push で先に届くことがあり、そのあとで読み直しが行を差し替えると、
        // 次に打った値が消える。ポートの欄を変えるのは読み直しだけ（push は動きの行しか触らない）。
        await Assertions.Expect(Item("Hds.Port").Locator("input[type=number]")).ToHaveValueAsync(app.Port.ToString());
    }

    [Fact]
    public async Task TheRowSitsRightBelowThePortAndOffersToAddTheRule()
    {
        await Assertions.Expect(Row).ToBeVisibleAsync();
        await Assertions.Expect(Row.Locator(".meta .name")).ToContainTextAsync("ファイアウォール");

        var above = await Row.EvaluateAsync<string>("row => row.closest('.cfg-row').previousElementSibling.querySelector('.cfg-item').dataset.key");
        Assert.Equal("Hds.Port", above);

        Assert.Equal("warn", await Badge.GetAttributeAsync("data-tone"));
        await Assertions.Expect(Button).ToHaveTextAsync("ファイアウォールに追加");
        await Assertions.Expect(Button).ToBeEnabledAsync();
        await Assertions.Expect(Row.Locator(".cfg-action-note")).ToHaveTextAsync("管理者の確認が表示されます");
        await Assertions.Expect(ApplyFirst).ToBeHiddenAsync();
        await Assertions.Expect(Row.Locator(".cfg-action-copy code")).ToContainTextAsync($"-LocalPort {app.Port}");

        // 見るだけでは足さない（UAC はボタンを押したときだけ）。
        Assert.Empty(AddedInThisTest());
    }

    /// <summary>設定の検索は、行の名前で当たる（値の行と同じ）。</summary>
    [Fact]
    public async Task SearchFindsTheRowByItsLabel()
    {
        await Page.Locator("#cfgSearch").FillAsync("ファイアウォール");

        await Assertions.Expect(Row).ToBeVisibleAsync();
        await Assertions.Expect(Item("Hds.Port")).ToBeHiddenAsync();
        Assert.False(await Row.EvaluateAsync<bool>("row => row.closest('.cfg-row').classList.contains('nomatch')"));
    }

    /// <summary>
    /// 動きの行の、見えていないことのある字（「変更を適用すると使えます」・コピーのボタン・コマンド）や、状態の説明・ボタンの注記には当たらない。
    /// 当たるのは名前と説明だけ（「コピー」「適用」で、何も見えていない行が出てこないように）。
    /// </summary>
    [Fact]
    public async Task SearchDoesNotHitTheRowsHelperTexts()
    {
        foreach (var needle in new[] { "変更を適用すると使えます", "コピー", "New-NetFirewallRule", "受信ポート TCP", "管理者の確認" })
        {
            await Page.Locator("#cfgSearch").FillAsync(needle);
            Assert.True(await Row.EvaluateAsync<bool>("row => row.closest('.cfg-row').classList.contains('nomatch')"), $"「{needle}」で当たった");
        }

        await Page.Locator("#cfgSearch").FillAsync("ルールはボタンを押したときにだけ追加されます");
        await Assertions.Expect(Row).ToBeVisibleAsync();
    }

    /// <summary>
    /// VR の窓の幅（本文が狭い）でも、設定の画面が横に巻かない。長いコマンドは折り返す —
    /// 折り返さないと、行もその囲みも一緒に広がるので、行やコマンドの欄の中では何もはみ出さず、設定の画面ごと横に広がる。
    /// </summary>
    [Fact]
    public async Task TheRowWrapsInANarrowWindow()
    {
        await Page.EvaluateAsync("() => { el.cfgScroll.style.maxWidth = '26em'; }");
        await Assertions.Expect(Row.Locator(".cfg-action-copy code")).ToBeVisibleAsync();

        var overflow = await Row.EvaluateAsync<JsonElement>(
            """
            row => {
              const code = row.querySelector('.cfg-action-copy code');
              const style = getComputedStyle(code);
              return {
                page: el.cfgScroll.scrollWidth - el.cfgScroll.clientWidth,
                spill: Math.ceil(row.getBoundingClientRect().right - el.cfgScroll.getBoundingClientRect().right),
                lines: Math.round((code.clientHeight - parseFloat(style.paddingTop) - parseFloat(style.paddingBottom)) / parseFloat(style.lineHeight)),
              };
            }
            """);

        Assert.True(overflow.GetProperty("page").GetInt32() <= 0, $"設定の画面が横に巻く（{overflow}）");
        Assert.True(overflow.GetProperty("spill").GetInt32() <= 0, $"行が設定の画面の右へはみ出している（{overflow}）");
        Assert.True(overflow.GetProperty("lines").GetInt32() > 1, $"コマンドが折り返していない（{overflow}）");
    }

    /// <summary>
    /// 裏で状態が変わったら（ここではパネルの「追加」が直した）、その行だけが描き直される。同じページで打ちかけの変更は消えない。
    /// </summary>
    [Fact]
    public async Task AChangeFromTheBackgroundRepaintsOnlyTheRow()
    {
        var seconds = Item("NoSignalSeconds").Locator("input[type=number]");
        await seconds.FillAsync("42");
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeVisibleAsync();

        await Page.EvaluateAsync("() => bridge.callModule('hrs', 'addFirewallRule', '{}')");

        await Assertions.Expect(Badge).ToHaveTextAsync("許可されています");
        Assert.Equal("good", await Badge.GetAttributeAsync("data-tone"));
        await Assertions.Expect(Button).ToBeHiddenAsync();

        await Assertions.Expect(seconds).ToHaveValueAsync("42");
        await Assertions.Expect(Item("NoSignalSeconds")).ToHaveClassAsync(new Regex(@"\bdirty\b"));
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeVisibleAsync();
    }

    /// <summary>ボタンは「適用」を待たずにその場で走り（溜めにも乗らない）、直ったら行が変わる。</summary>
    [Fact]
    public async Task PressingTheButtonRunsTheActionAndTheRowFollows()
    {
        await Button.ClickAsync();

        await Assertions.Expect(Badge).ToHaveTextAsync("許可されています");
        await Assertions.Expect(Button).ToBeHiddenAsync();
        await Assertions.Expect(Row.Locator(".cfg-action-copy")).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeHiddenAsync();

        Assert.Equal([app.Port.ToString()], AddedInThisTest());
    }

    /// <summary>
    /// ポートを打ち替えて適用する前は、状態はまだ前のポートの話なので、ボタンの代わりに「変更を適用すると使えます」。
    /// 元に戻すか適用すると、ボタンが戻る（適用したら新しいポートで見直す）。
    /// </summary>
    [Fact]
    public async Task EditingThePortAsksToApplyBeforeTheButton()
    {
        var port = Item("Hds.Port").Locator("input[type=number]");
        var before = app.Port;
        var next = AppFixture.FreePort();

        await port.FillAsync(next.ToString());
        await Assertions.Expect(ApplyFirst).ToHaveTextAsync("変更を適用すると使えます");
        await Assertions.Expect(Button).ToBeHiddenAsync();

        await port.FillAsync(before.ToString());
        await Assertions.Expect(Button).ToBeVisibleAsync();
        await Assertions.Expect(ApplyFirst).ToBeHiddenAsync();

        await port.FillAsync(next.ToString());
        await Assertions.Expect(ApplyFirst).ToBeVisibleAsync();
        await Page.Locator("#cfgCommit").ClickAsync();
        app.Port = next;

        await Assertions.Expect(Row.Locator(".cfg-action-detail")).ToContainTextAsync($"TCP {next}");
        await Assertions.Expect(Button).ToBeVisibleAsync();
        await Assertions.Expect(ApplyFirst).ToBeHiddenAsync();
    }

    /// <summary>
    /// 値ごとに断られた「適用」（入った値と、モジュールに断られた値がある）のあと、画面は断られた値だけを溜めに残して描き直す。
    /// 残った中に前提の設定（ポート）があれば、描き直したあとも行は「変更を適用すると使えます」のまま（前のポートの話でボタンを出さない）。
    /// <para>
    /// 描き直すのは、本体の答えに「入った値」があるときだけ（<c>applied</c>。値ごとに断ったモジュールの分しか載らない）。
    /// 同梱のモジュールで値ごとに断るのは hrs の心拍計の指定・パラメータ名・ゾーンだけで、ポートは断らない（hrs が値ごとに断るときは、ポートは入る）。
    /// そこで、ポートをモジュールが断ったときに本体が返す答え（入った値と、断られた値の一言）を、画面の橋の口を 1 回だけ包んで作る。入る方の値は本物の口で入れる。
    /// </para>
    /// </summary>
    [Fact]
    public async Task APartlyRefusedApplyThatKeepsThePortStillAsksToApplyFirst()
    {
        var seconds = Item("Hds.NoSignalSeconds").Locator("input[type=number]");
        var before = await seconds.InputValueAsync();

        try
        {
            await seconds.FillAsync(before == "60" ? "61" : "60");
            await Item("Hds.Port").Locator("input[type=number]").FillAsync(AppFixture.FreePort().ToString());
            await Assertions.Expect(ApplyFirst).ToBeVisibleAsync();

            await Page.EvaluateAsync(
                """
                () => {
                  const real = window.__e2eRealApply = bridge.applyConfigChanges;
                  bridge.applyConfigChanges = async json => {
                    bridge.applyConfigChanges = real;
                    const changes = JSON.parse(json);
                    delete changes.hrs['Hds.Port'];
                    const reply = await real(JSON.stringify(changes));
                    return {
                      ok: false,
                      notes: [...reply.notes, { module: 'hrs', key: 'Hds.Port', message: 'e2e: refused by the module' }],
                      applied: [...reply.applied, ...Object.keys(changes.hrs).map(key => ({ module: 'hrs', key }))],
                    };
                  };
                }
                """);

            await Page.Locator("#cfgCommit").ClickAsync();

            // 描き直された: 入った方は溜めから外れ、断られたポートだけが残った（赤く、「変更済み」のまま）。
            await Assertions.Expect(Item("Hds.Port")).ToHaveClassAsync(new Regex(@"\bbad\b"));
            await Assertions.Expect(Item("Hds.Port")).ToHaveClassAsync(new Regex(@"\bdirty\b"));
            await Assertions.Expect(Item("Hds.NoSignalSeconds")).Not.ToHaveClassAsync(new Regex(@"\bdirty\b"));

            await Assertions.Expect(ApplyFirst).ToBeVisibleAsync();
            await Assertions.Expect(Button).ToBeHiddenAsync();
        }
        finally
        {
            await Page.EvaluateAsync("() => { if (window.__e2eRealApply) bridge.applyConfigChanges = window.__e2eRealApply; delete window.__e2eRealApply; }");
            if (await Page.Locator("#cfgApply").IsVisibleAsync()) await Page.Locator("#cfgCancel").ClickAsync();
            await Page.EvaluateAsync("value => bridge.applyConfigChanges(JSON.stringify({ hrs: { 'Hds.NoSignalSeconds': value } }))", int.Parse(before));
        }
    }

    /// <summary>ブロックのときは「ブロックを削除して許可」。手で足しても届かないので、コピーの 1 行は出さない。</summary>
    [Fact]
    public async Task ABlockSaysSoAndOffersToRemoveIt()
    {
        await ResetAsync("blocked", "ブロックされています");

        Assert.Equal("bad", await Badge.GetAttributeAsync("data-tone"));
        await Assertions.Expect(Button).ToHaveTextAsync("ブロックを削除して許可");
        await Assertions.Expect(Row.Locator(".cfg-action-copy")).ToBeHiddenAsync();
    }

    /// <summary>コピーはアプリのクリップボード（試験のアプリは中だけで持つ）へ、そのポートのコマンドを書く。</summary>
    [Fact]
    public async Task CopyPutsTheCommandOnTheClipboard()
    {
        var copy = Row.Locator(".cfg-action-copy-btn");
        await copy.ClickAsync();
        await Assertions.Expect(copy).ToHaveTextAsync("コピーしました");

        var pasted = await Page.EvaluateAsync<JsonElement>(
            "() => bridge.invokeContextMenu(JSON.stringify({ id: 'host paste', editable: true }))");
        var text = pasted.GetProperty("text").GetString();

        Assert.StartsWith($"New-NetFirewallRule -DisplayName 'SpoitableToolKit HDS ({app.Port})'", text);
        Assert.Contains($"-LocalPort {app.Port} -Profile Private,Domain", text);
        Assert.Equal(await Row.Locator(".cfg-action-copy code").TextContentAsync(), text);
    }

    /// <summary>言語を変えると、行の札・ボタン・説明もモジュールが新しい言語で引き直す。</summary>
    [Fact]
    public async Task SwitchingTheLanguageRedescribesTheRow()
    {
        try
        {
            await Page.EvaluateAsync("() => bridge.applyConfigChanges(JSON.stringify({ host: { Language: 'En' } }))");

            await Assertions.Expect(Badge).ToHaveTextAsync("Not found");
            await Assertions.Expect(Button).ToHaveTextAsync("Add firewall rule");
            await Assertions.Expect(Row.Locator(".meta .name")).ToContainTextAsync("Firewall");
            await Assertions.Expect(Row.Locator(".cfg-action-detail")).ToContainTextAsync($"No firewall rule allows TCP {app.Port}");
        }
        finally
        {
            await Page.EvaluateAsync("() => bridge.applyConfigChanges(JSON.stringify({ host: { Language: 'Ja' } }))");
            await Assertions.Expect(Badge).ToHaveTextAsync("見当たりません");
        }
    }
}
