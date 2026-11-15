using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Modules;

/// <summary>
/// このクラスのアプリは、<c>SPOITABLE_DOTNET</c> で無い <c>dotnet.exe</c> を指して「SDK が無い PC」にしてある。.NET の配布元は、どのクラスのアプリも
/// <see cref="AppFixture.GitHub"/>（手元の偽物）へ向く（<c>SPOITABLE_DOTNET_RELEASES</c>）。本物の配布元へは出ず、本物の SDK（290 MB）はダウンロードしない。
/// 偽の zip は SDK ではないので、ダウンロードしたあとの「確かめる」で断られる所までを本物で通す
/// （SDK が入ったあと Build に進む所は、台帳の unit: <c>ModuleInstallJobsTests.WithoutADotnetSdkItAsksThenDownloadsThenBuilds</c>）。
/// </summary>
public sealed class ModuleBuildToolsTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string SdkZip = "/dotnet/Sdk/10.0.404/dotnet-sdk-10.0.404-win-x64.zip";
    private const string Zipball = $"/repos/someone/needs-sdk/zipball/{Commit}";

    /// <summary>偽の SDK: 展開はできるが、dotnet.exe は動かない（SDK を持つと答えない）。大きさは 3 MB 強（縮まない中身）。</summary>
    private static readonly byte[] FakeSdk = MakeFakeSdk();

    private static byte[] MakeFakeSdk()
    {
        var filler = new byte[3 * 1024 * 1024];
        new Random(1).NextBytes(filler);
        return FakeGitHubServer.Zip(("dotnet.exe", "not a real dotnet"u8.ToArray()), ("sdk/10.0.404/dotnet.dll", filler));
    }

    private readonly ModuleScreen _screen = new(app);

    private IPage Page => app.Page;

    private ILocator Card => _screen.Card("someone/needs-sdk");

    private ILocator Dialog => _screen.Dialog;

    private string Tools => Path.Combine(app.DataDirectory, "tools");

    public async Task InitializeAsync()
    {
        // 「SDK が無い PC」にして起動し直す（このクラスで 1 回だけ）。
        if (!app.ExtraEnvironment.ContainsKey("SPOITABLE_DOTNET"))
        {
            app.ExtraEnvironment["SPOITABLE_DOTNET"] = Path.Combine(app.DataDirectory, "no-dotnet-here", "dotnet.exe");
            await app.RestartAsync();
        }

        app.GitHub.PublishDotnetSdk("10.0.404", FakeSdk);
        app.GitHub.PublishSource("someone/needs-sdk", Commit, FakeGitHubServer.Zip(("someone-needs-sdk-0123456/README.md", "# x"u8.ToArray())));

        await _screen.OpenAsync();
    }

    /// <summary>カードを残さない（このクラスのアプリは 1 つ）。</summary>
    public async Task DisposeAsync() => await _screen.ClearJobsAsync();

    /// <summary>
    /// 閉じるときは片付けを待たないので、ダウンロード中に閉じた残り（落としかけ・展開しかけ）は次の起動で消す。
    /// 次にダウンロードするまで待つと、二度とダウンロードしない PC ではずっと残る。
    /// </summary>
    [Fact]
    public async Task WhatAnEarlierDownloadLeftBehindIsClearedAtTheNextStart()
    {
        var part = Path.Combine(Tools, "dotnet-sdk.zip.part");
        var fresh = Path.Combine(Tools, "dotnet.new");
        Directory.CreateDirectory(Path.Combine(fresh, "sdk"));
        File.WriteAllText(Path.Combine(fresh, "sdk", "left.txt"), "x");
        File.WriteAllText(part, "half");

        await app.RestartAsync();

        for (var attempt = 0; attempt < 100 && (File.Exists(part) || Directory.Exists(fresh)); attempt++) await Task.Delay(100);
        Assert.False(File.Exists(part), "落としかけの zip が残った");
        Assert.False(Directory.Exists(fresh), "展開しかけが残った");
        await _screen.OpenAsync();
    }

    private async Task AskToBuildAsync()
    {
        await _screen.LookUpAsync("https://github.com/someone/needs-sdk");
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "build");
        await Dialog.Locator(".pl-job-yes").ClickAsync();
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-asking", "sdk", new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task WithoutADotnetSdkTheDialogAsksBeforeDownloadingOne()
    {
        var downloads = _screen.Asked(SdkZip);
        await AskToBuildAsync();

        await Assertions.Expect(Dialog.Locator(".q")).ToHaveTextAsync(
            ".NET SDK 10 以上がこのPCにインストールされていません、ダウンロードしますか? (推定: 3.0MB)");
        await Assertions.Expect(Dialog.Locator(".pl-job-facts .k")).ToHaveTextAsync(new[] { "ダウンロード元", "詳細" });
        await Assertions.Expect(ModuleScreen.Fact(Dialog, "ダウンロード元")).ToHaveTextAsync("Microsoft（.NET SDK 10.0.404）");
        await Assertions.Expect(ModuleScreen.Fact(Dialog, "詳細")).ToHaveTextAsync(
            "このアプリのツールとしてダウンロードされて動作し、PC全体への影響はありません。");
        await Assertions.Expect(Dialog.Locator(".pl-job-yes")).ToHaveTextAsync("ダウンロード");
        await Assertions.Expect(Dialog.Locator(".pl-job-yes")).ToBeEnabledAsync();
        await Assertions.Expect(Dialog.Locator(".pl-job-cancel")).ToHaveTextAsync("キャンセル");
        // 何を Build するかは、頭に出す。
        await Assertions.Expect(_screen.DialogCommit).ToHaveTextAsync("commit: 0123456");
        await Assertions.Expect(Card.Locator(".pl-job-status .detail")).ToHaveTextAsync(".NET SDK のダウンロード");
        Assert.Equal(downloads, _screen.Asked(SdkZip));
        Assert.True(_screen.Asked("HEAD " + SdkZip) > 0, "大きさを聞いていない");
        Assert.Equal(0, _screen.Asked(Zipball));
        Assert.False(Directory.Exists(Path.Combine(Tools, "dotnet")));

        await Dialog.Locator(".pl-job-cancel").ClickAsync();

        await Assertions.Expect(Dialog).ToBeHiddenAsync();
        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(0);
        Assert.Equal(downloads, _screen.Asked(SdkZip));
        Assert.Equal(0, _screen.Asked(Zipball));
        Assert.False(Directory.Exists(Path.Combine(Tools, "dotnet")));
        await ModuleScreen.ExpectNoDirectoriesAsync(_screen.BuildFolder);
    }

    [Fact]
    public async Task ADownloadThatIsNotAnSdkIsNotInstalledAndTheCardSaysWhy()
    {
        await AskToBuildAsync();
        var before = _screen.Asked(SdkZip);

        await Dialog.Locator(".pl-job-yes").ClickAsync();

        await Assertions.Expect(Card).ToHaveAttributeAsync("data-state", "failed", new() { Timeout = 60_000 });
        await Assertions.Expect(Dialog).ToBeHiddenAsync();
        await Assertions.Expect(Card.Locator(".pl-job-fail .t")).ToHaveTextAsync(".NET SDK のインストールに失敗しました");
        await Assertions.Expect(Card.Locator(".pl-job-fail .reason")).ToContainTextAsync(
            "The .NET SDK 10.0.404 was downloaded and unpacked, but it does not list an SDK that can build modules; nothing was installed");
        await Assertions.Expect(Card.Locator(".pl-job-yes")).ToHaveCountAsync(0);
        Assert.Equal(before + 1, _screen.Asked(SdkZip));
        Assert.Equal(0, _screen.Asked(Zipball));
        Assert.False(Directory.Exists(Path.Combine(Tools, "dotnet")));
        Assert.False(Directory.Exists(Path.Combine(Tools, "dotnet.new")));
        Assert.False(File.Exists(Path.Combine(Tools, "dotnet-sdk.zip.part")));

        await Card.Locator(".pl-job-retry").ClickAsync();

        await Assertions.Expect(Card).ToHaveAttributeAsync("data-asking", "build");
        Assert.Equal(before + 1, _screen.Asked(SdkZip));
        Assert.Equal(0, _screen.Asked(Zipball));
    }

    /// <summary>
    /// 量が進んでも、カードは作り直さない（作り直すと、押している途中の「キャンセル」が差し替わって押下が落ちる）。
    /// 偽の配布元が zip を半分で止めるので、本体から配られた本物の進みで見る。
    /// </summary>
    [Fact]
    public async Task WhileTheSdkDownloadsTheCardShowsHowFarItIsAndCanBeCancelled()
    {
        await AskToBuildAsync();

        using var held = app.GitHub.Hold("/dotnet/Sdk/", after: FakeSdk.Length / 2);

        // カードが「ダウンロード中」になった最初の node に印を付ける（まだ量が届く前。そのとき出ていた量も控える）。
        // 「ダウンロード中」になってから印を付けると、1 MB の進みがもう届いていることがあり、そのあと量が変わらないので、作り直しても気付けない。
        await Page.EvaluateAsync("""
            () => {
              window.__e2eFirstAmount = null;
              const observer = new MutationObserver(() => {
                const node = document.querySelector("#plJobs .pl-job[data-state='sdkDownloading']");
                if (!node) return;
                node.dataset.e2eSame = 'yes';
                window.__e2eFirstAmount = node.querySelector('.pl-job-status .amount')?.textContent ?? '';
                observer.disconnect();
              });
              observer.observe(document.getElementById('plJobs'), { childList: true, subtree: true });
            }
            """);
        await Dialog.Locator(".pl-job-yes").ClickAsync();

        await Assertions.Expect(Card).ToHaveAttributeAsync("data-state", "sdkDownloading");
        await Assertions.Expect(Dialog).ToBeHiddenAsync();
        await Assertions.Expect(Card.Locator(".pl-job-status .step")).ToHaveTextAsync("1/2");
        await Assertions.Expect(Card.Locator(".pl-job-status .what")).ToHaveTextAsync(".NET SDK 10.0.404 をダウンロード中");
        await Assertions.Expect(Card.Locator(".pl-job-head .commit")).ToHaveTextAsync("commit: 0123456");
        await Assertions.Expect(Card.Locator(".pl-job-head .pl-job-cancel")).ToBeEnabledAsync();

        // 本体が 1 MB 以上を受け取るまで待つ（進みを知らせるのは、およそ 1 MB ごと）。
        await _screen.WaitForLedgerAsync("job => job.state === 'sdkDownloading' && job.bytes >= 1048576");

        // 画面を開き直すと、本体のいまの量で描き直る（量は 100 ms に 1 回しか配られないので、止まった所の量は、配られていないことがある）。
        await Page.EvaluateAsync("() => selectView('log')");
        await _screen.OpenAsync();

        await Assertions.Expect(Card.Locator(".pl-job-status .amount")).ToHaveTextAsync(new Regex(@"^1\.\d/3\.0MB$"));
        await Assertions.Expect(Card.Locator(".pl-job-bar")).ToHaveAttributeAsync("data-kind", "part");
        var width = await Card.Locator(".pl-job-bar i").EvaluateAsync<string>("node => node.style.width");
        Assert.Matches(@"^(3[3-9]|4\d|50)\.\d%$", width);
        await Assertions.Expect(Card).ToHaveAttributeAsync("data-e2e-same", "yes");
        var first = await Page.EvaluateAsync<string>("() => window.__e2eFirstAmount");
        Assert.NotEqual(await Card.Locator(".pl-job-status .amount").TextContentAsync(), first);
        Assert.Equal(0, _screen.Asked(Zipball));

        await Card.Locator(".pl-job-head .pl-job-cancel").ClickAsync();

        await Assertions.Expect(_screen.Cards).ToHaveCountAsync(0);
        held.Dispose();
        for (var attempt = 0; attempt < 50 && File.Exists(Path.Combine(Tools, "dotnet-sdk.zip.part")); attempt++) await Task.Delay(100);
        Assert.False(File.Exists(Path.Combine(Tools, "dotnet-sdk.zip.part")), "ダウンロードしかけが残った");
        Assert.False(Directory.Exists(Path.Combine(Tools, "dotnet")));
        Assert.Equal(0, _screen.Asked(Zipball));
    }
}
