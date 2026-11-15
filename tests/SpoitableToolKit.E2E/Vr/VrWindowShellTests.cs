using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Vr;

/// <summary>
/// VrLoop が窓の browser に開かせる URL（<c>index.html?surface=window&amp;view=…</c>）を、そのまま本物のシェルに読ませる。
/// ページを読み直すので、終わったら元の URL に戻す。
/// ホストへの呼び出しは、bind された <c>stkHost</c> の口を差し替えて数える（<c>SPOITABLE_NO_VR</c> の起動では、本物へ通しても何も起きず見えない）。
/// ✕ がデスクトップの窓を閉じる口へ流れるとアプリの窓が閉じて後の試験が全部落ちるので、押す前に両方とも差し替える。
/// </summary>
public sealed class VrWindowShellTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private IPage Page => app.Page;

    private string _home = "";

    public async Task InitializeAsync()
    {
        _home = Page.Url;
        await Open(new Uri(new Uri(_home), "index.html?surface=window&view=settings").ToString());
    }

    public async Task DisposeAsync() => await Open(_home);

    private async Task Open(string url)
    {
        await Page.GotoAsync(url);
        await Page.WaitForFunctionAsync("() => window.__stkReady === true", null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
    }

    private Task Spy(params string[] names) => Page.EvaluateAsync(
        """
        names => {
          window.__calls = [];
          for (const name of names) stkHost[name] = (token, ...args) => { window.__calls.push([name, ...args].join(' ')); };
        }
        """,
        names);

    private Task<string[]> Calls() => Page.EvaluateAsync<string[]>("() => window.__calls");

    /// <summary>作った直後のページには push が届かないので、開く画面は URL で受ける。</summary>
    [Fact]
    public async Task TheWindowStartsOnTheScreenItWasOpenedFor()
    {
        await Assertions.Expect(Page.Locator("#sidebar .item.on")).ToHaveAttributeAsync("data-id", "settings");
        await Assertions.Expect(Page.Locator("#viewSettings")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task OnlyTheCloseButtonIsLeftInTheTitleBar()
    {
        await Assertions.Expect(Page.Locator("#winMin")).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator("#winMax")).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator("#winClose")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TheCrossClosesTheVrWindowNotTheDesktopWindow()
    {
        await Spy("closeVrWindow", "closeWindow");

        await Page.Locator("#winClose").ClickAsync();

        Assert.Equal(["closeVrWindow"], await Calls());
    }

    /// <summary>手首のショートカットをもう一度押したとき、閉じるか切り替えるかを C# が決めるので、ホストへ知らせる。</summary>
    [Fact]
    public async Task SwitchingScreensTellsTheHost()
    {
        await Spy("vrWindowViewing");

        await Page.Locator("#sidebar .item[data-id='log']").ClickAsync();

        Assert.Equal(["vrWindowViewing log"], await Calls());
    }

    /// <summary>字の大きさは 0.5 m の板で読める大きさ（デスクトップの 1.5 倍）。通知は手首に出るのでベルは出さない。</summary>
    [Fact]
    public async Task TheWindowHasLargerTextAndANarrowRail()
    {
        Assert.Equal("21px", await Page.EvaluateAsync<string>("() => getComputedStyle(document.body).fontSize"));

        var rail = await Page.Locator("#sidebar").BoundingBoxAsync();
        Assert.InRange(rail!.Width, 100, 110);

        await Assertions.Expect(Page.Locator("#sidebar .item[data-id='log'] .lbl")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#sidebar .item[data-id='dashboard']")).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator("#bell")).ToBeHiddenAsync();
    }

    /// <summary>
    /// 窓は字が大きく幅が足りないので、設定の右の目次は隠して本文に回す。ページを選ぶ左の一覧は残す。
    /// 目次は作ってあり（hidden ではない）、隠しているのは幅の決まりだけ。
    /// </summary>
    [Fact]
    public async Task TheSettingsKeepTheListButHideTheContentsInTheWindow()
    {
        await app.OpenSettings("hrs");

        await Assertions.Expect(app.SettingsPage("hrs")).ToHaveAttributeAsync("aria-current", "page");
        await Assertions.Expect(Page.Locator("#cfgNav")).ToBeVisibleAsync();
        await Assertions.Expect(app.SettingsPage("display")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#cfgToc button")).Not.ToHaveCountAsync(0);
        Assert.Null(await Page.Locator("#cfgToc").GetAttributeAsync("hidden"));
        await Assertions.Expect(Page.Locator("#cfgToc")).ToBeHiddenAsync();

        // 一覧で別のページへ移れる。
        await app.SettingsPage("notify").ClickAsync();
        await Assertions.Expect(Page.Locator("#viewSettings .cfg-item[data-key='ConfirmSeconds']")).ToBeVisibleAsync();
    }

    /// <summary>別の origin なので中の字は直せず、iframe ごと拡大する。枠が本文と同じ大きさで、中から見た画面が 1/1.5 になっているかを見る。</summary>
    [Fact]
    public async Task AModulePanelIsScaledToFillTheWindow()
    {
        await Page.Locator("#sidebar .item[data-id='hrs']").ClickAsync();

        var frame = Page.Locator("iframe.view[data-id='hrs']");
        await Assertions.Expect(frame).ToBeVisibleAsync();

        Assert.Equal("1.5", await frame.EvaluateAsync<string>("e => getComputedStyle(e).zoom"));
        await AssertFills(frame);

        // バナーを 1 本出す（読み直して元の URL に戻すので、後に残らない）。
        await Page.EvaluateAsync("() => window.stk.receive('banner', { id: 'e2e-vr-window', kind: 'error', title: 'e2e', body: 'banner' })");
        await Assertions.Expect(Page.Locator("#banners .banner[data-id='e2e-vr-window']")).ToBeVisibleAsync();
        await AssertFills(frame);
    }

    /// <summary>手首の通知は字だけなので、押せるもの・進み具合のあるものは窓にも出さないと VR の中で押す口が無くなる。</summary>
    [Fact]
    public async Task OnlyToastsWithButtonsOrProgressAreShownInTheWindow()
    {
        await Page.EvaluateAsync(
            """
            () => {
              window.stk.receive('toast', { id: 9101, title: 'plain', body: null, progress: null, actions: [] });
              window.stk.receive('toast', { id: 9102, title: 'act', body: null, progress: null, actions: [{ index: 0, label: 'open' }] });
              window.stk.receive('toast', { id: 9103, title: 'prog', body: null, progress: 0.4, actions: [] });
            }
            """);

        await Assertions.Expect(Page.Locator("#toasts .toast[data-id='9101']")).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator("#toasts .toast[data-id='9102'] .act")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#toasts .toast[data-id='9103'] .pct")).ToHaveTextAsync("40%");
        await Assertions.Expect(Page.Locator("#toasts .toast[data-id='9103']")).ToBeVisibleAsync();
    }

    /// <summary>浮かせる部品（暦・一覧）は body の直下に置かれて字を自分で決めているので、何もしないと 12px のまま。選べることも見る。</summary>
    [Fact]
    public async Task TheCalendarAndListsAreAsLargeAsTheWindowText()
    {
        await Page.Locator("#sidebar .item[data-id='log']").ClickAsync();

        await InPage.OpenWhenAsync(Page, "#logFrom");
        var day = Page.Locator(".spoitable-when-pop:not([hidden]) .spoitable-when-grid button:not(.spoitable-when-other)").First;
        Assert.Equal("21px", await day.EvaluateAsync<string>("e => getComputedStyle(e).fontSize"));

        await InPage.WhenDayAsync(Page, 1);
        Assert.Matches(@"^\d{4}-\d{2}-01T", await Page.Locator("#logFrom").InputValueAsync());

        await Page.Keyboard.PressAsync("Escape");
        await Page.Locator(".spoitable-pick-box:has(#logSize) .spoitable-pick-value").ClickAsync();
        var item = Page.Locator(".spoitable-pick-list:not([hidden]) button").First;
        Assert.Equal("21px", await item.EvaluateAsync<string>("e => getComputedStyle(e).fontSize"));
    }

    [Fact]
    public async Task TheCalendarKeepsItsSizeOutsideTheWindow()
    {
        await Open(_home);
        await Page.Locator("#sidebar .item[data-id='log']").ClickAsync();

        await InPage.OpenWhenAsync(Page, "#logFrom");
        var day = Page.Locator(".spoitable-when-pop:not([hidden]) .spoitable-when-grid button:not(.spoitable-when-other)").First;
        Assert.Equal("12px", await day.EvaluateAsync<string>("e => getComputedStyle(e).fontSize"));

        await Page.Keyboard.PressAsync("Escape");
    }

    private async Task AssertFills(ILocator frame)
    {
        var (box, views) = (await frame.BoundingBoxAsync(), await Page.Locator("#views").BoundingBoxAsync());
        Assert.InRange(box!.Height, views!.Height - 2, views.Height + 1);
        Assert.InRange(box.Width, views.Width - 2, views.Width + 1);
        Assert.InRange(box.Y, views.Y - 1, views.Y + 1);

        var inner = Page.FrameLocator("iframe.view[data-id='hrs']").Locator("html");
        var (width, height) = (await inner.EvaluateAsync<double>("() => innerWidth"), await inner.EvaluateAsync<double>("() => innerHeight"));
        Assert.InRange(width, views.Width / 1.5 - 2, views.Width / 1.5 + 1);
        Assert.InRange(height, views.Height / 1.5 - 2, views.Height / 1.5 + 1);
    }

    [Fact]
    public async Task TheUsualShellKeepsItsOwnBehaviour()
    {
        await Open(_home);
        await Spy("vrWindowViewing", "closeVrWindow", "closeWindow");

        await Page.Locator("#sidebar .item[data-id='settings']").ClickAsync();
        await Assertions.Expect(Page.Locator("#winMin")).ToBeVisibleAsync();
        await Page.Locator("#winClose").ClickAsync();

        Assert.Equal(["closeWindow"], await Calls());
    }

    [Fact]
    public async Task TheWindowShowsWideScrollbarsAndTheDesktopKeepsThinOnes()
    {
        Assert.Equal(22, await ScrollbarWidthAsync(Page.Locator("body")));

        await Open(_home);
        Assert.Equal(10, await ScrollbarWidthAsync(Page.Locator("body")));
    }

    /// <summary>別 origin なのでシェルの CSS は届かない。シェルが URL に surface=window を付け、bridge.js が太くする。パネルは iframe ごと 1.5 倍なので、中では 15px（見た目は 22px）。</summary>
    [Fact]
    public async Task APanelInTheWindowGetsTheWideScrollbarsToo()
    {
        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();
        var frame = Page.Locator("iframe.view[data-id='videourl']");
        await Assertions.Expect(frame).ToBeVisibleAsync();
        Assert.Contains("surface=window", await frame.GetAttributeAsync("src"));

        var inside = Page.FrameLocator("iframe.view[data-id='videourl']").Locator("body");
        await Assertions.Expect(inside).ToBeVisibleAsync();
        Assert.Equal(15, await ScrollbarWidthAsync(inside));

        await Open(_home);
        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();
        await Assertions.Expect(frame).ToBeVisibleAsync();
        Assert.DoesNotContain("surface=", await frame.GetAttributeAsync("src"));
    }

    /// <summary>
    /// 場所はパネルの URL の <c>#</c> の後ろに付き（パネルは <c>location.hash</c> / <c>hashchange</c> で受ける）、
    /// C# へは場所ごと返す（同じパネルの別の場所のタイルは「閉じる」ではなく「切り替える」になる）。
    /// </summary>
    [Fact]
    public async Task AShortcutToAPlaceOpensThePanelThere()
    {
        await Open(new Uri(new Uri(_home), "index.html?surface=window&view=" + Uri.EscapeDataString("videourl#recent")).ToString());

        var frame = Page.Locator("iframe.view[data-id='videourl']");
        await Assertions.Expect(frame).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#sidebar .item.on")).ToHaveAttributeAsync("data-id", "videourl");
        var first = await frame.GetAttributeAsync("src");
        Assert.EndsWith("#recent", first);
        Assert.Contains("surface=window", first);

        // パネルが自分で別の場所へ移った（iframe の src の属性は #recent のまま）。同じ場所のタイルを押し直したら戻る。
        var panel = await (await frame.ElementHandleAsync()).ContentFrameAsync()
            ?? throw new InvalidOperationException("パネルの iframe の中身が無い");
        await panel.EvaluateAsync("() => { location.hash = 'moved'; }");
        await Page.EvaluateAsync("() => selectView('videourl#recent')");
        await panel.WaitForFunctionAsync("() => location.hash === '#recent'");

        await Spy("vrWindowViewing");
        await Page.EvaluateAsync("() => selectView('videourl#other')");

        Assert.EndsWith("#other", await frame.GetAttributeAsync("src"));
        var calls = await Calls();
        Assert.Equal(["vrWindowViewing videourl#other"], calls);
    }

    [Fact]
    public async Task LayoutModeDimsTheWindowAndSaysHowToMoveIt()
    {
        var hint = Page.Locator("#layoutHint");
        await Assertions.Expect(hint).ToBeHiddenAsync();

        await Page.EvaluateAsync("() => window.stk.receive('vrLayout', { on: true })");

        await Assertions.Expect(hint).ToBeVisibleAsync();
        await Assertions.Expect(hint).Not.ToHaveTextAsync("");
        Assert.Equal("0.35", await Page.Locator(".frame").EvaluateAsync<string>("node => getComputedStyle(node).opacity"));
        Assert.Equal("1", await hint.EvaluateAsync<string>("node => getComputedStyle(node).opacity"));

        await Page.EvaluateAsync("() => window.stk.receive('vrLayout', { on: false })");

        await Assertions.Expect(hint).ToBeHiddenAsync();
        Assert.Equal("1", await Page.Locator(".frame").EvaluateAsync<string>("node => getComputedStyle(node).opacity"));
    }

    /// <summary>
    /// Layout Mode の間に初めて窓を開くと、切り替えの push は窓が出来る前に済んでいるので、読み終わったときに聞き直す。
    /// この起動は VR が無い（答えは false）ので、bind された口の答えを差し替える。差し替えは印 <c>e2eLayout</c> の付いた読み込みだけ
    /// （init script はこのページのあとの読み込みにも残る）。
    /// </summary>
    [Fact]
    public async Task TheWindowAsksForLayoutModeWhenItLoads()
    {
        // stkHost に setter を張っても通らない（CefSharp は bind した口を自分で作る）ので、bind を包んで、bind し終わった直後に答えを差し替える。
        await Page.AddInitScriptAsync("""
            if (new URLSearchParams(location.search).has('e2eLayout')) {
              const bind = CefSharp.BindObjectAsync.bind(CefSharp);
              CefSharp.BindObjectAsync = async (...names) => {
                const bound = await bind(...names);
                stkHost.getVrLayout = async () => true;
                return bound;
              };
            }
            """);

        await Open(new Uri(new Uri(_home), "index.html?surface=window&view=settings&e2eLayout=1").ToString());

        await Assertions.Expect(Page.Locator("#layoutHint")).ToBeVisibleAsync();
        Assert.True(await Page.EvaluateAsync<bool>("() => document.body.classList.contains('vr-layout')"));
    }

    /// <summary>
    /// 答えは bind の道、push は別の道で、着く順は決まっていない。
    /// 答えを止めて、その間に「完了」の push を届けてから、古い「入」を返す。
    /// </summary>
    [Fact]
    public async Task AnOldLayoutAnswerDoesNotUndoANewerPush()
    {
        await Page.AddInitScriptAsync("""
            if (new URLSearchParams(location.search).has('e2eLayoutLate')) {
              const bind = CefSharp.BindObjectAsync.bind(CefSharp);
              CefSharp.BindObjectAsync = async (...names) => {
                const bound = await bind(...names);
                stkHost.getVrLayout = () => new Promise(resolve => { window.__e2eLayoutAnswer = resolve; });
                return bound;
              };
            }
            """);

        await Page.GotoAsync(new Uri(new Uri(_home), "index.html?surface=window&view=settings&e2eLayoutLate=1").ToString());
        await Page.WaitForFunctionAsync("() => typeof window.__e2eLayoutAnswer === 'function'", null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });

        await Page.EvaluateAsync("() => { window.stk.receive('vrLayout', { on: false }); window.__e2eLayoutAnswer(true); }");
        await Page.WaitForFunctionAsync("() => window.__stkReady === true", null, new PageWaitForFunctionOptions { Timeout = 30_000 });

        Assert.False(await Page.EvaluateAsync<bool>("() => document.body.classList.contains('vr-layout')"));
    }

    [Fact]
    public async Task TheDesktopIgnoresLayoutMode()
    {
        await Open(_home);

        await Page.EvaluateAsync("() => window.stk.receive('vrLayout', { on: true })");

        await Assertions.Expect(Page.Locator("#layoutHint")).ToBeHiddenAsync();
        Assert.False(await Page.EvaluateAsync<bool>("() => document.body.classList.contains('vr-layout')"));
    }

    /// <summary><paramref name="inside"/> の文書に、縦に巻ける箱を置いて巻き棒の幅を測る（測ったら外す）。</summary>
    private static Task<int> ScrollbarWidthAsync(ILocator inside) => inside.EvaluateAsync<int>("""
        node => {
          const box = node.ownerDocument.createElement('div');
          box.style.cssText = 'position:fixed;left:0;top:0;width:200px;height:100px;overflow-y:scroll;visibility:hidden';
          box.appendChild(node.ownerDocument.createElement('div')).style.height = '400px';
          node.ownerDocument.body.appendChild(box);
          const width = box.offsetWidth - box.clientWidth;
          box.remove();
          return width;
        }
        """);
}
