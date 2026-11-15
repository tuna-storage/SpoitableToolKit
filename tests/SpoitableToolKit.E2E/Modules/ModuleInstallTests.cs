using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Modules;

/// <summary>
/// アプリは <see cref="AppFixture.GitHub"/>（手元の偽物）へ聞き、本物の GitHub へは出ない。インストールするのは本物の小さいモジュール
/// （<c>tests/fixtures/SampleModule</c>）で、起動し直したあと本当に読み込まれるところまで見る。
/// </summary>
public sealed class ModuleInstallTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private const string InstallNote =
        "このモジュールは現在のユーザーの権限で動作します。信頼できる開発元であることを確認しインストールを行ってください。インストールは再起動後に完了となります。";

    private readonly ModuleScreen _screen = new(app);

    private IPage Page => app.Page;

    private ILocator Row(string id) => _screen.Row(id);

    private ILocator Dialog => _screen.Dialog;

    /// <summary>入れかけの置き場（<c>modules/.pending/&lt;Id&gt;</c>。次の起動で入る）。</summary>
    private string Pending => Path.Combine(_screen.Modules, ".pending");

    public async Task InitializeAsync() => await _screen.OpenAsync();

    /// <summary>カードも入れかけも残さず、偽の GitHub の調子も戻す（このクラスのアプリと偽の GitHub は 1 つ）。途中で落ちた試験がそれを残すと、あとの試験が巻き添えになる。</summary>
    public async Task DisposeAsync()
    {
        app.GitHub.Down = false;
        await _screen.ClearJobsAsync();
        if (File.Exists(Pending)) File.Delete(Pending);
        await Page.EvaluateAsync(
            "async () => { for (const id of ['sample', 'later', 'other', 'fixed']) await bridge.cancelPendingModule(id); }");
        await _screen.ShowTabAsync("modules");
        await Assertions.Expect(_screen.RestartBar).ToBeHiddenAsync();
    }

    /// <summary>形だけのモジュール（中身は dll ではない）。インストールの手前まで・入れかけまでを見る試験に使う（読み込ませない）。</summary>
    private static byte[] Shell(string id, string version) => FakeGitHubServer.Zip(
        ("module.json", Encoding.UTF8.GetBytes($"{{ \"id\": \"{id}\", \"name\": \"{id} module\", \"version\": \"{version}\" }}")),
        ($"{id}.dll", "not a dll"u8.ToArray()));

    [Fact]
    public async Task AUrlThatCannotBeInstalledFromSaysWhy()
    {
        var asked = app.GitHub.Requests.Count;
        await _screen.EnterUrlAsync("https://example.com/someone/module");

        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-step", "url");
        await Assertions.Expect(_screen.UrlMessage.Locator(".t")).ToHaveTextAsync("この URL からはインストールできません");
        await Assertions.Expect(_screen.UrlMessage.Locator(".d")).ToHaveTextAsync("only github.com repositories are accepted");
        await Assertions.Expect(Page.Locator("#maUrl")).ToHaveValueAsync("https://example.com/someone/module");
        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(0);
        await Assertions.Expect(_screen.JobsHead).ToBeHiddenAsync();
        Assert.Equal(asked, app.GitHub.Requests.Count);

        // 同じモーダルのまま直す。リリースが無く、ソースも聞けない repo。
        await Page.Locator("#maUrl").FillAsync("https://github.com/someone/fixed");
        await Page.Locator("#maNext").ClickAsync();
        await _screen.AgreeAsync();

        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-state", "failed");
        await Assertions.Expect(_screen.DialogTitle).ToHaveTextAsync("someone/fixed");
        await Assertions.Expect(Dialog.Locator(".pl-job-fail .t")).ToHaveTextAsync("不明な理由によりインストールに失敗しました");
        await Assertions.Expect(Dialog.Locator(".pl-job-fail .reason")).ToContainTextAsync("no published release");
        await Assertions.Expect(Dialog.Locator(".pl-job-retry")).ToHaveTextAsync("リトライ");
        await Assertions.Expect(Dialog.Locator(".pl-job-close")).ToHaveTextAsync("閉じる");
        await Assertions.Expect(_screen.Card("someone/fixed")).ToHaveAttributeAsync("data-state", "failed");
        await Assertions.Expect(_screen.JobsCount).ToHaveTextAsync("残り 1 件");

        app.GitHub.Publish("someone/fixed", "v1.0.0", Shell("fixed", "1.0.0"));
        await Dialog.Locator(".pl-job-retry").ClickAsync();

        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "install");
        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(1);
        await Dialog.Locator(".pl-job-cancel").ClickAsync();
        await Assertions.Expect(Dialog).ToBeHiddenAsync();
        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(0);

        // モジュールの形でない（dll が無い）。
        app.GitHub.Publish("someone/notamodule", "v1", FakeGitHubServer.Zip(
            ("module.json", """{ "id": "notamodule", "name": "x", "version": "1" }"""u8.ToArray()), ("readme.txt", "x"u8.ToArray())));
        await _screen.LookUpAsync("https://github.com/someone/notamodule");

        await Assertions.Expect(Dialog.Locator(".pl-job-fail .t")).ToHaveTextAsync("不明な理由によりインストールに失敗しました");
        await Assertions.Expect(Dialog.Locator(".pl-job-fail .reason")).ToContainTextAsync("notamodule.dll");
        await Assertions.Expect(Dialog.Locator(".pl-job-yes")).ToHaveCountAsync(0);
        await _screen.CloseDialogAsync();

        var shapeless = _screen.Card("someone/notamodule");
        await Assertions.Expect(shapeless.Locator(".pl-job-fail .reason")).ToContainTextAsync("notamodule.dll");
        await shapeless.Locator(".pl-job-close").ClickAsync();
        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(0);

        await _screen.LookUpAsync("https://github.com/someone/notamodule");
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-state", "failed");
        await Dialog.Locator(".pl-job-close").ClickAsync();
        await Assertions.Expect(Dialog).ToBeHiddenAsync();
        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(0);

        await Assertions.Expect(_screen.RestartBar).ToBeHiddenAsync();
        await ModuleScreen.ExpectNoDirectoriesAsync(_screen.Staging);
    }

    /// <summary>本体より新しい SDK で作ったモジュール（<c>module.json</c> の <c>"sdk"</c>）は、インストールしても読み込めないので、聞く前に断る。</summary>
    [Fact]
    public async Task AModuleBuiltForANewerAppIsTurnedDownWithTheReason()
    {
        app.GitHub.Publish("someone/fromthefuture", "v1.0.0", FakeGitHubServer.Zip(
            ("module.json", """{ "id": "fromthefuture", "name": "From the future", "version": "1.0.0", "sdk": "9999.1.0" }"""u8.ToArray()),
            ("fromthefuture.dll", "not a dll"u8.ToArray())));

        await _screen.LookUpAsync("https://github.com/someone/fromthefuture");

        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-state", "failed");
        await Assertions.Expect(Dialog.Locator(".pl-job-fail .t")).ToHaveTextAsync("不明な理由によりインストールに失敗しました");
        await Assertions.Expect(Dialog.Locator(".pl-job-fail .reason")).ToContainTextAsync(
            "The module was built with SpoitableToolKit SDK 9999.1.0, which is newer than this app's");
        await Assertions.Expect(Dialog.Locator(".pl-job-fail .reason")).ToContainTextAsync("Update the app first, then add the module again.");
        await Assertions.Expect(Dialog.Locator(".pl-job-yes")).ToHaveCountAsync(0);
        await Assertions.Expect(_screen.RestartBar).ToBeHiddenAsync();
        await ModuleScreen.ExpectNoDirectoriesAsync(_screen.Staging);
    }

    [Fact]
    public async Task LookingUpShowsWhatWouldBeInstalledAndCancellingLeavesNothing()
    {
        var zip = Shell("other", "0.9.0");
        app.GitHub.Publish("someone/other", "v0.9.0", zip, asset: "other-module.zip");

        await _screen.LookUpAsync("https://github.com/someone/other");

        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "install");
        await Assertions.Expect(_screen.DialogTitle).ToHaveTextAsync("someone/other");
        await Assertions.Expect(Dialog.Locator(".q")).ToHaveTextAsync("other module 0.9.0 をインストールしますか?");
        await Assertions.Expect(Dialog.Locator(".pl-job-facts .k")).ToHaveTextAsync(new[] { "ID", "開発元", "SHA-256", "注意" });
        await Assertions.Expect(ModuleScreen.Fact(Dialog, "ID")).ToHaveTextAsync("other");
        await Assertions.Expect(ModuleScreen.Fact(Dialog, "開発元")).ToHaveTextAsync("someone/other v0.9.0 other-module.zip");
        await Assertions.Expect(ModuleScreen.Fact(Dialog, "SHA-256")).ToHaveTextAsync(Convert.ToHexString(SHA256.HashData(zip)));
        await Assertions.Expect(ModuleScreen.Fact(Dialog, "注意")).ToHaveTextAsync(InstallNote);
        await Assertions.Expect(Dialog.Locator(".pl-job-yes")).ToHaveTextAsync("インストール");
        await Assertions.Expect(Dialog.Locator(".pl-job-yes")).ToBeEnabledAsync();
        await Assertions.Expect(Dialog.Locator(".pl-job-cancel")).ToHaveTextAsync("キャンセル");

        var card = _screen.Card("someone/other");
        await Assertions.Expect(card).ToHaveAttributeAsync("data-asking", "install");
        await Assertions.Expect(card.Locator(".pl-job-status .what")).ToHaveTextAsync("確認待ち");
        await Assertions.Expect(card.Locator(".pl-job-status .detail")).ToHaveTextAsync("インストール");
        await Assertions.Expect(card.Locator(".pl-job-review")).ToHaveTextAsync("確認");
        await Assertions.Expect(card.Locator(".pl-job-facts")).ToHaveCountAsync(0);
        await Assertions.Expect(card.Locator(".pl-job-yes")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("#plJobsHead h2")).ToHaveTextAsync("インストール中:");
        await Assertions.Expect(_screen.JobsCount).ToHaveTextAsync("残り 1 件");
        await Assertions.Expect(Row("other")).ToHaveCountAsync(0);
        await Assertions.Expect(_screen.RestartBar).ToBeHiddenAsync();

        await Dialog.Locator(".pl-job-cancel").ClickAsync();

        await Assertions.Expect(Dialog).ToBeHiddenAsync();
        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(0);
        await Assertions.Expect(_screen.JobsHead).ToBeHiddenAsync();
        await Assertions.Expect(Row("other")).ToHaveCountAsync(0);
        await ModuleScreen.ExpectNoDirectoriesAsync(_screen.Staging);
        Assert.False(Directory.Exists(Path.Combine(Pending, "other")));
    }

    [Fact]
    public async Task AConfirmedInstallWaitsForTheNextStartAndCanBeTakenBack()
    {
        app.GitHub.Publish("someone/later", "v3.0.0", Shell("later", "3.0.0"));
        await _screen.LookUpAsync("https://github.com/someone/later");
        await Dialog.Locator(".pl-job-yes").ClickAsync();

        await Assertions.Expect(Dialog).ToBeHiddenAsync();
        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(0);
        await Assertions.Expect(Row("later").Locator(".pl-state")).ToHaveTextAsync("再起動後に有効化");
        await Assertions.Expect(Row("later").Locator(".pl-source")).ToHaveTextAsync("開発元: someone/later v3.0.0");
        await Assertions.Expect(Row("later").Locator(".pl-title .tag")).ToHaveCountAsync(0);
        await Assertions.Expect(_screen.RestartBar).ToBeVisibleAsync();
        Assert.True(File.Exists(Path.Combine(Pending, "later", "module.json")));
        Assert.False(Directory.Exists(Path.Combine(_screen.Modules, "later")), "次の起動より前に入った");

        await _screen.OpenMenuAsync(Row("later"));
        var items = await _screen.MenuItemsAsync();
        Assert.Equal(["cancelPending"], items);
        await Assertions.Expect(_screen.Menu.Locator("button[data-id='cancelPending']")).ToHaveTextAsync("インストールをキャンセル");
        await _screen.Menu.Locator("button[data-id='cancelPending']").ClickAsync();

        await Assertions.Expect(Row("later")).ToHaveCountAsync(0);
        await Assertions.Expect(_screen.RestartBar).ToBeHiddenAsync();
        Assert.False(Directory.Exists(Path.Combine(Pending, "later")));
    }

    /// <summary>入れかけの置き場（<c>.pending</c>）と同じ名前のファイルを置いて、本物の「書けない」を起こす。</summary>
    [Fact]
    public async Task AnInstallThatCouldNotBeQueuedSaysSoAndKeepsAsking()
    {
        app.GitHub.Publish("someone/other", "v0.9.0", Shell("other", "0.9.0"));
        await _screen.LookUpAsync("https://github.com/someone/other");
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "install");
        await Assertions.Expect(Dialog.Locator(".pl-job-note")).ToHaveCountAsync(0);

        // 先の試験が空の置き場を残していれば、どける（空でなければ、ここで落ちる = 先の試験が入れかけを残した）。
        if (Directory.Exists(Pending)) Directory.Delete(Pending);
        File.WriteAllText(Pending, "in the way");
        try
        {
            await Dialog.Locator(".pl-job-yes").ClickAsync();

            await Assertions.Expect(Dialog.Locator(".pl-job-note")).ToHaveTextAsync(
                "次の起動キューの追加に失敗しました。もう一度押すか、アプリケーションのログを確認してください");
            await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "install");
            await Assertions.Expect(Dialog.Locator(".q")).ToHaveTextAsync("other module 0.9.0 をインストールしますか?");
            await Assertions.Expect(Dialog.Locator(".pl-job-yes")).ToBeEnabledAsync();
            await Assertions.Expect(Row("other")).ToHaveCountAsync(0);
            await Assertions.Expect(_screen.RestartBar).ToBeHiddenAsync();
        }
        finally
        {
            File.Delete(Pending);
        }

        await Dialog.Locator(".pl-job-yes").ClickAsync();

        await Assertions.Expect(Dialog).ToBeHiddenAsync();
        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(0);
        await Assertions.Expect(Row("other").Locator(".pl-state")).ToHaveTextAsync("再起動後に有効化");
        Assert.True(File.Exists(Path.Combine(Pending, "other", "module.json")));
    }

    [Fact]
    public async Task AnInstalledModuleLoadsAfterARestartAndCanBeUpdated()
    {
        app.GitHub.Publish("someone/sample", "v1.2.3", AppFixture.SampleModuleZip("1.2.3"));
        await _screen.LookUpAsync("https://github.com/someone/sample");
        await Assertions.Expect(Dialog.Locator(".q")).ToHaveTextAsync("Sample 1.2.3 をインストールしますか?");
        await Assertions.Expect(Dialog.Locator(".pl-job-facts .k")).ToHaveTextAsync(new[] { "ID", "開発元", "SHA-256", "注意" });
        await Dialog.Locator(".pl-job-yes").ClickAsync();
        await Assertions.Expect(Row("sample").Locator(".pl-state")).ToHaveTextAsync("再起動後に有効化");

        await app.RestartAsync();
        await _screen.OpenAsync();

        await Assertions.Expect(Row("sample").Locator(".pl-state")).ToHaveTextAsync("動作中");
        await Assertions.Expect(Row("sample").Locator(".pl-title .tag")).ToHaveCountAsync(0);
        await Assertions.Expect(Row("sample").Locator(".pl-title .ver")).ToHaveTextAsync("1.2.3");
        await Assertions.Expect(Row("sample").Locator(".pl-source")).ToHaveTextAsync("開発元: someone/sample v1.2.3");
        await Assertions.Expect(Row("sample").Locator(".pl-update")).ToHaveCountAsync(0);
        await Assertions.Expect(_screen.RestartBar).ToBeHiddenAsync();
        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(0);
        Assert.False(Directory.Exists(Path.Combine(Pending, "sample")));

        // 更新のタブ。確かめられなかった（GitHub から答えが返らない）ときは、そう言う。「更新はありません」とは言わない。
        await _screen.ShowTabAsync("updates");
        await Assertions.Expect(Page.Locator("#plUpdates")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#plList")).ToBeHiddenAsync();
        await Assertions.Expect(_screen.UpdatesTab).ToHaveTextAsync("更新");
        app.GitHub.Down = true;
        await Page.Locator("#plCheckBtn").ClickAsync();
        await Assertions.Expect(_screen.CheckMessage).ToHaveTextAsync("更新のチェックに失敗しました (GitHubからレスポンスを受け取れませんでした)");
        app.GitHub.Down = false;
        await Page.Locator("#plCheckBtn").ClickAsync();
        await Assertions.Expect(_screen.CheckMessage).ToBeHiddenAsync();
        await Assertions.Expect(_screen.CheckedAt).ToHaveTextAsync(new Regex(@"^最終確認: \d{4}/\d\d/\d\d \d\d:\d\d:\d\d$"));
        await Assertions.Expect(Page.Locator("#plUpdateList")).ToHaveTextAsync("更新はありません");

        app.GitHub.Publish("someone/sample", "v2.0.0", AppFixture.SampleModuleZip("2.0.0"));
        await Page.Locator("#plCheckBtn").ClickAsync();

        var update = _screen.UpdateRow("sample");
        await Assertions.Expect(update.Locator(".pl-update")).ToHaveTextAsync("更新があります: v2.0.0");
        await Assertions.Expect(_screen.UpdatesTab).ToHaveTextAsync("更新 (1)");
        await Assertions.Expect(Row("sample").Locator(".pl-update")).ToHaveTextAsync("更新があります: v2.0.0");
        await Assertions.Expect(Page.Locator("#sidebar .item[data-id='moduleManager']")).ToHaveClassAsync(new Regex(@"\bhas-update\b"));
        await Assertions.Expect(_screen.RestartBar).ToBeHiddenAsync();

        await update.Locator(".pl-update-btn").ClickAsync();
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-step", "job");
        await Assertions.Expect(Dialog.Locator(".q")).ToHaveTextAsync("Sample 2.0.0 をインストールしますか?");
        await Assertions.Expect(Dialog.Locator(".pl-job-facts .k")).ToHaveTextAsync(new[] { "ID", "開発元", "SHA-256", "上書き", "注意" });
        await Assertions.Expect(ModuleScreen.Fact(Dialog, "開発元")).ToHaveTextAsync("someone/sample v2.0.0 module.zip");
        await Assertions.Expect(ModuleScreen.Fact(Dialog, "上書き")).ToHaveTextAsync("既存の 1.2.3 が置き換えられます");
        await Dialog.Locator(".pl-job-yes").ClickAsync();

        await Assertions.Expect(Dialog).ToBeHiddenAsync();
        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("#plUpdateList")).ToHaveTextAsync("更新はありません");
        await Assertions.Expect(_screen.UpdatesTab).ToHaveTextAsync("更新");
        await _screen.ShowTabAsync("modules");
        await Assertions.Expect(Row("sample").Locator(".pl-next")).ToHaveTextAsync("次の起動で 2.0.0 に更新されます");
        await Assertions.Expect(Row("sample").Locator(".pl-source")).ToHaveTextAsync("開発元: someone/sample v2.0.0");
        await Assertions.Expect(Row("sample").Locator(".pl-state")).ToHaveTextAsync("動作中");
        await Assertions.Expect(Row("sample").Locator(".pl-update")).ToHaveCountAsync(0);
        await Assertions.Expect(_screen.RestartBar).ToBeVisibleAsync();
        await _screen.OpenMenuAsync(Row("sample"));
        await Assertions.Expect(_screen.Menu.Locator("button[data-id='cancelPending']")).ToHaveTextAsync("インストールをキャンセル");
        await _screen.DismissMenuAsync();

        await app.RestartAsync();
        await _screen.OpenAsync();

        await Assertions.Expect(Row("sample").Locator(".pl-state")).ToHaveTextAsync("動作中");
        await Assertions.Expect(Row("sample").Locator(".pl-source")).ToHaveTextAsync("開発元: someone/sample v2.0.0");
        await Assertions.Expect(Row("sample").Locator(".pl-update")).ToHaveCountAsync(0);
        await Assertions.Expect(Row("sample").Locator(".pl-next")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("#sidebar .item[data-id='moduleManager']")).Not.ToHaveClassAsync(new Regex(@"\bhas-update\b"));
        await Assertions.Expect(_screen.RestartBar).ToBeHiddenAsync();
        await ModuleScreen.ExpectNoDirectoriesAsync(Path.Combine(_screen.Modules, ".trash"));
    }
}
