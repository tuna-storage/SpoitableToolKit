using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Shell;

/// <summary>ToBeVisible は切られていても通るので、実際に画面に出ているかは別に見る。ネイティブのメニューは CEF が OS 側に描き DOM が変わらないので、ここでは見られない。</summary>
public sealed class ContextMenuTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private const int InjectedId = 990501;

    private IPage Page => app.Page;

    private ILocator Menu => Page.Locator("#ctxMenu");

    public Task InitializeAsync() => Page.Locator("#sidebar .item[data-id='log']").ClickAsync();

    public async Task DisposeAsync()
    {
        await Page.EvaluateAsync("() => dismissMenu?.()");
        await RemoveInjectedRowAsync();
    }

    /// <summary>右クリックの直後に見ると「まだ出ていない」だけで緑になるので、往復の終わりを待ってから見る。</summary>
    [Fact]
    public async Task NothingOpensOnTheBarePage()
    {
        await RightClickBackgroundAsync();

        await Assertions.Expect(Menu).ToBeHiddenAsync();
        Assert.Equal(0, await Page.EvaluateAsync<int>("() => window.__stkCtx.items"));
    }

    [Fact]
    public async Task TheDefaultMenuIsPreventedInThePageToo()
    {
        var delivered = await Page.EvaluateAsync<bool>(
            "() => document.body.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }))");

        Assert.False(delivered);
    }

    [Fact]
    public async Task ALogRowOffersToCopyItself()
    {
        await InjectRowAsync("'e2e-menu-row'");
        await RightClickAsync(Page.Locator($"#rowsVrc .row[data-id='{InjectedId}'] .line"));

        await Assertions.Expect(Menu).ToBeVisibleAsync();
        Assert.Equal(["host copyRow", "host copyDetail"], await ItemIdsAsync());

        // t() は無いキーをそのまま返すので、文言は引いてから出す。
        var button = Menu.Locator("button").First;
        Assert.NotEqual(await button.GetAttributeAsync("data-label-key"), (await button.TextContentAsync())?.Trim());
    }

    /// <summary>ToBeVisible は親に切られていても窓の外でも通るので、画面自身に聞く。</summary>
    [Fact]
    public async Task TheMenuIsWhereItSaysItIs()
    {
        await InjectRowAsync("'e2e-menu-onscreen'");
        await RightClickAsync(Page.Locator($"#rowsVrc .row[data-id='{InjectedId}'] .line"));
        await Assertions.Expect(Menu).ToBeVisibleAsync();

        var onScreen = await Page.EvaluateAsync<bool>("""
            () => {
              const box = el.ctxMenu.getBoundingClientRect();
              const at = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2);
              return !!at && el.ctxMenu.contains(at);
            }
            """);

        Assert.True(onScreen);
    }

    [Fact]
    public async Task TheDetailItemIsOfferedWithoutOpeningTheRow()
    {
        await InjectRowAsync("'e2e-menu-detail'");
        var row = Page.Locator($"#rowsVrc .row[data-id='{InjectedId}']");

        await RightClickAsync(row.Locator(".line"));
        Assert.Equal(["host copyRow", "host copyDetail"], await ItemIdsAsync());

        await Page.EvaluateAsync("() => closeMenu?.()");
        await row.Locator(".line").ClickAsync();
        await Assertions.Expect(Page.Locator("#logSheet")).ToBeVisibleAsync();

        try
        {
            await RightClickAsync(row.Locator(".line"));
            Assert.Equal(["host copyRow", "host copyDetail"], await ItemIdsAsync());
        }
        finally
        {
            await Page.EvaluateAsync("() => { closeMenu?.(); closeSheet(); }");
        }
    }

    [Fact]
    public async Task EscapeClosesIt()
    {
        await InjectRowAsync("'e2e-menu-escape'");
        await RightClickAsync(Page.Locator($"#rowsVrc .row[data-id='{InjectedId}'] .line"));
        await Assertions.Expect(Menu).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Menu).ToBeHiddenAsync();
    }

    [Fact]
    public async Task AnInputOffersTheEditingItems()
    {
        await app.OpenSettings("hrs");

        var device = Page.Locator("#viewSettings .cfg-item[data-key='Device'] input[type=text]");
        await Assertions.Expect(device).ToBeVisibleAsync();

        await RightClickAsync(device);

        Assert.Equal(["host cut", "host copy", "host paste"], await ItemIdsAsync());
    }

    /// <summary>type=number / datetime-local は選択 API を持たず、setRangeText が InvalidStateError を投げる。出すと貼り付けが押せるのに何も入らない。</summary>
    [Theory]
    [InlineData("#viewSettings .cfg-item[data-key='ConfirmSeconds'] input")]
    [InlineData("#e2eWhen")]
    public async Task AFieldWithoutASelectionOffersNothing(string selector)
    {
        await app.OpenSettings("notify");

        // #logFrom / #logTo はページ内のカレンダーを被せて元の欄を隠してあるので、型ごとの判断は欄を 1 つ置いて見る。
        if (selector == "#e2eWhen")
        {
            await Page.EvaluateAsync(
                """
                () => {
                  const field = document.createElement('input');
                  field.id = 'e2eWhen';
                  field.type = 'datetime-local';
                  document.getElementById('viewSettings').prepend(field);
                }
                """);
        }

        var field = Page.Locator(selector).First;
        await Assertions.Expect(field).ToBeVisibleAsync();

        // 別の場所の選択が残ると「コピー」が出るので、名乗りの前に畳む。
        await Page.EvaluateAsync("() => window.getSelection().removeAllRanges()");

        await RightClickAsync(field);

        await Assertions.Expect(Menu).ToBeHiddenAsync();
        Assert.Equal(0, await Page.EvaluateAsync<int>("() => window.__stkCtx.items"));

        await Page.EvaluateAsync("() => document.getElementById('e2eWhen')?.remove()");
    }

    [Fact]
    public async Task ASecretFieldOffersOnlyPaste()
    {
        await app.OpenSettings("videourl");
        await Page.Locator("#viewSettings .cfg-item[data-key='Server'] input[type=checkbox]").CheckAsync();

        var token = Page.Locator("#viewSettings .cfg-item[data-key='Server.Token'] input");
        await Assertions.Expect(token).ToBeVisibleAsync();

        await RightClickAsync(token);
        Assert.Equal(["host paste"], await ItemIdsAsync());

        await Page.EvaluateAsync("() => closeMenu?.()");

        // #cfgCancel は押さない（戻した時点で「適用」の帯が消え、見えないボタンを待って落ちる）。
        await Page.Locator("#viewSettings .cfg-item[data-key='Server'] input[type=checkbox]").UncheckAsync();
    }

    /// <summary>value を直に書き換えると input が出ないので、入力を見張る側に届くこと（「適用」が生えること）まで見る。</summary>
    [Fact]
    public async Task PastingFillsTheFieldAndArmsApply()
    {
        await app.OpenSettings("hrs");

        var device = Page.Locator("#viewSettings .cfg-item[data-key='Device'] input[type=text]");
        await Assertions.Expect(device).ToBeVisibleAsync();

        var before = await device.InputValueAsync();

        try
        {
            await Page.EvaluateAsync("() => bridge.copyToClipboard('e2e-pasted')");
            await device.ClickAsync();
            await device.PressAsync("Control+a");

            await RightClickAsync(device);
            await Menu.Locator("button[data-id='host paste']").ClickAsync();

            await ExpectPastedAsync(device, "e2e-pasted");

            await Assertions.Expect(Page.Locator("#cfgApply")).ToBeVisibleAsync();
        }
        finally
        {
            await CancelIfArmedAsync();
            await Assertions.Expect(device).ToHaveValueAsync(before);
        }
    }

    /// <summary>試験のアプリは Windows のクリップボードに触らない（手元のクリップボードと履歴を荒らさないため）。</summary>
    [Fact]
    public async Task TheAppUnderTestLeavesTheWindowsClipboardAlone()
    {
        var marker = "e2e-" + Guid.NewGuid().ToString("N");

        Assert.True(await Page.EvaluateAsync<bool>("text => bridge.copyToClipboard(text)", marker));

        Assert.NotEqual(marker, await WindowsClipboardAsync());
    }

    private static async Task<string> WindowsClipboardAsync()
    {
        using var reader = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "powershell", "-NoProfile -NonInteractive -Command Get-Clipboard -Raw")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("powershell を起動できなかった");

        var text = await reader.StandardOutput.ReadToEndAsync();
        await reader.WaitForExitAsync();
        return text.TrimEnd('\r', '\n');
    }

    /// <summary>落ちたときはアプリの出力の終わりを一緒に出す（読めなかったのか空だったのかが分かる）。</summary>
    private async Task ExpectPastedAsync(ILocator field, string value)
    {
        try
        {
            await Assertions.Expect(field).ToHaveValueAsync(value);
        }
        catch (PlaywrightException ex)
        {
            throw new Xunit.Sdk.XunitException(
                $"{ex.Message}{Environment.NewLine}アプリの出力（終わりの 40 行）:{Environment.NewLine}" +
                string.Join(Environment.NewLine, app.Output.TakeLast(40)));
        }
    }

    /// <summary>「取り消す」は帯が出ているときだけ押す（出ていないと 30 秒待って落ち、本当の失敗が隠れる）。</summary>
    private async Task CancelIfArmedAsync()
    {
        if (await Page.Locator("#cfgCancel").IsVisibleAsync()) await Page.Locator("#cfgCancel").ClickAsync();
    }

    /// <summary>引数なしの setRangeText は押したときの選択を使うので、右クリックした時点の範囲を置き換えることを、間で選択を動かして見る。</summary>
    [Fact]
    public async Task PastingUsesTheRangeFromWhenTheMenuOpened()
    {
        await app.OpenSettings("hrs");

        var device = Page.Locator("#viewSettings .cfg-item[data-key='Device'] input[type=text]");
        await Assertions.Expect(device).ToBeVisibleAsync();

        var before = await device.InputValueAsync();

        try
        {
            await Page.EvaluateAsync("() => bridge.copyToClipboard('WHOLE')");

            await device.FillAsync("abcdef");
            await device.EvaluateAsync("field => field.setSelectionRange(0, 6)");

            await RightClickAsync(device);
            await Assertions.Expect(Menu).ToBeVisibleAsync();

            await device.EvaluateAsync("field => field.setSelectionRange(0, 0)");

            await Menu.Locator("button[data-id='host paste']").ClickAsync();

            await ExpectPastedAsync(device, "WHOLE");
        }
        finally
        {
            await CancelIfArmedAsync();
            await Assertions.Expect(device).ToHaveValueAsync(before);
        }
    }

    /// <summary>パネルは別 origin でシェルから DOM に触れないので、名乗りは bridge.js が postMessage で渡し、描くのはシェル。</summary>
    [Fact]
    public async Task APanelInputOffersTheSameEditingItems()
    {
        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();

        var url = Page.FrameLocator("iframe.view[data-id='videourl']").Locator("#url");
        await Assertions.Expect(url).ToBeVisibleAsync();

        await RightClickAsync(url);

        Assert.Equal(["host cut", "host copy", "host paste"], await ItemIdsAsync());
    }

    /// <summary>iframe の位置を足し忘れても DOM には出ているので、押した点の真下に出ているかまで見る。</summary>
    [Fact]
    public async Task TheMenuOpensOverThePanelNotTheCornerOfTheWindow()
    {
        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();

        var url = Page.FrameLocator("iframe.view[data-id='videourl']").Locator("#url");
        await Assertions.Expect(url).ToBeVisibleAsync();

        // Playwright の箱は iframe の中の要素でも窓の座標。
        var box = await url.BoundingBoxAsync();
        Assert.NotNull(box);

        await RightClickAsync(url);
        await Assertions.Expect(Menu).ToBeVisibleAsync();

        var menu = await Menu.BoundingBoxAsync();
        Assert.NotNull(menu);

        // iframe の矩形の中かどうかだけでは足し忘れを捕まえられないので、押した点の真下に出ているかを見る。
        var clickX = box.X + (box.Width / 2);
        var clickY = box.Y + (box.Height / 2);

        Assert.True(Math.Abs(menu.X - clickX) < 8, $"横が {clickX} ではなく {menu.X}");
        Assert.True(Math.Abs(menu.Y - clickY) < 8, $"縦が {clickY} ではなく {menu.Y}");
    }

    /// <summary>パネルの中は bridge.js が置き換える（シェルからは別 origin の DOM に触れない）ので、押して入るところまで見る。</summary>
    [Fact]
    public async Task PastingWorksInsideAPanelToo()
    {
        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();

        var url = Page.FrameLocator("iframe.view[data-id='videourl']").Locator("#url");
        await Assertions.Expect(url).ToBeVisibleAsync();

        try
        {
            await Page.EvaluateAsync("() => bridge.copyToClipboard('https://e2e.test/pasted')");

            await url.FillAsync("xxxxxx");
            await url.EvaluateAsync("field => field.setSelectionRange(0, 6)");

            await RightClickAsync(url);
            await Assertions.Expect(Menu).ToBeVisibleAsync();

            await url.EvaluateAsync("field => field.setSelectionRange(0, 0)");
            await Menu.Locator("button[data-id='host paste']").ClickAsync();

            await ExpectPastedAsync(url, "https://e2e.test/pasted");
        }
        finally
        {
            await url.FillAsync("");
        }
    }

    /// <summary>iframe の中の mousedown はシェルの窓に届かない。閉じたらパネルの頼みにも返事が返ることを見る（返らないと右クリックの処理が待ったまま残る）。</summary>
    [Fact]
    public async Task AClickElsewhereInThePanelClosesTheMenu()
    {
        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();

        var frame = Page.FrameLocator("iframe.view[data-id='videourl']");
        var url = frame.Locator("#url");
        await Assertions.Expect(url).ToBeVisibleAsync();
        var settled = await SettledAsync(url);

        await RightClickAsync(url);
        await Assertions.Expect(Menu).ToBeVisibleAsync();

        await frame.Locator("body").ClickAsync(new() { Position = new Position { X = 4, Y = 4 } });

        await Assertions.Expect(Menu).ToBeHiddenAsync();
        await ExpectSettledAsync(url, settled + 1);
    }

    /// <summary>メニューキー・Shift+F10 は mousedown が来ない。右クリックし直すだけだと mousedown で前のメニューが先に閉じるので、mousedown 無しの contextmenu を置く。</summary>
    [Fact]
    public async Task OpeningAnotherMenuWithoutAClickSettlesTheFirst()
    {
        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();

        var url = Page.FrameLocator("iframe.view[data-id='videourl']").Locator("#url");
        await Assertions.Expect(url).ToBeVisibleAsync();
        var settled = await SettledAsync(url);

        await RightClickAsync(url);
        await Assertions.Expect(Menu).ToBeVisibleAsync();

        var before = await TurnsAsync();
        await url.EvaluateAsync(
            "field => { const box = field.getBoundingClientRect(); field.dispatchEvent(new MouseEvent('contextmenu', "
            + "{ bubbles: true, cancelable: true, clientX: box.left + 4, clientY: box.top + 4 })); }");
        await Page.WaitForFunctionAsync($"() => (window.__stkCtx?.turns ?? 0) > {before}");

        await Assertions.Expect(Menu).ToBeVisibleAsync();
        await ExpectSettledAsync(url, settled + 1);

    }

    /// <summary>パネル内のキーはシェルの窓に届かないので bridge.js が Escape を渡す。渡した Escape はパネルには届けず、閉じたあとの Escape はパネルのもの。</summary>
    [Fact]
    public async Task EscapeInsideAPanelClosesItsMenuAndOnlyItsMenu()
    {
        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();

        var url = Page.FrameLocator("iframe.view[data-id='videourl']").Locator("#url");
        await Assertions.Expect(url).ToBeVisibleAsync();
        var settled = await SettledAsync(url);

        await url.EvaluateAsync("""
            _ => {
              window.__e2eEscapes = [];
              if (window.__e2eEscapeListener) return;
              window.__e2eEscapeListener = true;
              window.addEventListener('keydown', e => {
                if (e.key === 'Escape') window.__e2eEscapes.push(e.defaultPrevented);
              });
            }
            """);

        await RightClickAsync(url);
        await Assertions.Expect(Menu).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(Menu).ToBeHiddenAsync();
        await ExpectSettledAsync(url, settled + 1);
        Assert.Equal(0, await url.EvaluateAsync<int>("_ => window.__e2eEscapes.length"));

        await Page.Keyboard.PressAsync("Escape");
        var escapes = await url.EvaluateAsync<bool[]>("_ => window.__e2eEscapes");
        Assert.Equal([false], escapes);
    }

    /// <summary>変換中の Escape は IME のものなので、シェルへ渡さない。</summary>
    [Fact]
    public async Task AnEscapeWhileComposingLeavesThePanelMenuOpen()
    {
        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();

        var url = Page.FrameLocator("iframe.view[data-id='videourl']").Locator("#url");
        await Assertions.Expect(url).ToBeVisibleAsync();

        await RightClickAsync(url);
        await Assertions.Expect(Menu).ToBeVisibleAsync();
        var settled = await SettledAsync(url);

        // シェルが受け終わるのを待ってから見る（パネルからの知らせは順に届くので、後の getMode の返事が返れば前の知らせは片付いている）。
        await url.EvaluateAsync("""
            async field => {
              field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', isComposing: true, bubbles: true, cancelable: true }));
              await spoitable.call('getMode').catch(() => null);
            }
            """);

        await Assertions.Expect(Menu).ToBeVisibleAsync();
        Assert.Equal(settled, await SettledAsync(url));
    }

    /// <summary>Locator の evaluate は 1 つ目の引数に要素が来る。</summary>
    private static Task<int> SettledAsync(ILocator inPanel) => inPanel.EvaluateAsync<int>("_ => window.__stkCtxSettled ?? 0");

    private static async Task ExpectSettledAsync(ILocator inPanel, int count)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        var now = 0;

        while (waited.Elapsed < TimeSpan.FromSeconds(5))
        {
            now = await SettledAsync(inPanel);
            if (now >= count) break;
            await Task.Delay(50);
        }

        Assert.Equal(count, now);
    }

    /// <summary>ホストが考え終わるまで待つ（待たずに見ると「まだ出ていない」だけを掴む）。</summary>
    private async Task RightClickAsync(ILocator target)
    {
        var before = await TurnsAsync();

        await target.ClickAsync(new() { Button = MouseButton.Right });
        await Page.WaitForFunctionAsync($"() => (window.__stkCtx?.turns ?? 0) > {before}");
    }

    private async Task RightClickBackgroundAsync()
    {
        var before = await TurnsAsync();

        await Page.Locator("#viewLog").ClickAsync(new()
        {
            Button = MouseButton.Right,
            Position = new Position { X = 4, Y = 4 },
        });

        await Page.WaitForFunctionAsync($"() => (window.__stkCtx?.turns ?? 0) > {before}");
    }

    private Task<int> TurnsAsync() => Page.EvaluateAsync<int>("() => window.__stkCtx?.turns ?? 0");

    /// <summary>文言で見ると、言語を変える試験の後ろで走ったときに落ちるので印で見る。</summary>
    private async Task<string[]> ItemIdsAsync() =>
        [.. await Menu.Locator("button").EvaluateAllAsync<string[]>("list => list.map(b => b.dataset.id)")];

    /// <summary>面の先頭は他の試験が入れた行かもしれず、単独で走らせると 1 本も無いので掴まない。</summary>
    private async Task InjectRowAsync(string title)
    {
        await Assertions.Expect(Page.Locator("#rowsVrc")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await Page.EvaluateAsync($$"""
            () => {
              // **見た印は戻す。** 大きい id を残すと、後続の試験で本物の行が
              // 「もう見た」と判定されて出なくなる。
              const saved = seam.vrc.seen;
              try {
                stk.receive('logEvents', [{
                  id: {{InjectedId}}, kind: 'PlayerJoined', ts: new Date().toISOString(),
                  title: {{title}}, detail: '',
                }]);
              } finally {
                seam.vrc.seen = saved;
              }
            }
            """);
    }

    private Task RemoveInjectedRowAsync() => Page.EvaluateAsync(
        $"() => document.querySelector(\"#rowsVrc .row[data-id='{InjectedId}']\")?.remove()");
}
