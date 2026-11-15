using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Modules;

/// <summary>
/// 本物の GitHub へも nuget.org へも出ない。ソースは <see cref="AppFixture.GitHub"/>（手元の偽物）から届き、SDK のパッケージはアプリに同梱のもの（<c>sdk/</c>）だけ。
/// 本物の <c>dotnet build</c> が走る（見本のモジュール 1 つで数秒。Build まで通すのは 1 本だけにしてある）。
/// </summary>
public sealed class ModuleBuildTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    private const string BuildReason = "このリポジトリにはリリースが存在しないため、使用するにはソースコードからBuildを行う必要があります。";

    private const string BuildNote =
        "Buildの際に不審なコードが実行される場合があります。信頼できる開発元のリポジトリであることを確認し、Buildするを押下してください。";

    private const string InstallNote =
        "このモジュールは現在のユーザーの権限で動作します。信頼できる開発元であることを確認しインストールを行ってください。インストールは再起動後に完了となります。";

    private readonly ModuleScreen _screen = new(app);

    private IPage Page => app.Page;

    private ILocator Row(string id) => _screen.Row(id);

    private ILocator Dialog => _screen.Dialog;

    public async Task InitializeAsync() => await _screen.OpenAsync();

    /// <summary>カードも入れかけも残さない（このクラスのアプリは 1 つ。途中で落ちた試験が残すと、あとの試験が巻き添えになる）。</summary>
    public async Task DisposeAsync()
    {
        await _screen.ClearJobsAsync();
        await Page.EvaluateAsync("async () => { await bridge.cancelPendingModule('mymodule'); }");
        await _screen.ShowTabAsync("modules");
        await Assertions.Expect(_screen.RestartBar).ToBeHiddenAsync();
    }

    [Fact]
    public async Task ARepositoryWithoutAReleaseIsBuiltHereAfterAskingAndLoadsAfterARestart()
    {
        var zipball = $"/repos/someone/mymodule/zipball/{Commit}";
        app.GitHub.PublishSource("someone/mymodule", Commit, AppFixture.SdkSampleSourceZip("MyModule", "someone-mymodule-0123456"));

        await _screen.LookUpAsync("https://github.com/someone/mymodule");

        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "build");
        await Assertions.Expect(_screen.DialogTitle).ToHaveTextAsync("someone/mymodule");
        // 何を Build するかは、下に 40 桁で出す（頭には出さない）。
        await Assertions.Expect(_screen.DialogCommit).ToBeHiddenAsync();
        await Assertions.Expect(Dialog.Locator(".q")).ToHaveTextAsync("ソースからBuildしインストールしますか?");
        await Assertions.Expect(Dialog.Locator(".pl-job-facts .k")).ToHaveTextAsync(new[] { "理由", "Build対象", "注意" });
        await Assertions.Expect(ModuleScreen.Fact(Dialog, "理由")).ToHaveTextAsync(BuildReason);
        await Assertions.Expect(ModuleScreen.Fact(Dialog, "Build対象")).ToHaveTextAsync($"commit {Commit}");
        await Assertions.Expect(ModuleScreen.Fact(Dialog, "注意")).ToHaveTextAsync(BuildNote);
        await Assertions.Expect(Dialog.Locator(".pl-job-yes")).ToHaveTextAsync("Buildする");
        await Assertions.Expect(Dialog.Locator(".pl-job-cancel")).ToHaveTextAsync("キャンセル");
        await Assertions.Expect(_screen.Card("someone/mymodule").Locator(".pl-job-status .detail")).ToHaveTextAsync("ソースからのBuild");
        Assert.Equal(0, _screen.Asked(zipball));
        await ModuleScreen.ExpectNoDirectoriesAsync(_screen.BuildFolder);

        var card = _screen.Card("someone/mymodule");
        using (app.GitHub.Hold("/repos/someone/mymodule/zipball/"))
        {
            await Dialog.Locator(".pl-job-yes").ClickAsync();
            await _screen.WaitUntilAskedAsync(zipball);

            await Assertions.Expect(Dialog).ToBeHiddenAsync();
            await Assertions.Expect(card).ToHaveAttributeAsync("data-state", "building");
            await Assertions.Expect(card.Locator(".pl-job-head .commit")).ToHaveTextAsync("commit: 0123456");
            await Assertions.Expect(card.Locator(".pl-job-head .pl-job-cancel")).ToHaveTextAsync("キャンセル");
            await Assertions.Expect(card.Locator(".pl-job-status .what")).ToHaveTextAsync("Build中...");
            await Assertions.Expect(card.Locator(".pl-job-status .detail")).ToHaveTextAsync("downloading source...");
            await Assertions.Expect(card.Locator(".pl-job-status .time")).ToHaveTextAsync(new Regex(@"^\d+:\d\d$"));
            // .NET SDK はこの PC にあるので、手順（1/2）は出ない。
            await Assertions.Expect(card.Locator(".pl-job-status .step")).ToHaveCountAsync(0);
            await Assertions.Expect(card.Locator(".pl-job-bar")).ToHaveAttributeAsync("data-kind", "flow");
            await Assertions.Expect(card.Locator(".pl-job-yes")).ToHaveCountAsync(0);
            await Assertions.Expect(Page.Locator("#plAddBtn")).ToBeEnabledAsync();
        }

        // 「確認する」で、リリースからのときと同じ中身（開発元だけが違う。SHA-256 の代わりに commit）。
        await Assertions.Expect(card).ToHaveAttributeAsync("data-asking", "install", new() { Timeout = 180_000 });
        await Assertions.Expect(card.Locator(".pl-job-status .what")).ToHaveTextAsync("確認待ち");
        await Assertions.Expect(card.Locator(".pl-job-status .detail")).ToHaveTextAsync("インストール");
        await Assertions.Expect(card.Locator(".pl-job-head .commit")).ToHaveTextAsync("commit: 0123456");
        await Assertions.Expect(Dialog).ToBeHiddenAsync();
        await card.Locator(".pl-job-review").ClickAsync();

        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "install");
        await Assertions.Expect(Dialog.Locator(".q")).ToHaveTextAsync(new Regex(@" 0\.3\.0 をインストールしますか\?$"));
        await Assertions.Expect(Dialog.Locator(".pl-job-facts .k")).ToHaveTextAsync(new[] { "ID", "開発元", "commit", "注意" });
        await Assertions.Expect(ModuleScreen.Fact(Dialog, "ID")).ToHaveTextAsync("mymodule");
        await Assertions.Expect(ModuleScreen.Fact(Dialog, "開発元")).ToHaveTextAsync("someone/mymodule (このPC上でBuild)");
        await Assertions.Expect(ModuleScreen.Fact(Dialog, "commit")).ToHaveTextAsync(Commit);
        await Assertions.Expect(ModuleScreen.Fact(Dialog, "注意")).ToHaveTextAsync(InstallNote);
        await Assertions.Expect(Dialog.Locator(".pl-job-yes")).ToHaveTextAsync("インストール");
        await Assertions.Expect(_screen.DialogCommit).ToBeHiddenAsync();
        await Assertions.Expect(Row("mymodule")).ToHaveCountAsync(0);
        Assert.Equal(1, _screen.Asked(zipball));

        // 作業場は残さない（パッケージの展開先も、この中）。
        await ModuleScreen.ExpectNoDirectoriesAsync(_screen.BuildFolder);

        await Dialog.Locator(".pl-job-yes").ClickAsync();

        await Assertions.Expect(Dialog).ToBeHiddenAsync();
        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(0);
        await Assertions.Expect(Row("mymodule").Locator(".pl-state")).ToHaveTextAsync("再起動後に有効化");
        await Assertions.Expect(Row("mymodule").Locator(".pl-source")).ToHaveTextAsync("開発元: someone/mymodule commit: 0123456 (このPC上でBuild)");
        await Assertions.Expect(_screen.RestartBar).ToBeVisibleAsync();
        Assert.True(File.Exists(Path.Combine(_screen.Modules, ".pending", "mymodule", "module.json")));

        await app.RestartAsync();
        await _screen.OpenAsync();

        await Assertions.Expect(Row("mymodule").Locator(".pl-state")).ToHaveTextAsync("動作中");
        await Assertions.Expect(Row("mymodule").Locator(".pl-title .ver")).ToHaveTextAsync("0.3.0");
        await Assertions.Expect(Row("mymodule").Locator(".pl-source")).ToHaveTextAsync("開発元: someone/mymodule commit: 0123456 (このPC上でBuild)");
        await Assertions.Expect(_screen.RestartBar).ToBeHiddenAsync();

        // Build したものは、その repo の先頭の commit と比べる（リリースは聞かない）。回数は押す前後の差で見る（起動の 10 秒後に、自分からも確かめる）。
        const string newer = "89abcdef0123456789abcdef0123456789abcdef";
        var releasesAsked = _screen.Asked("/repos/someone/mymodule/releases/latest");
        var headsAsked = _screen.Asked("/repos/someone/mymodule/commits/HEAD");
        await _screen.ShowTabAsync("updates");
        await Page.Locator("#plCheckBtn").ClickAsync();
        await Assertions.Expect(_screen.CheckMessage).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator("#plUpdateList")).ToHaveTextAsync("更新はありません");
        await Assertions.Expect(_screen.UpdateRow("mymodule")).ToHaveCountAsync(0);
        Assert.Equal(releasesAsked, _screen.Asked("/repos/someone/mymodule/releases/latest"));
        Assert.True(_screen.Asked("/repos/someone/mymodule/commits/HEAD") > headsAsked, "先頭の commit を聞かなかった");

        // 確かめるだけで、ソースは落とさない。
        app.GitHub.PublishSource("someone/mymodule", newer, AppFixture.SdkSampleSourceZip("MyModule", "someone-mymodule-89abcde"));
        await Page.Locator("#plCheckBtn").ClickAsync();
        await Assertions.Expect(_screen.UpdateRow("mymodule").Locator(".pl-update")).ToHaveTextAsync("更新があります: commit 89abcde");
        Assert.Equal(releasesAsked, _screen.Asked("/repos/someone/mymodule/releases/latest"));
        Assert.Equal(0, _screen.Asked($"/repos/someone/mymodule/zipball/{newer}"));

        // 「更新」では同意を聞かない。
        await _screen.UpdateRow("mymodule").Locator(".pl-update-btn").ClickAsync();
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-step", "job");
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "build");
        await Assertions.Expect(ModuleScreen.Fact(Dialog, "Build対象")).ToContainTextAsync(newer);
        await Dialog.Locator(".pl-job-yes").ClickAsync();
        var update = _screen.Card("someone/mymodule");
        await Assertions.Expect(update).ToHaveAttributeAsync("data-asking", "install", new() { Timeout = 180_000 });
        await update.Locator(".pl-job-review").ClickAsync();
        await Dialog.Locator(".pl-job-yes").ClickAsync();

        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(0);
        await _screen.ShowTabAsync("modules");
        await Assertions.Expect(Row("mymodule").Locator(".pl-next")).ToHaveTextAsync("次の起動で 0.3.0 (commit 89abcde) に更新されます");
        await Assertions.Expect(Row("mymodule").Locator(".pl-source")).ToHaveTextAsync("開発元: someone/mymodule commit: 89abcde (このPC上でBuild)");
        await Assertions.Expect(Row("mymodule").Locator(".pl-update")).ToHaveCountAsync(0);
        Assert.Equal(1, _screen.Asked($"/repos/someone/mymodule/zipball/{newer}"));
    }

    [Fact]
    public async Task CancellingAtTheBuildQuestionDownloadsNothing()
    {
        const string commit = "89abcdef0123456789abcdef0123456789abcdef";
        app.GitHub.PublishSource("someone/declined", commit, FakeGitHubServer.Zip(("someone-declined-89abcde/README.md", "# x"u8.ToArray())));

        await _screen.LookUpAsync("https://github.com/someone/declined");
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "build");
        await Assertions.Expect(ModuleScreen.Fact(Dialog, "Build対象")).ToHaveTextAsync($"commit {commit}");

        await Dialog.Locator(".pl-job-cancel").ClickAsync();

        await Assertions.Expect(Dialog).ToBeHiddenAsync();
        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(0);
        await Assertions.Expect(_screen.JobsHead).ToBeHiddenAsync();
        Assert.Equal(0, _screen.Asked($"/repos/someone/declined/zipball/{commit}"));
        Assert.False(Directory.Exists(Path.Combine(_screen.Modules, ".pending", "declined")));
        await ModuleScreen.ExpectNoDirectoriesAsync(_screen.BuildFolder);
        await ModuleScreen.ExpectNoDirectoriesAsync(_screen.Staging);
    }

    [Fact]
    public async Task ARepositoryWithNothingToBuildSaysWhyAndRetryingAsksAgain()
    {
        const string commit = "fedcba9876543210fedcba9876543210fedcba98";
        var zipball = $"/repos/someone/docs-only/zipball/{commit}";
        app.GitHub.PublishSource("someone/docs-only", commit, FakeGitHubServer.Zip(("someone-docs-only-fedcba9/README.md", "# nothing to build"u8.ToArray())));

        await _screen.LookUpAsync("https://github.com/someone/docs-only");
        await Dialog.Locator(".pl-job-yes").ClickAsync();

        var card = _screen.Card("someone/docs-only");
        await Assertions.Expect(card).ToHaveAttributeAsync("data-state", "failed", new() { Timeout = 60_000 });
        await Assertions.Expect(Dialog).ToBeHiddenAsync();
        await Assertions.Expect(card.Locator(".pl-job-fail .t")).ToHaveTextAsync("Buildに失敗しました");
        await Assertions.Expect(card.Locator(".pl-job-fail .reason")).ToContainTextAsync(
            "someone/docs-only at fedcba9 could not be built: no module project was found");
        await Assertions.Expect(card.Locator(".pl-job-head .commit")).ToHaveTextAsync("commit: fedcba9");
        await Assertions.Expect(card.Locator(".pl-job-retry")).ToHaveTextAsync("リトライ");
        await Assertions.Expect(card.Locator(".pl-job-yes")).ToHaveCountAsync(0);
        Assert.Equal(1, _screen.Asked(zipball));
        await ModuleScreen.ExpectNoDirectoriesAsync(_screen.BuildFolder);
        await ModuleScreen.ExpectNoDirectoriesAsync(_screen.Staging);

        await card.Locator(".pl-job-retry").ClickAsync();

        await Assertions.Expect(card).ToHaveAttributeAsync("data-asking", "build");
        await Assertions.Expect(card.Locator(".pl-job-review")).ToBeEnabledAsync();
        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(1);
        Assert.Equal(2, _screen.Asked("/repos/someone/docs-only/commits/HEAD"));
        Assert.Equal(1, _screen.Asked(zipball));
        await card.Locator(".pl-job-review").ClickAsync();
        await Assertions.Expect(Dialog.Locator(".pl-job-yes")).ToBeEnabledAsync();
    }
}
