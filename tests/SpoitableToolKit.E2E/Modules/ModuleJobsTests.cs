using System.Text;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Modules;

/// <summary>
/// 「途中」は、偽の GitHub の答えを足止めして作る（<see cref="FakeGitHubServer.Hold"/>）。
/// 台帳そのものの決まり（答えは 1 回・同じ repo は 1 件・.NET SDK は 1 本）は unit（<c>ModuleInstallJobsTests</c>）で見る。
/// </summary>
public sealed class ModuleJobsTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private readonly ModuleScreen _screen = new(app);

    private IPage Page => app.Page;

    private ILocator Dialog => _screen.Dialog;

    public async Task InitializeAsync() => await _screen.OpenAsync();

    /// <summary>カードも入れかけも残さず、設定も戻す（このクラスのアプリは 1 つ）。</summary>
    public async Task DisposeAsync()
    {
        await Page.EvaluateAsync("() => { if (window.__e2eAnswer) { bridge.answerModuleJob = window.__e2eAnswer; window.__e2eAnswer = null; } }");
        await _screen.ClearJobsAsync();
        await Page.EvaluateAsync("""
            async () => {
              await bridge.applyConfigChanges(JSON.stringify({ host: { ParallelModuleBuilds: true } }));
              for (const id of ['quick', 'kept', 'stubborn']) await bridge.cancelPendingModule(id);
            }
            """);
        await Assertions.Expect(_screen.RestartBar).ToBeHiddenAsync();
    }

    /// <summary>形だけのモジュール（中身は dll ではない。読み込ませない）。</summary>
    private static byte[] Shell(string id) => FakeGitHubServer.Zip(
        ("module.json", Encoding.UTF8.GetBytes($"{{ \"id\": \"{id}\", \"name\": \"{id} module\", \"version\": \"1.0.0\" }}")),
        ($"{id}.dll", "not a dll"u8.ToArray()));

    /// <summary>Build するものが無いソース（Build は「モジュールの project が無い」で、すぐ失敗する）。</summary>
    private static byte[] NothingToBuild(string top) => FakeGitHubServer.Zip(($"{top}/README.md", "# nothing to build"u8.ToArray()));

    /// <summary>リリースのある repo を追加して、モーダルが「インストールしますか」を聞くところまで。返すのはその件の id。</summary>
    private async Task<string> AskedToInstallAsync(string repository, string id)
    {
        app.GitHub.Publish(repository, "v1.0.0", Shell(id));
        await _screen.LookUpAsync($"https://github.com/{repository}");
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "install");
        return await Dialog.GetAttributeAsync("data-id") ?? throw new InvalidOperationException("モーダルに id が無い");
    }

    [Fact]
    public async Task SeveralModulesAreInstalledSideBySide()
    {
        app.GitHub.Publish("someone/slow", "v1.0.0", Shell("slow"), asset: "slow.zip");
        app.GitHub.Publish("someone/quick", "v1.0.0", Shell("quick"));

        using (app.GitHub.Hold("/download/someone/slow/"))
        {
            await _screen.LookUpAsync("https://github.com/someone/slow");
            await _screen.WaitUntilAskedAsync("/download/someone/slow/slow.zip");

            await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-state", "downloading");
            await Assertions.Expect(Dialog.Locator(".pl-job-status .what")).ToHaveTextAsync("slow.zip をダウンロード中");
            await Assertions.Expect(Dialog.Locator(".pl-job-status .amount")).ToHaveTextAsync("0/1KB");
            await Assertions.Expect(Dialog.Locator(".pl-job-bar")).ToHaveAttributeAsync("data-kind", "part");
            await Assertions.Expect(Dialog.Locator(".pl-job-cancel")).ToBeEnabledAsync();
            var slow = _screen.Card("someone/slow");
            var slowId = await Dialog.GetAttributeAsync("data-id");
            await Assertions.Expect(slow).ToHaveAttributeAsync("data-id", slowId!);
            await Assertions.Expect(slow).ToHaveAttributeAsync("data-state", "downloading");
            await Assertions.Expect(slow.Locator(".pl-job-status .what")).ToHaveTextAsync("slow.zip をダウンロード中");
            await Assertions.Expect(slow.Locator(".pl-job-status .amount")).ToHaveTextAsync("0/1KB");
            await Assertions.Expect(slow.Locator(".pl-job-status .step")).ToHaveCountAsync(0);
            await Assertions.Expect(slow.Locator(".pl-job-bar")).ToHaveAttributeAsync("data-kind", "part");
            await Assertions.Expect(slow.Locator(".pl-job-head .pl-job-cancel")).ToBeEnabledAsync();
            await Assertions.Expect(_screen.JobsCount).ToHaveTextAsync("残り 1 件");

            await Dialog.Locator(".ma-close").ClickAsync();
            await Assertions.Expect(Dialog).ToBeHiddenAsync();
            await Assertions.Expect(slow).ToHaveAttributeAsync("data-state", "downloading");

            await _screen.LookUpAsync("https://github.com/someone/quick");

            await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "install");
            var quick = _screen.Card("someone/quick");
            await Assertions.Expect(quick).ToHaveAttributeAsync("data-asking", "install");
            await Assertions.Expect(slow).ToHaveAttributeAsync("data-state", "downloading");
            await Assertions.Expect(_screen.Cards).ToHaveCountAsync(2);
            await Assertions.Expect(_screen.Cards.Nth(0)).ToHaveAttributeAsync("aria-label", "someone/slow");
            await Assertions.Expect(_screen.Cards.Nth(1)).ToHaveAttributeAsync("aria-label", "someone/quick");
            await Assertions.Expect(_screen.JobsCount).ToHaveTextAsync("残り 2 件");
            await _screen.CloseDialogAsync();
            await Assertions.Expect(quick.Locator(".pl-job-status .what")).ToHaveTextAsync("確認待ち");

            await _screen.LookUpAsync("https://github.com/someone/slow");
            await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-id", slowId!);
            await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-state", "downloading");
            await Assertions.Expect(_screen.Cards).ToHaveCountAsync(2);
            Assert.Equal(1, _screen.Asked("/download/someone/slow/slow.zip"));
            await _screen.CloseDialogAsync();

            await quick.Locator(".pl-job-review").ClickAsync();
            await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "install");
            await Assertions.Expect(_screen.DialogTitle).ToHaveTextAsync("someone/quick");
            await Dialog.Locator(".pl-job-yes").ClickAsync();

            await Assertions.Expect(Dialog).ToBeHiddenAsync();
            await Assertions.Expect(_screen.Row("quick").Locator(".pl-state")).ToHaveTextAsync("再起動後に有効化");
            await Assertions.Expect(_screen.Cards).ToHaveCountAsync(1);
            await Assertions.Expect(slow).ToHaveAttributeAsync("data-state", "downloading");
            await Assertions.Expect(_screen.JobsCount).ToHaveTextAsync("残り 1 件");
        }

        var arrived = _screen.Card("someone/slow");
        await Assertions.Expect(arrived).ToHaveAttributeAsync("data-asking", "install");
        await Assertions.Expect(arrived.Locator(".pl-job-status .what")).ToHaveTextAsync("確認待ち");
        await Assertions.Expect(Dialog).ToBeHiddenAsync();
        await arrived.Locator(".pl-job-review").ClickAsync();
        await Assertions.Expect(Dialog.Locator(".q")).ToHaveTextAsync("slow module 1.0.0 をインストールしますか?");
    }

    /// <summary>状態は本体が持っているので、画面を読み直しても同じカードが出る。どの質問への答えかは、本体から受け取り直す。</summary>
    [Fact]
    public async Task TheCardsAreStillThereAfterThePageIsReloaded()
    {
        var id = await AskedToInstallAsync("someone/kept", "kept");

        await Page.ReloadAsync();
        await Page.WaitForFunctionAsync("() => window.__stkReady === true", null, new() { Timeout = 30_000 });
        await _screen.OpenAsync();

        var card = _screen.Card("someone/kept");
        await Assertions.Expect(card).ToHaveAttributeAsync("data-id", id);
        await Assertions.Expect(card).ToHaveAttributeAsync("data-asking", "install");
        await Assertions.Expect(_screen.JobsCount).ToHaveTextAsync("残り 1 件");
        await Assertions.Expect(Dialog).ToBeHiddenAsync();

        await card.Locator(".pl-job-review").ClickAsync();
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-id", id);
        await Assertions.Expect(Dialog.Locator(".q")).ToHaveTextAsync("kept module 1.0.0 をインストールしますか?");
        await Assertions.Expect(ModuleScreen.Fact(Dialog, "開発元")).ToHaveTextAsync("someone/kept v1.0.0 module.zip");
        await Dialog.Locator(".pl-job-yes").ClickAsync();

        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(0);
        await Assertions.Expect(_screen.Row("kept").Locator(".pl-state")).ToHaveTextAsync("再起動後に有効化");
    }

    [Fact]
    public async Task WithParallelBuildsOffTheSecondBuildWaitsForTheFirst()
    {
        const string first = "1111111111111111111111111111111111111111";
        const string second = "2222222222222222222222222222222222222222";
        app.GitHub.PublishSource("someone/first", first, NothingToBuild("someone-first-1111111"));
        app.GitHub.PublishSource("someone/second", second, NothingToBuild("someone-second-2222222"));

        await SetParallelBuildsAsync(false);
        await _screen.OpenAsync();

        // どちらのソースも足止めする（2 つ目は、始まったことを見てから放す）。
        using (app.GitHub.Hold("/repos/someone/first/zipball/"))
        using (app.GitHub.Hold("/repos/someone/second/zipball/"))
        {
            await _screen.LookUpAsync("https://github.com/someone/first");
            await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "build");
            await Dialog.Locator(".pl-job-yes").ClickAsync();
            await _screen.WaitUntilAskedAsync($"/repos/someone/first/zipball/{first}");
            var building = _screen.Card("someone/first");
            await Assertions.Expect(building).ToHaveAttributeAsync("data-state", "building");
            await Assertions.Expect(Dialog).ToBeHiddenAsync();

            await _screen.LookUpAsync("https://github.com/someone/second");
            await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "build");
            await Dialog.Locator(".pl-job-yes").ClickAsync();

            var waiting = _screen.Card("someone/second");
            await Assertions.Expect(waiting).ToHaveAttributeAsync("data-state", "queued");
            await Assertions.Expect(Dialog).ToBeHiddenAsync();
            await Assertions.Expect(waiting.Locator(".pl-job-status .what")).ToHaveTextAsync("someone/first のBuild完了を待機中...");
            await Assertions.Expect(waiting.Locator(".pl-job-status .detail")).ToHaveCountAsync(0);
            await Assertions.Expect(waiting.Locator(".pl-job-status .time")).ToHaveCountAsync(0);
            await Assertions.Expect(waiting.Locator(".pl-job-bar")).ToHaveAttributeAsync("data-kind", "idle");
            await Assertions.Expect(waiting.Locator(".pl-job-head .commit")).ToHaveTextAsync("commit: 2222222");
            await Assertions.Expect(waiting.Locator(".pl-job-head .pl-job-cancel")).ToBeEnabledAsync();
            await Assertions.Expect(building).ToHaveAttributeAsync("data-state", "building");
            Assert.Equal(0, _screen.Asked($"/repos/someone/second/zipball/{second}"));

            await SetParallelBuildsAsync(true);
            await _screen.OpenAsync();
            await _screen.WaitUntilAskedAsync($"/repos/someone/second/zipball/{second}");
            await Assertions.Expect(waiting).ToHaveAttributeAsync("data-state", "building");
            await Assertions.Expect(building).ToHaveAttributeAsync("data-state", "building");
        }

        await Assertions.Expect(_screen.Card("someone/first")).ToHaveAttributeAsync("data-state", "failed", new() { Timeout = 60_000 });
        await Assertions.Expect(_screen.Card("someone/second")).ToHaveAttributeAsync("data-state", "failed", new() { Timeout = 60_000 });
        await Assertions.Expect(_screen.Card("someone/second").Locator(".pl-job-fail .reason")).ToContainTextAsync(
            "someone/second at 2222222 could not be built: no module project was found");
        Assert.Equal(1, _screen.Asked($"/repos/someone/second/zipball/{second}"));
    }

    /// <summary>設定の画面で「並列 Build」を切り替えて「適用」し、本体に届くまで待つ。</summary>
    private async Task SetParallelBuildsAsync(bool on)
    {
        await app.OpenSettings("modules");
        var parallel = Page.Locator(
            "#viewSettings .cfg-group[data-module='host'] .cfg-item[data-key='ParallelModuleBuilds'] input[type=checkbox]");
        if (on) await Assertions.Expect(parallel).Not.ToBeCheckedAsync();
        else await Assertions.Expect(parallel).ToBeCheckedAsync();
        await parallel.ClickAsync();
        await Page.Locator("#cfgCommit").ClickAsync();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var applied = await Page.EvaluateAsync<bool>("""
                async on => {
                  const host = (await bridge.getConfigs()).find(group => group.module === 'host');
                  return JSON.parse(host.items.find(item => item.key === 'ParallelModuleBuilds').value) === on;
                }
                """, on);
            if (applied) return;
            await Task.Delay(100);
        }

        Assert.Fail("設定が本体に届かなかった");
    }

    [Fact]
    public async Task CancellingWhileItBuildsRemovesTheCardAndLeavesNothing()
    {
        const string commit = "3333333333333333333333333333333333333333";
        app.GitHub.PublishSource("someone/halfway", commit, NothingToBuild("someone-halfway-3333333"));

        using var held = app.GitHub.Hold("/repos/someone/halfway/zipball/");
        await _screen.LookUpAsync("https://github.com/someone/halfway");
        await Dialog.Locator(".pl-job-yes").ClickAsync();
        await _screen.WaitUntilAskedAsync($"/repos/someone/halfway/zipball/{commit}");
        var card = _screen.Card("someone/halfway");
        await Assertions.Expect(card).ToHaveAttributeAsync("data-state", "building");

        await card.Locator(".pl-job-head .pl-job-cancel").ClickAsync();

        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(0);
        await Assertions.Expect(_screen.JobsHead).ToBeHiddenAsync();
        await ModuleScreen.ExpectNoDirectoriesAsync(_screen.BuildFolder);
        await ModuleScreen.ExpectNoDirectoriesAsync(_screen.Staging);

        held.Dispose();
        await Task.Delay(300);
        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(0);
    }

    /// <summary>13 件目は URL の段のまま、始められないことと理由を欄の下に出す（入れた URL は消さない。同意も聞かない）。</summary>
    [Fact]
    public async Task MoreThanTwelveAtOnceAreRefusedUnderTheUrlField()
    {
        using var held = app.GitHub.Hold("/repos/crowd/");
        for (var number = 1; number <= 12; number++)
        {
            await _screen.LookUpAsync($"https://github.com/crowd/p{number}");
            await Assertions.Expect(_screen.Cards).ToHaveCountAsync(number);
            await _screen.CloseDialogAsync();
        }

        var looking = _screen.Card("crowd/p1");
        await Assertions.Expect(looking).ToHaveAttributeAsync("data-state", "looking");
        await Assertions.Expect(looking.Locator(".pl-job-status .what")).ToHaveTextAsync("GitHubへアクセス中...");
        await Assertions.Expect(looking.Locator(".pl-job-bar")).ToHaveAttributeAsync("data-kind", "flow");
        await Assertions.Expect(_screen.JobsCount).ToHaveTextAsync("残り 12 件");

        await _screen.EnterUrlAsync("https://github.com/crowd/p13");

        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-step", "url");
        await Assertions.Expect(_screen.UrlMessage.Locator(".t")).ToHaveTextAsync("インストールの開始に失敗しました");
        await Assertions.Expect(_screen.UrlMessage.Locator(".d")).ToHaveTextAsync("12 modules are already being installed; finish or cancel one first");
        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(12);
        await Assertions.Expect(Page.Locator("#maUrl")).ToHaveValueAsync("https://github.com/crowd/p13");

        await Page.Locator("#maUrl").FillAsync("https://github.com/crowd/p5");
        await Page.Locator("#maNext").ClickAsync();
        await Assertions.Expect(_screen.UrlMessage).ToBeHiddenAsync();
        await _screen.AgreeAsync();
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-id", (await _screen.Card("crowd/p5").GetAttributeAsync("data-id"))!);
        await _screen.CloseDialogAsync();

        await looking.Locator(".pl-job-head .pl-job-cancel").ClickAsync();
        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(11);
        await _screen.LookUpAsync("https://github.com/crowd/p13");

        await Assertions.Expect(_screen.Card("crowd/p13")).ToBeVisibleAsync();
        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(12);
        await Assertions.Expect(_screen.DialogTitle).ToHaveTextAsync("crowd/p13");
    }

    /// <summary>押せないボタンのまま残さない。本体の答えを「通らなかった」に差し替えて、画面の側を見る。</summary>
    [Fact]
    public async Task AnAnswerThatDoesNotGoThroughGivesTheButtonBack()
    {
        await AskedToInstallAsync("someone/stubborn", "stubborn");

        await Page.EvaluateAsync("""
            () => {
              window.__e2eAnswer = bridge.answerModuleJob;
              window.__e2eAnswers = 0;
              bridge.answerModuleJob = async () => { window.__e2eAnswers++; return false; };
            }
            """);
        await Dialog.Locator(".pl-job-yes").ClickAsync();

        await Page.WaitForFunctionAsync("() => window.__e2eAnswers === 1");
        await Assertions.Expect(Dialog.Locator(".pl-job-yes")).ToBeEnabledAsync();
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "install");
        await Assertions.Expect(_screen.Row("stubborn")).ToHaveCountAsync(0);

        await Page.EvaluateAsync("() => { bridge.answerModuleJob = window.__e2eAnswer; window.__e2eAnswer = null; }");
        await Dialog.Locator(".pl-job-yes").ClickAsync();

        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(0);
        await Assertions.Expect(_screen.Row("stubborn").Locator(".pl-state")).ToHaveTextAsync("再起動後に有効化");
    }

    /// <summary>配られる順は入れ替わることがある。古い方で上書きすると、消えたはずのカードが戻る・出ているカードが消える・追っているモーダルが閉じる。</summary>
    [Fact]
    public async Task AnOlderSnapshotDoesNotReplaceWhatIsShown()
    {
        await AskedToInstallAsync("someone/steady", "steady");

        // 本体の通し番号は 1 から増える。0 番の「何も無い」は、いま出ているものより古い。
        await Page.EvaluateAsync("() => window.stk.receive('moduleJobs', { revision: 0, dotnetMajor: 10, jobs: [] })");

        await Assertions.Expect(_screen.Card("someone/steady")).ToHaveAttributeAsync("data-asking", "install");
        await Assertions.Expect(_screen.JobsCount).ToHaveTextAsync("残り 1 件");
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "install");
    }

    /// <summary>
    /// 答えたあと、本体が次へ進むまでの間（本体は <c>ask: 0</c> を配る）は短いので、本体の中身を同じ通し番号のまま「答えたあと」にして流す
    /// （あとから来る本物の知らせは、通し番号が大きいので描かれる）。
    /// </summary>
    [Fact]
    public async Task AnAnsweredQuestionCannotBeAnsweredAgainWhileTheAppMovesOn()
    {
        var id = await AskedToInstallAsync("someone/answered", "answered");
        await Assertions.Expect(Dialog.Locator(".pl-job-yes")).ToBeEnabledAsync();

        await Page.EvaluateAsync("""
            async id => {
              const snapshot = await bridge.getModuleJobs();
              snapshot.jobs = snapshot.jobs.map(job => job.id === id ? { ...job, ask: 0, askKind: null } : job);
              window.stk.receive('moduleJobs', snapshot);
            }
            """, id);

        await Assertions.Expect(Dialog.Locator(".pl-job-yes")).ToBeDisabledAsync();
        await Assertions.Expect(Dialog.Locator(".pl-job-cancel")).ToBeEnabledAsync();
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "install");
    }
}
