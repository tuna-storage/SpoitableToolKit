using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Modules;

/// <summary>同意の文は書き直される前提なので、文の全部ではなく形（チェックの数・押せるか・どこへ進むか）で見る。</summary>
public sealed class ModuleAgreementTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private readonly ModuleScreen _screen = new(app);

    private IPage Page => app.Page;

    private ILocator Dialog => _screen.Dialog;

    private ILocator Continue => Page.Locator("#maContinue");

    private ILocator Url => Page.Locator("#maUrl");

    public async Task InitializeAsync()
    {
        app.GitHub.Publish("someone/agreed", "v1.0.0", FakeGitHubServer.Zip(
            ("module.json", Encoding.UTF8.GetBytes("""{ "id": "agreed", "name": "agreed module", "version": "1.0.0" }""")),
            ("agreed.dll", "not a dll"u8.ToArray())));
        await _screen.OpenAsync();
    }

    /// <summary>件を残さず、同意の段も出す形に戻す（このクラスのアプリは 1 つ）。</summary>
    public async Task DisposeAsync()
    {
        await _screen.ClearJobsAsync();
        await Page.EvaluateAsync("async () => { await bridge.applyConfigChanges(JSON.stringify({ host: { ShowModuleAgreement: true } })); }");
    }

    [Fact]
    public async Task TheAgreementNeedsTheFirstTwoChecksAndBackReturnsToTheUrl()
    {
        // 偽の GitHub はこのクラスで 1 つ（ほかの試験も同じ repo を聞く）。回数は前後の差で見る。
        var asked = AskedForTheRepository();
        await Page.Locator("#plAddBtn").ClickAsync();
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-step", "url");
        await Assertions.Expect(_screen.DialogTitle).ToHaveTextAsync("モジュールを追加");
        await Assertions.Expect(Page.Locator("#maNext")).ToHaveTextAsync("次へ");
        await Assertions.Expect(Url).ToBeFocusedAsync();

        await Url.FillAsync("https://github.com/someone/agreed");
        await Url.PressAsync("Enter");

        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-step", "agree");
        await Assertions.Expect(Page.Locator("#maAgreeStep input[type=checkbox]")).ToHaveCountAsync(3);
        await Assertions.Expect(Page.Locator("#maAgreeStep input[type=checkbox]:checked")).ToHaveCountAsync(0);
        await Assertions.Expect(Continue).ToBeDisabledAsync();
        Assert.Equal(asked, AskedForTheRepository());

        await Page.Locator("#maAgreeRisk").CheckAsync();
        await Assertions.Expect(Continue).ToBeDisabledAsync();
        await Page.Locator("#maAgreeHide").CheckAsync();
        await Assertions.Expect(Continue).ToBeDisabledAsync();
        await Page.Locator("#maAgreeLiability").CheckAsync();
        await Assertions.Expect(Continue).ToBeEnabledAsync();
        await Page.Locator("#maAgreeRisk").UncheckAsync();
        await Assertions.Expect(Continue).ToBeDisabledAsync();

        await Page.Locator("#maBack").ClickAsync();
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-step", "url");
        await Assertions.Expect(Url).ToHaveValueAsync("https://github.com/someone/agreed");

        await Page.Locator("#maNext").ClickAsync();
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-step", "agree");
        await Assertions.Expect(Page.Locator("#maAgreeStep input[type=checkbox]:checked")).ToHaveCountAsync(0);
        await _screen.AgreeAsync();

        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-step", "job");
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "install");
        await Assertions.Expect(_screen.Card("someone/agreed")).ToHaveCountAsync(1);
        Assert.True(ShowAgreementSaved() is null or true, "3 つ目を入れていないのに、同意の段を出さない形で保存した");

        await Dialog.Locator(".pl-job-cancel").ClickAsync();
        await Assertions.Expect(Dialog).ToBeHiddenAsync();

        await _screen.EnterUrlAsync("https://github.com/someone/agreed");
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-step", "agree");
        await _screen.CloseDialogAsync();
    }

    [Fact]
    public async Task DontShowAgainSkipsTheAgreementUntilTheSettingIsTurnedBackOn()
    {
        await _screen.EnterUrlAsync("https://github.com/someone/agreed");
        await _screen.AgreeAsync(hide: true);
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "install");
        await ExpectShowAgreementSavedAsync(false);
        await Dialog.Locator(".pl-job-cancel").ClickAsync();
        await Assertions.Expect(Dialog).ToBeHiddenAsync();

        await _screen.EnterUrlAsync("https://github.com/someone/agreed");
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-step", "job");
        await Assertions.Expect(Page.Locator("#maAgreeStep")).ToBeHiddenAsync();
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "install");
        await Dialog.Locator(".pl-job-cancel").ClickAsync();
        await Assertions.Expect(Dialog).ToBeHiddenAsync();

        await app.OpenSettings("modules");
        var item = Page.Locator("#viewSettings .cfg-group[data-module='host'] .cfg-item[data-key='ShowModuleAgreement']");
        await Assertions.Expect(item).ToContainTextAsync("モジュールの追加時に同意画面を表示する");
        await Assertions.Expect(item.Locator("input[type=checkbox]")).Not.ToBeCheckedAsync();
        await item.Locator("input[type=checkbox]").ClickAsync();
        await Page.Locator("#cfgCommit").ClickAsync();
        await ExpectShowAgreementSavedAsync(true);

        await _screen.OpenAsync();
        await _screen.EnterUrlAsync("https://github.com/someone/agreed");
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-step", "agree");
        await _screen.CloseDialogAsync();
    }

    [Fact]
    public async Task TheDialogClosesWithoutStoppingTheCard()
    {
        await Page.Locator("#plAddBtn").ClickAsync();
        await Url.FillAsync("https://github.com/someone/agreed");
        await Url.PressAsync("Escape");
        await Assertions.Expect(Dialog).ToBeHiddenAsync();

        await Page.Locator("#plAddBtn").ClickAsync();
        await Assertions.Expect(Url).ToHaveValueAsync(string.Empty);
        await Page.Locator("#maUrlCancel").ClickAsync();
        await Assertions.Expect(Dialog).ToBeHiddenAsync();

        await _screen.LookUpAsync("https://github.com/someone/agreed");
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "install");
        await Dialog.ClickAsync(new() { Position = new Position { X = 3, Y = 3 } });
        await Assertions.Expect(Dialog).ToBeHiddenAsync();

        var card = _screen.Card("someone/agreed");
        await Assertions.Expect(card).ToHaveAttributeAsync("data-asking", "install");
        await card.Locator(".pl-job-review").ClickAsync();
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "install");
    }

    /// <summary>VR の窓では、右クリックのメニューが URL を入れるただ 1 つの道（メニューはモーダルの上に出る）。</summary>
    [Fact]
    public async Task TheUrlFieldTakesAPasteFromTheRightClickMenu()
    {
        await Page.Locator("#plAddBtn").ClickAsync();
        await Page.EvaluateAsync("() => bridge.copyToClipboard('https://github.com/someone/agreed')");

        var before = await Page.EvaluateAsync<int>("() => window.__stkCtx?.turns ?? 0");
        await Url.ClickAsync(new() { Button = MouseButton.Right });
        await Page.WaitForFunctionAsync($"() => (window.__stkCtx?.turns ?? 0) > {before}");
        await Assertions.Expect(_screen.Menu).ToBeVisibleAsync();
        await _screen.Menu.Locator("button[data-id='host paste']").ClickAsync();

        await Assertions.Expect(Url).ToHaveValueAsync("https://github.com/someone/agreed");
        await Assertions.Expect(Dialog).ToBeVisibleAsync();
        await Page.Locator("#maNext").ClickAsync();
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-step", "agree");
        await _screen.CloseDialogAsync();
    }

    [Fact]
    public async Task EscapeOnThePasteMenuClosesOnlyTheMenu()
    {
        await Page.Locator("#plAddBtn").ClickAsync();
        await Url.FillAsync("https://github.com/someone/agreed");

        var before = await Page.EvaluateAsync<int>("() => window.__stkCtx?.turns ?? 0");
        await Url.ClickAsync(new() { Button = MouseButton.Right });
        await Page.WaitForFunctionAsync($"() => (window.__stkCtx?.turns ?? 0) > {before}");
        await Assertions.Expect(_screen.Menu).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(_screen.Menu).ToBeHiddenAsync();
        await Assertions.Expect(Dialog).ToBeVisibleAsync();
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-step", "url");
        await Assertions.Expect(Url).ToHaveValueAsync("https://github.com/someone/agreed");

        await _screen.CloseDialogAsync();
    }

    [Fact]
    public async Task MovingToAnotherViewClosesTheDialogButNotTheCard()
    {
        await _screen.LookUpAsync("https://github.com/someone/agreed");
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "install");

        await Page.EvaluateAsync("() => selectView('log')");

        await Assertions.Expect(Dialog).ToBeHiddenAsync();
        await _screen.OpenAsync();
        await Assertions.Expect(_screen.Card("someone/agreed")).ToHaveAttributeAsync("data-asking", "install");
        await Assertions.Expect(Dialog).ToBeHiddenAsync();
    }

    [Fact]
    public async Task AnAnswerForAClosedDialogIsDropped()
    {
        await Page.EvaluateAsync("""
            () => {
              window.__e2eCheck = bridge.checkModuleUrl;
              window.__e2eHeld = null;
              bridge.checkModuleUrl = url => {
                if (window.__e2eHeld) return window.__e2eCheck(url);
                return new Promise(done => {
                  window.__e2eHeld = () => new Promise(settled => {
                    const answer = window.__e2eCheck(url);
                    done(answer);
                    answer.then(() => setTimeout(settled, 50));
                  });
                });
              };
            }
            """);
        try
        {
            await Page.Locator("#plAddBtn").ClickAsync();
            await Url.FillAsync("https://github.com/someone/agreed");
            await Page.Locator("#maNext").ClickAsync();
            await Page.WaitForFunctionAsync("() => !!window.__e2eHeld");
            await _screen.CloseDialogAsync();

            await _screen.EnterUrlAsync("https://example.com/someone/module");
            await Assertions.Expect(_screen.UrlMessage.Locator(".t")).ToHaveTextAsync("この URL からはインストールできません");

            await Page.EvaluateAsync("() => window.__e2eHeld()");

            await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-step", "url");
            await Assertions.Expect(Url).ToHaveValueAsync("https://example.com/someone/module");
            await Assertions.Expect(_screen.UrlMessage.Locator(".t")).ToHaveTextAsync("この URL からはインストールできません");
            await Assertions.Expect(Page.Locator("#maNext")).ToBeEnabledAsync();
            await _screen.CloseDialogAsync();
        }
        finally
        {
            await Page.EvaluateAsync("() => { bridge.checkModuleUrl = window.__e2eCheck; }");
        }
    }

    /// <summary>本体の答えを「断った」に差し替えて、画面の側を見る（本体が断るのは、もう 12 件ある・閉じている途中のとき）。</summary>
    [Fact]
    public async Task AnInstallThatCannotStartSaysWhyInTheDialog()
    {
        await Page.EvaluateAsync("""
            () => {
              window.__e2eStart = bridge.startModuleJob;
              bridge.startModuleJob = async () => ({ ok: false, id: null, error: 'e2e: refused' });
            }
            """);
        try
        {
            await _screen.EnterUrlAsync("https://github.com/someone/agreed");
            await _screen.AgreeAsync();

            await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-step", "url");
            await Assertions.Expect(_screen.UrlMessage.Locator(".t")).ToHaveTextAsync("インストールの開始に失敗しました");
            await Assertions.Expect(_screen.UrlMessage.Locator(".d")).ToHaveTextAsync("e2e: refused");
            await Assertions.Expect(Url).ToHaveValueAsync("https://github.com/someone/agreed");
            await Assertions.Expect(_screen.Cards).ToHaveCountAsync(0);
            await _screen.CloseDialogAsync();

            // 行の「更新」と同じ道（その行の開発元の URL で始める）。
            await Page.EvaluateAsync("() => startModuleUpdate({ source: 'someone/agreed' })");

            await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-step", "refused");
            await Assertions.Expect(_screen.DialogTitle).ToHaveTextAsync("someone/agreed");
            await Assertions.Expect(Dialog.Locator(".pl-job-fail .t")).ToHaveTextAsync("インストールの開始に失敗しました");
            await Assertions.Expect(Dialog.Locator(".pl-job-fail .reason")).ToHaveTextAsync("e2e: refused");
            await Assertions.Expect(Page.Locator("#maAgreeStep")).ToBeHiddenAsync();
            await Dialog.Locator(".ma-close").ClickAsync();
            await Assertions.Expect(Dialog).ToBeHiddenAsync();
            await Assertions.Expect(_screen.Cards).ToHaveCountAsync(0);
        }
        finally
        {
            await Page.EvaluateAsync("() => { bridge.startModuleJob = window.__e2eStart; }");
        }
    }

    private int AskedForTheRepository() => app.GitHub.Requests.Count(path => path.Contains("someone/agreed", StringComparison.Ordinal));

    private bool? ShowAgreementSaved()
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(Path.Combine(app.DataDirectory, "config.json")))?["ShowModuleAgreement"]?.GetValue<bool>();
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private async Task ExpectShowAgreementSavedAsync(bool expected)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (ShowAgreementSaved() == expected) return;
            await Task.Delay(100);
        }

        Assert.Fail($"config.json の ShowModuleAgreement が {expected} にならなかった（{ShowAgreementSaved()}）");
    }
}
