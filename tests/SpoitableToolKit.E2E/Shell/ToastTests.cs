using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Shell;

/// <summary>消える時計はホスト側。実時間を待つのは 1 秒だけ（出しておく秒数の設定を先に 1 にしてから見る）。</summary>
public sealed class ToastTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private IPage Page => app.Page;

    private ILocator Toasts => Page.Locator("#toasts .toast");

    /// <summary>他のテストが残したトーストは、件数で見る判定を狂わせる。</summary>
    public Task InitializeAsync() => CloseAll();

    /// <summary>残したトーストは右下の操作を覆うので、DisposeAsync で片付ける。</summary>
    public async Task DisposeAsync()
    {
        await Save("{ ToastSeconds: 3 }");
        await CloseAll();

        await Page.Locator("#sidebar .item[data-id='log']").ClickAsync();
    }

    private Task Save(string hostChanges) => Page.EvaluateAsync(
        $"() => bridge.applyConfigChanges(JSON.stringify({{ host: {hostChanges} }}))");

    private async Task CloseAll()
    {
        await Page.EvaluateAsync("() => document.querySelectorAll('#toasts .toast .x').forEach(b => b.click())");
        await Assertions.Expect(Toasts).ToHaveCountAsync(0, new() { Timeout = 5_000 });
    }

    [Fact]
    public async Task SavingSettingsPopsAToastInTheCorner()
    {
        await Save("{ ToastSeconds: 3 }");

        await Assertions.Expect(Toasts.First).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(Toasts.First.Locator(".title")).ToContainTextAsync("保存");

        await Assertions.Expect(Page.Locator("#modal")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task ClosingItRemovesIt()
    {
        await Save("{ ToastSeconds: 3 }");
        await Assertions.Expect(Toasts.First).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await Toasts.First.Locator(".x").ClickAsync();

        await Assertions.Expect(Toasts).ToHaveCountAsync(0, new() { Timeout = 5_000 });
    }

    /// <summary>push は受け取れない間は捨てられるので、読み込み直しや VR オーバーレイは一覧で追いつく。</summary>
    [Fact]
    public async Task LiveOnesCanBeReadFromGetStatus()
    {
        await Save("{ ToastSeconds: 30 }");
        await Assertions.Expect(Toasts.First).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var live = await Page.EvaluateAsync<int>("async () => (await bridge.getStatus()).toasts.length");

        Assert.True(live >= 1, "getStatus に、出ている途中のトーストが入っていない");
    }

    /// <summary>本物の呼び手は hrs の書き出しだが、記録が無いと押せない（e2e では出せない）ので、描く側だけを見る。押したあとの道は unit が見ている。</summary>
    [Fact]
    public async Task ItsActionsAreListed()
    {
        await Page.EvaluateAsync(
            """
            () => window.stk.receive('toast', {
              id: 9001, title: 'e2e', body: null, progress: null,
              actions: [{ index: 0, label: 'フォルダを開く' }],
            })
            """);

        var toast = Page.Locator("#toasts .toast[data-id='9001']");
        await Assertions.Expect(toast.Locator(".act")).ToHaveTextAsync("フォルダを開く");

        // C# 側に相方が居ないので「閉じる」は効かない。引っ込めるところまで自分でやる。
        await Page.EvaluateAsync("() => window.stk.receive('toastClosed', { id: 9001 })");
        await Assertions.Expect(toast).ToHaveCountAsync(0);
    }

    /// <summary>偽なのは記録の中身だけ。「フォルダを開く」は押さない（エクスプローラーが前面に出てフォーカスを奪い、関係のないテストが落ちる）。</summary>
    [Fact]
    public async Task ExportingPopsAToastWithAnAction()
    {
        app.SeedHeartRates(5);

        // アプリの外から書いたので、既に開いているパネルは知らない。開き直させる（開き直した先で hrs が数え終わってから返すので、書き出しボタンが有効になる）。
        await Page.EvaluateAsync(
            """
            () => {
              const frame = document.querySelector("iframe.view[data-id='hrs']");
              if (frame) frame.src = frame.src;
            }
            """);

        await Page.Locator("#sidebar .item[data-id='hrs']").ClickAsync();

        var panel = Page.FrameLocator("iframe.view[data-id='hrs']");
        await panel.Locator("#tabStats").ClickAsync();

        var export = panel.Locator("#export");
        await Assertions.Expect(export).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await export.ClickAsync();

        var toast = Toasts.First;
        await Assertions.Expect(toast).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(toast.Locator(".title")).ToContainTextAsync("エクスポートしました");
        await Assertions.Expect(toast.Locator(".act")).ToHaveCountAsync(2);

        await toast.GetByRole(AriaRole.Button, new() { Name = "パスをコピー" }).ClickAsync();

        await Assertions.Expect(Toasts).ToHaveCountAsync(0, new() { Timeout = 10_000 });

        // 「いま」に戻す（ほかの試験は「いま」のタイルや一覧を見る）。
        await panel.Locator("#tabNow").ClickAsync();
    }

    [Fact]
    public async Task LeftAloneItDismissesItself()
    {
        await Save("{ ToastSeconds: 1 }");

        await Assertions.Expect(Toasts.First).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(Toasts).ToHaveCountAsync(0, new() { Timeout = 10_000 });
    }
}
