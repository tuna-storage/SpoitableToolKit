using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Modules;

/// <summary>
/// モジュールの画面（管理画面）の掴み方。モジュールをインストールする試験のクラスが、同じこれを使う。
/// <para>
/// インストールしている途中のもの 1 つ = カード 1 枚（<c>#plJobs .pl-job</c>）。カードは repo の名前（<c>aria-label</c>）で掴む。
/// 確かめ（Build・.NET SDK・インストール）は「モジュールを追加」のモーダル（<see cref="Dialog"/>。追っている件の <c>data-id</c>・<c>data-state</c>・<c>data-asking</c> を持つ）に出る。
/// カードは「確認待ち」と「確認する」だけ。
/// <b>Page は持たない</b>（アプリを起動し直すと替わるので、毎回 <see cref="AppFixture.Page"/> から取る）。
/// </para>
/// </summary>
internal sealed class ModuleScreen(AppFixture app)
{
    private IPage Page => app.Page;

    public ILocator Cards => Page.Locator("#plJobs .pl-job");

    public ILocator Card(string title) => Page.Locator($"#plJobs .pl-job[aria-label='{title}']");

    public ILocator Row(string id) => Page.Locator($"#plList .pl-row[data-id='{id}']");

    /// <summary>「更新」のタブの行（更新のあるものだけが並ぶ）。</summary>
    public ILocator UpdateRow(string id) => Page.Locator($"#plUpdateList .pl-row[data-id='{id}']");

    /// <summary>「モジュールを追加」のモーダル（段は <c>data-step</c>: url / agree / job / refused）。</summary>
    public ILocator Dialog => Page.Locator("#maModal");

    /// <summary>モーダルの見出し（URL と同意の段は「モジュールを追加」、件を追う段は repo）と、その下の commit。</summary>
    public ILocator DialogTitle => Page.Locator("#maTitle");

    public ILocator DialogCommit => Page.Locator("#maSub");

    /// <summary>見出し「インストール中:」（カードが無ければ隠れる）と、その右の「残り N 件」。</summary>
    public ILocator JobsHead => Page.Locator("#plJobsHead");

    public ILocator JobsCount => Page.Locator("#plJobsCount");

    /// <summary>URL の段の、欄の下の理由（見出し <c>.t</c> と本体の文 <c>.d</c>）。</summary>
    public ILocator UrlMessage => Page.Locator("#maUrlMsg");

    /// <summary>更新のタブの、確かめている最中・確かめられなかったときの一言。</summary>
    public ILocator CheckMessage => Page.Locator("#plCheckMsg");

    /// <summary>更新のタブの「最後に確認: …」。</summary>
    public ILocator CheckedAt => Page.Locator("#plCheckedAt");

    public ILocator UpdatesTab => Page.Locator("#plTabs .spoitable-tab[data-tab='updates']");

    public ILocator RestartBar => Page.Locator("#plRestart");

    /// <summary>行の「…」のメニュー（右クリックのメニューと同じ箱）。</summary>
    public ILocator Menu => Page.Locator("#ctxMenu");

    public string Modules => Path.Combine(app.DataDirectory, "modules");

    /// <summary>仮置き（確かめたもの。インストールするかを聞いている間だけある）。</summary>
    public string Staging => Path.Combine(Modules, ".staging");

    /// <summary>Build の作業場（終わったら消える）。</summary>
    public string BuildFolder => Path.Combine(app.DataDirectory, ".module-build");

    /// <summary>確かめの 2 列から、<paramref name="label"/> の右の値。</summary>
    public static ILocator Fact(ILocator box, string label) => box.Locator(
        $"xpath=.//*[contains(@class,'pl-job-facts')]/*[contains(concat(' ', normalize-space(@class), ' '), ' k ')][normalize-space(.)='{label}']/following-sibling::*[1]");

    public async Task OpenAsync() => await Page.Locator("#sidebar .item[data-id='moduleManager']").ClickAsync();

    public async Task ShowTabAsync(string tab) => await Page.Locator($"#plTabs .spoitable-tab[data-tab='{tab}']").ClickAsync();

    /// <summary>「モジュールを追加」を押して、URL を入れて「次へ」（同意の段の手前まで）。</summary>
    public async Task EnterUrlAsync(string url)
    {
        await Page.Locator("#plAddBtn").ClickAsync();
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-step", "url");
        await Page.Locator("#maUrl").FillAsync(url);
        await Page.Locator("#maNext").ClickAsync();

        // 本体が URL を確かめ終わるまで（段が進む・理由が出る。理由は「次へ」で消えてから出直す）。
        await Page.WaitForFunctionAsync("() => el.maModal.hidden || el.maModal.dataset.step !== 'url' || !el.maUrlMsg.hidden");
    }

    /// <summary>
    /// 「モジュールを追加」から件を始めて、モーダルで追うところまで。同意の段が出たら 1 つ目と 2 つ目だけ入れて続ける
    /// （3 つ目は入れない — 次も同意の段が出る）。
    /// </summary>
    public async Task LookUpAsync(string url)
    {
        await EnterUrlAsync(url);
        if (await Dialog.GetAttributeAsync("data-step") == "agree") await AgreeAsync();

        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-step", "job");
    }

    /// <summary>同意の段で、1 つ目と 2 つ目（<paramref name="hide"/> なら 3 つ目も）を入れて「続ける」。</summary>
    public async Task AgreeAsync(bool hide = false)
    {
        await Assertions.Expect(Dialog).ToHaveAttributeAsync("data-step", "agree");
        await Page.Locator("#maAgreeRisk").CheckAsync();
        await Page.Locator("#maAgreeLiability").CheckAsync();
        if (hide) await Page.Locator("#maAgreeHide").CheckAsync();
        await Page.Locator("#maContinue").ClickAsync();
    }

    /// <summary>モーダルを閉じる（Esc。件は止まらない）。</summary>
    public async Task CloseDialogAsync()
    {
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Dialog).ToBeHiddenAsync();
    }

    public async Task PickAsync(ILocator row, string item)
    {
        await OpenMenuAsync(row);
        await Menu.Locator($"button[data-id='{item}']").ClickAsync();
        await Assertions.Expect(Menu).ToBeHiddenAsync();
    }

    /// <summary>
    /// 行の「…」を押して、メニューが出るまで。<b>先に見える所まで巻いて、巻き終わりの知らせを待ってから押す</b> —
    /// 巻くのと押すのを続けると、巻いた知らせ（scroll）が開いたあとに届いて、メニューが閉じる（巻いたら閉じる決まり）。
    /// </summary>
    public async Task OpenMenuAsync(ILocator row)
    {
        var button = row.Locator(".pl-more");
        await button.ScrollIntoViewIfNeededAsync();
        await Page.EvaluateAsync("() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done)))");
        await button.ClickAsync();
        await Assertions.Expect(Menu).ToBeVisibleAsync();
    }

    public async Task<string[]> MenuItemsAsync() =>
        [.. await Menu.Locator("button").EvaluateAllAsync<string[]>("list => list.map(b => b.dataset.id)")];

    public async Task DismissMenuAsync()
    {
        await Page.EvaluateAsync("() => dismissMenu?.()");
        await Assertions.Expect(Menu).ToBeHiddenAsync();
    }

    public int Asked(string path) => app.GitHub.Requests.Count(request => request == path);

    /// <summary><paramref name="path"/> が <paramref name="times"/> 回聞かれるまで待つ（足止めした道に、アプリが届いたのを待つ）。</summary>
    public async Task WaitUntilAskedAsync(string path, int times = 1)
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            if (Asked(path) >= times) return;
            await Task.Delay(100);
        }

        Assert.Fail($"{path} が {times} 回聞かれなかった（{Asked(path)} 回）。聞かれた道: {string.Join(", ", app.GitHub.Requests.TakeLast(20))}");
    }

    /// <summary>
    /// <b>本体の台帳</b>（画面ではなく）に、<paramref name="predicate"/>（JS の <c>job =&gt; …</c>）を満たす件が現れるまで待つ。
    /// 画面に配られない変化（間引かれた量の進み）を待つのに使う。
    /// </summary>
    public async Task WaitForLedgerAsync(string predicate)
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            if (await Page.EvaluateAsync<bool>($"async () => (await bridge.getModuleJobs()).jobs.some({predicate})")) return;
            await Task.Delay(100);
        }

        Assert.Fail($"本体の台帳に、{predicate} を満たす件が現れなかった");
    }

    /// <summary>
    /// インストールしている途中のものを、全部やめる（片付け。このクラスのアプリは 1 つなので、残すと次の試験が同じ repo の前の件を掴む）。
    /// モーダルとメニューも閉じる（画面の状態なので、次の試験へ渡さない）。
    /// <para>
    /// <b>やめてから、足止めを放す</b>（放すのが先だと、やめる前に答えが届いて失敗の件として残ることがある）。それでも入れ違いは起きるので、
    /// <b>本体の台帳が空になるまで</b>繰り返す（失敗して残った件は、次の回の「やめる」で消える）。
    /// </para>
    /// </summary>
    public async Task ClearJobsAsync()
    {
        await Page.EvaluateAsync("() => { dismissMenu?.(); closeAddModule(); }");

        var left = 0;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            left = await Page.EvaluateAsync<int>("""
                async () => {
                  const { jobs } = await bridge.getModuleJobs();
                  for (const job of jobs) await bridge.cancelModuleJob(job.id);
                  return jobs.length;
                }
                """);
            app.GitHub.ReleaseAll();
            if (left == 0) break;
            await Task.Delay(100);
        }

        Assert.True(left == 0, $"インストールしている途中のものが {left} 件残った");
        await Assertions.Expect(Cards).ToHaveCountAsync(0);
        await Page.EvaluateAsync("() => closeAddModule()");
    }

    /// <summary><paramref name="directory"/> の中にフォルダが残っていない（片付けは少し遅れるので、5 秒まで待つ）。</summary>
    public static async Task ExpectNoDirectoriesAsync(string directory)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (!Directory.Exists(directory) || Directory.GetDirectories(directory).Length == 0) return;
            await Task.Delay(100);
        }

        Assert.Fail($"{directory} に残っている: {string.Join(", ", Directory.GetDirectories(directory).Select(Path.GetFileName))}");
    }
}
