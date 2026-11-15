using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Shell;

public sealed class ShellTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private IPage Page => app.Page;

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>畳んだ種別はホスト側に残るので、DisposeAsync で戻す。</summary>
    public async Task DisposeAsync()
    {
        // raw も既定で畳む側。
        await Page.EvaluateAsync("() => bridge.setHiddenKinds(JSON.stringify(['ApiRequest', 'raw']))");
        await Page.EvaluateAsync(
            "() => bridge.applyConfigChanges(JSON.stringify({ hrs: { Device: '" + AppFixture.NoHeartRateMonitor + "' } }))");
        await Page.EvaluateAsync(
            "() => bridge.applyConfigChanges(JSON.stringify({ host: { Theme: 'dark' } }))");

        // 触りかけの変更が次の試験の「適用」に混ざらないように落とす。
        if (await Page.Locator("#cfgApply").IsVisibleAsync()) await Page.Locator("#cfgCancel").ClickAsync();

        // 絞り込みの窓を開いたままにすると後ろの行が押せない。
        if (await Page.Locator("#filterModal").IsVisibleAsync()) await Page.Locator("#filterClose").ClickAsync();
    }

    /// <summary>パネルの実体が無いものを出さない側は unit で見る。同梱のモジュールは 2 つともパネルを持つので、ここでは負の側を作れない。</summary>
    [Fact]
    public async Task OnlyModulesWithAPanelAppearInTheSidebar()
    {
        var items = Page.Locator("#sidebar .item");

        await Assertions.Expect(items).ToHaveCountAsync(6);
        await Assertions.Expect(items.Nth(0)).ToHaveAttributeAsync("data-id", "dashboard");
        await Assertions.Expect(items.Nth(1)).ToHaveAttributeAsync("data-id", "log");
        await Assertions.Expect(items.Nth(2)).ToHaveAttributeAsync("data-id", "hrs");
        await Assertions.Expect(items.Nth(3)).ToHaveAttributeAsync("data-id", "videourl");
        await Assertions.Expect(items.Nth(4)).ToHaveAttributeAsync("data-id", "moduleManager");
        await Assertions.Expect(items.Nth(5)).ToHaveAttributeAsync("data-id", "settings");
    }

    [Fact]
    public async Task TheSidebarFoldsToARailThatStillTakesItsPlace()
    {
        await Page.EvaluateAsync("() => selectView('log')");

        await Page.WaitForFunctionAsync("() => !document.body.classList.contains('rail')");
        await Assertions.Expect(Page.Locator("#sidebar .item .lbl").First).ToBeVisibleAsync();
        var open = await SettledWidth();

        try
        {
            await Page.Locator("#sidebar .railbtn").ClickAsync();
            var rail = await SettledWidth();

            Assert.True(rail < open / 2, $"畳んでも細くならない（{open} → {rail}）");

            await Assertions.Expect(Page.Locator("#sidebar .item .lbl").First).Not.ToBeVisibleAsync();

            var views = await Box(".views");
            Assert.True(Math.Abs(views!.X - rail) <= 1, $"本文が帯の右から始まっていない（帯 {rail}、本文 {views.X}）");

            await Page.Locator("#sidebar .item[data-id='settings']").ClickAsync();
            await Assertions.Expect(Page.Locator("#viewSettings")).ToBeVisibleAsync();
        }
        finally
        {
            await Page.EvaluateAsync("() => { if (document.body.classList.contains('rail')) document.querySelector('#sidebar .railbtn').click(); selectView('log'); }");
        }

        Assert.Equal(open, await SettledWidth());
    }

    private const string Marks = """
        () => {
          const box = el => { const r = el.getBoundingClientRect(); return [r.left, r.top, r.width, r.height].map(v => Math.round(v * 10) / 10).join(' '); };
          const all = (name, selector) => [...document.querySelectorAll(selector)].map(el => name + ' ' + box(el));
          return [
            ...all('icon', '#sidebar .item .ico'),
            ...all('rule', '#sidebar .heading .rule'),
            ...all('mark', '#sidebar .brand .mark'),
            ...all('fold', '#sidebar .railbtn .ic'),
          ];
        }
        """;

    /// <summary>名前は読み上げのために aria-label に残す（title は出さない）。</summary>
    [Fact]
    public async Task FoldingTheSidebarKeepsEveryRowWhereItWas()
    {
        await Page.EvaluateAsync("() => selectView('log')");
        await Page.WaitForFunctionAsync("() => !document.body.classList.contains('rail')");

        const string Rows = "() => [...document.querySelectorAll('#sidebar .item')].map(b => { const r = b.getBoundingClientRect(); return b.dataset.id + ' ' + Math.round(r.top) + ' ' + Math.round(r.height); })";
        var open = await Page.EvaluateAsync<string[]>(Rows);

        await Assertions.Expect(Page.Locator("#sidebar .heading .rule").First).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#sidebar .heading .label").First).ToBeVisibleAsync();

        await Assertions.Expect(Page.Locator("#sidebar .foot #version")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#version")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^v\d"));
        Assert.True(await Page.EvaluateAsync<bool>("() => { const v = document.getElementById('version'); return v.scrollWidth <= v.clientWidth; }"),
            "版の表記が切れている");

        var openMarks = await Page.EvaluateAsync<string[]>(Marks);

        Assert.Equal("", await Page.EvaluateAsync<string>("""
            () => {
              const rect = selector => document.querySelector(selector).getBoundingClientRect();
              const icon = rect('#sidebar .item .ico');
              const middle = r => r.left + r.width / 2;
              const off = [];
              const rule = rect('#sidebar .heading .rule');
              if (Math.abs(rule.left - icon.left) > .25 || Math.abs(rule.width - icon.width) > .25) off.push(`rule ${rule.left}+${rule.width} / icon ${icon.left}+${icon.width}`);
              for (const name of ['.brand .mark', '.railbtn .ic']) {
                const r = rect('#sidebar ' + name);
                if (Math.abs(middle(r) - middle(icon)) > .25) off.push(`${name} ${middle(r)} / icon ${middle(icon)}`);
              }
              return off.join('; ');
            }
            """));

        try
        {
            var moved = await Page.EvaluateAsync<string[]>(
                $"() => {{ document.querySelector('#sidebar .railbtn').click(); return ({Marks})(); }}");
            Assert.Equal(openMarks, moved);

            await SettledWidth();

            var rail = await Page.EvaluateAsync<string[]>(Rows);

            Assert.True(open.Length >= 4, $"行が足りない（{open.Length}）");
            Assert.Equal(open, rail);

            Assert.Equal(openMarks, await Page.EvaluateAsync<string[]>(Marks));

            // 帯の幅は右の線を除いて小数のまま測る（clientWidth は整数に丸めるので 0.4px ほどのずれが隠れる）。
            var centre = await Page.EvaluateAsync<double>("""
                () => {
                  const s = document.getElementById('sidebar');
                  const box = s.getBoundingClientRect();
                  const inner = box.width - parseFloat(getComputedStyle(s).borderRightWidth);
                  const i = s.querySelector('.item .ico').getBoundingClientRect();
                  return i.left - box.left + i.width / 2 - inner / 2;
                }
                """);
            Assert.True(Math.Abs(centre) <= .25, $"畳んだ帯の真ん中にアイコンが来ていない（{centre:0.00}px ずれ）");

            await Assertions.Expect(Page.Locator("#sidebar .heading .rule").First).ToBeVisibleAsync();
            await Assertions.Expect(Page.Locator("#sidebar .heading .label").First).Not.ToBeVisibleAsync();

            await Assertions.Expect(Page.Locator("#version")).Not.ToBeVisibleAsync();

            var named = await Page.EvaluateAsync<string[]>(
                "() => [...document.querySelectorAll('#sidebar .item')].map(b => (b.title ? 'title:' : '') + (b.getAttribute('aria-label') || ''))");
            Assert.All(named, n => Assert.False(n.StartsWith("title:", StringComparison.Ordinal) || n.Length == 0, $"名前の付き方が違う: '{n}'"));
            Assert.Contains("設定", named);
        }
        finally
        {
            await Page.EvaluateAsync("() => { if (document.body.classList.contains('rail')) document.querySelector('#sidebar .railbtn').click(); selectView('log'); }");
        }
    }

    [Fact]
    public async Task TheRailIsRememberedAcrossReloads()
    {
        await Page.EvaluateAsync("() => selectView('log')");

        try
        {
            await Page.Locator("#sidebar .railbtn").ClickAsync();
            await Page.WaitForFunctionAsync("() => document.body.classList.contains('rail')");

            await Page.ReloadAsync();
            await Assertions.Expect(Page.Locator("#sidebar .item[data-id='log']")).ToBeVisibleAsync(new() { Timeout = 15_000 });

            Assert.True(await Page.EvaluateAsync<bool>("() => document.body.classList.contains('rail')"), "読み込み直したら開いた形に戻った");
        }
        finally
        {
            await Page.EvaluateAsync("() => { if (document.body.classList.contains('rail')) document.querySelector('#sidebar .railbtn').click(); }");
        }
    }

    /// <summary>バーは窓に固定して浮いているので、窓の真ん中に置くと窓が狭いときサイドバーの上に出て項目が押せなくなる。本文の真ん中に置く。</summary>
    [Fact]
    public async Task TheApplyBarDoesNotCoverTheSidebar()
    {
        // ページは画面を離れても覚えているので、前の試験が開いたページのままのことがある。
        await app.OpenSettings("notify");

        // 変更を 1 つ作らないと「適用」のバーが出ない。取り消しは DisposeAsync が持つ。
        var seconds = Page.Locator("#viewSettings .cfg-item[data-key='ConfirmSeconds'] input[type=number]");

        // 固定値だとたまたま同じだった回に変更が作られないので、いまの値からずらす（上限 30 に当たらない向きへ）。
        var now = int.TryParse(await seconds.InputValueAsync(), out var value) ? value : 0;

        await seconds.FillAsync((now >= 30 ? now - 1 : now + 1).ToString());
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeVisibleAsync();

        foreach (var rail in new[] { false, true })
        {
            if (rail) await Page.Locator("#sidebar .railbtn").ClickAsync();
            await SettledWidth();

            var (bar, side, views) = (await Box("#cfgApply"), await Box("#sidebar"), await Box("#views"));
            Assert.True(bar!.X >= side!.X + side.Width,
                $"「適用」のバーがサイドバーに被さっている（rail={rail}: バー {bar.X}、サイドバーの右端 {side.X + side.Width}）");

            // 被さらないだけだと、窓が広い試験機では窓の真ん中に置いても通る。
            var (barMiddle, viewsMiddle) = (bar.X + bar.Width / 2, views!.X + views.Width / 2);
            Assert.True(Math.Abs(barMiddle - viewsMiddle) <= 2,
                $"「適用」のバーが本文の真ん中に無い（rail={rail}: バー {barMiddle}、本文 {viewsMiddle}）");
        }

        await Page.EvaluateAsync("() => { if (document.body.classList.contains('rail')) document.querySelector('#sidebar .railbtn').click(); selectView('log'); }");
    }

    /// <summary>開閉は transition で動くので、2 回続けて同じ値になるまで待つ。</summary>
    private async Task<double> SettledWidth()
    {
        double last = -1;
        for (var i = 0; i < 40; i++)
        {
            var width = await Page.EvaluateAsync<double>("() => document.querySelector('#sidebar').getBoundingClientRect().width");
            if (Math.Abs(width - last) < 0.5) return width;
            last = width;
            await Task.Delay(60);
        }
        return last;
    }

    private Task<LocatorBoundingBoxResult?> Box(string selector) =>
        Page.Locator(selector).BoundingBoxAsync();

    /// <summary>心拍計は要らない。getStatus が空を返しても通らないよう、試験から入れた値が画面に出るところまで見る。</summary>
    [Fact]
    public async Task TheHeartRatePanelShowsItsCurrentState()
    {
        const string device = "PanelProbe [A1:B2:C3:D4:E5:F6]";

        await Page.EvaluateAsync(
            "() => bridge.applyConfigChanges(JSON.stringify({ hrs: { Device: '" + device + "' } }))");

        await Page.Locator("#sidebar .item[data-id='hrs']").ClickAsync();

        var panel = Page.FrameLocator("iframe.view[data-id='hrs']");

        await Assertions.Expect(panel.Locator("#device")).ToContainTextAsync(device);

        await Assertions.Expect(panel.Locator("#devices > *")).Not.ToHaveCountAsync(0);

        // 繋がっているかは見ない（近くに心拍計があるかで変わる）。
    }

    [Fact]
    public async Task PickingAModuleOpensAnIframeOnItsOwnOrigin()
    {
        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();

        var frame = Page.Locator("iframe.view[data-id='videourl']");
        await Assertions.Expect(frame).ToHaveAttributeAsync("src", "spoitable://videourl/index.html");
        await Assertions.Expect(frame).ToBeVisibleAsync();

        await Assertions.Expect(Page.FrameLocator("iframe.view[data-id='videourl']").Locator("h1")).ToHaveTextAsync("動画 URL");
    }

    [Fact]
    public async Task ACallFromThePanelReachesTheModuleAndComesBack()
    {
        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();
        var panel = Page.FrameLocator("iframe.view[data-id='videourl']");

        // 出るメッセージは、別 ALC のモジュールが投げた例外が iframe まで戻ってきたもの。
        await panel.Locator("#url").FillAsync("not-a-url");
        await panel.Locator("#go").ClickAsync();

        var message = panel.Locator("#msg");
        await Assertions.Expect(message).ToHaveClassAsync("msg bad");
        await Assertions.Expect(message).ToContainTextAsync("http/https");
    }

    [Fact]
    public async Task YouCanGetBackToTheLogScreen()
    {
        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();
        await Page.Locator("#sidebar .item[data-id='log']").ClickAsync();

        await Assertions.Expect(Page.Locator("#viewLog")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("iframe.view[data-id='videourl']")).Not.ToBeVisibleAsync();

        // 一度開いた iframe は残す（開き直すたびにモジュール側の状態が飛ばないように）。
        await Assertions.Expect(Page.Locator("iframe.view[data-id='videourl']")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task RoutingFollowsTheSenderNotTheClaimedId()
    {
        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();
        await Assertions.Expect(Page.Locator("iframe.view[data-id='videourl']")).ToBeVisibleAsync();

        // シェルは発信元 origin から宛先を決めるので、hrs を名乗っても videourl の口に届く（知らない method で失敗する）。
        var reply = await Page.FrameLocator("iframe.view[data-id='videourl']").Locator("body").EvaluateAsync<string>(
            """
            () => new Promise(resolve => {
              window.addEventListener('message', e => resolve(JSON.stringify(e.data)), { once: true });
              parent.postMessage(
                { stk: 1, id: 999, kind: 'module', module: 'hrs', method: '__spoofed', payload: {} },
                'spoitable://ui');
            })
            """);

        Assert.Contains("__spoofed", reply);
        Assert.DoesNotContain("hrs", reply);
    }

    /// <summary>bind がブラウザ単位でフレーム単位ではないので、bind は止められず token で呼び出しを止めている。見るのは「bind が失敗すること」ではなく「呼び出しが断られること」。</summary>
    [Fact]
    public async Task AModuleCanBindStkHostButCannotCallIt()
    {
        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();
        await Assertions.Expect(Page.Locator("iframe.view[data-id='videourl']")).ToBeVisibleAsync();

        var measured = await Page.FrameLocator("iframe.view[data-id='videourl']").Locator("body")
            .EvaluateAsync<string>(
                """
                () => (async () => {
                  const out = { origin: location.origin, cefsharp: typeof CefSharp };

                  try { await CefSharp.BindObjectAsync('stkHost'); out.bound = typeof window.stkHost; }
                  catch (e) { out.bound = 'bindFailed'; }

                  // token なし。
                  try { await window.stkHost.getStatus(); out.noToken = 'ALLOWED'; }
                  catch (e) { out.noToken = 'rejected'; }

                  // 引数の数は合っていて中身だけ違う。**ここが本丸** — token を見ていなければ通る。
                  const fake = '0'.repeat(64);
                  try { await window.stkHost.getStatus(fake); out.wrongToken = 'ALLOWED'; }
                  catch (e) { out.wrongToken = 'rejected'; }

                  // ログの DB。取れると「誰といつ会ったか」が読める。
                  try { await window.stkHost.getRecentEvents(fake, 5, null); out.recentEvents = 'ALLOWED'; }
                  catch (e) { out.recentEvents = 'rejected'; }

                  // 他モジュール宛の直接呼び出し（シェルを迂回すると名乗りが効いていた経路）。
                  try { await window.stkHost.callModule(fake, 'hrs', 'getStatus', '{}'); out.callOther = 'ALLOWED'; }
                  catch (e) { out.callOther = 'rejected'; }

                  // シェルの HTML を直接読めない (token はそこに埋め込まれて配られる)。
                  // 別 origin なので CORS で塞がる — このハンドラは ACAO を出さない。
                  try {
                    const response = await fetch('spoitable://ui/index.html');
                    out.readShellHtml = response.ok ? 'ALLOWED' : `status${response.status}`;
                  } catch (e) { out.readShellHtml = 'blocked'; }
                  // token はここには置かれていない。
                  out.tokenHere = typeof window.__stkToken;

                  // シェルからも読めない (同一生成元ポリシー)。**これが成立の根拠。**
                  try { out.tokenFromParent = typeof parent.__stkToken; }
                  catch (e) { out.tokenFromParent = 'blocked'; }

                  return JSON.stringify(out);
                })()
                """);

        var result = JsonNode.Parse(measured)!.AsObject();

        Assert.Equal("spoitable://videourl", (string?)result["origin"]);

        // bind は通ったまま。塞いだのは呼び出しであって bind ではない。
        Assert.Equal("object", (string?)result["bound"]);

        Assert.Equal("rejected", (string?)result["noToken"]);
        Assert.Equal("rejected", (string?)result["wrongToken"]);
        Assert.Equal("rejected", (string?)result["recentEvents"]);
        Assert.Equal("rejected", (string?)result["callOther"]);

        Assert.Equal("blocked", (string?)result["readShellHtml"]);

        Assert.Equal("undefined", (string?)result["tokenHere"]);
        Assert.Equal("blocked", (string?)result["tokenFromParent"]);

        // シェルは受け取ったら消す（あとからメインフレームで動く何かに拾わせない）。
        Assert.Equal("undefined", await Page.EvaluateAsync<string>("() => typeof window.__stkToken"));
    }

    /// <summary>spoitable:// が fetch を許さないだけでも「シェルの HTML を読めない」は通るので、同じ origin の iframe に入口を開いて、配られた中身を読む。</summary>
    [Fact]
    public async Task AnEntryOpenedInAnInnerFrameComesWithoutTheToken()
    {
        var script = await Page.EvaluateAsync<string>(
            """
            () => new Promise((resolve, reject) => {
              const frame = document.createElement('iframe');

              // 中のスクリプトは走らせない（読むだけ）。同じ origin なので親から中身を読める。
              frame.sandbox = 'allow-same-origin';
              frame.style.display = 'none';
              frame.onload = () => {
                try { resolve(frame.contentDocument.querySelector('script').textContent); }
                catch (e) { reject(e); }
                finally { frame.remove(); }
              };
              frame.src = '/wrist.html';
              document.body.appendChild(frame);
            })
            """);

        Assert.Contains("__STK_TOKEN__", script);
    }
    /// <summary>畳んだ種別は設定画面には出さない（読みながら切り替えるものなので）。</summary>
    [Fact]
    public async Task CollapsedKindsArePersistedInTheHostSettings()
    {
        await Page.Locator("#sidebar .item[data-id='log']").ClickAsync();
        await Page.Locator("#logFilter").ClickAsync();

        // 印で掴む（文字だと件数の札まで含む）。
        var item = Page.Locator("#filters .fitem[data-kind='PlayerJoined']");
        await item.Locator("input.spoitable-switch").ClickAsync();

        // 押した先を先に見る（ファイルだけを待つと、違う項目を押しても「保存が届かない」としか見えない）。
        await Assertions.Expect(item).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("(^| )off( |$)"));
        await ExpectHiddenKinds(kinds => kinds.Contains("PlayerJoined"));

        await item.Locator("input.spoitable-switch").ClickAsync();
        await Assertions.Expect(item).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("(^| )off( |$)"));
        await ExpectHiddenKinds(kinds => !kinds.Contains("PlayerJoined"));
    }

    /// <summary>ThemeTokenTests はソースのテキストを読むだけで配信の失敗を捕まえられない。_host/ は URL のホストに関わらずシェルの web/ から配られるので、別 origin のパネルでも同一 origin になる。値そのものは書かない（空でないことと、シェルとパネルで同じであることを見る）。</summary>
    [Fact]
    public async Task TheTokenReachesBothTheShellAndThePanels()
    {
        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();
        await Assertions.Expect(Page.Locator("iframe.view[data-id='videourl']")).ToBeVisibleAsync();

        const string read =
            """
            () => {
              const root = getComputedStyle(document.documentElement);
              return JSON.stringify({
                accent: root.getPropertyValue('--accent').trim(),
                fg: root.getPropertyValue('--fg').trim(),
                // 暗色の節に無いトークン。accent や fg だけを見ていると、
                // 明るい側の :root が丸ごと壊れても、暗色の節が肩代わりして気付けない。
                warn: root.getPropertyValue('--warn').trim(),
                body: getComputedStyle(document.body).backgroundColor,
              });
            }
            """;

        var shell = JsonNode.Parse(await Page.EvaluateAsync<string>(read))!;
        var panel = JsonNode.Parse(
            await Page.FrameLocator("iframe.view[data-id='videourl']").Locator("body")
                .EvaluateAsync<string>(read))!;

        Assert.NotEqual(string.Empty, (string?)shell["accent"]);
        Assert.NotEqual(string.Empty, (string?)panel["accent"]);
        Assert.NotEqual(string.Empty, (string?)shell["warn"]);
        Assert.NotEqual(string.Empty, (string?)panel["warn"]);

        Assert.Equal((string?)shell["accent"], (string?)panel["accent"]);
        Assert.Equal((string?)shell["fg"], (string?)panel["fg"]);

        Assert.NotEqual("rgba(0, 0, 0, 0)", (string?)panel["body"]);
    }

    /// <summary>パネルは別 origin の別 document なので、シェルは &lt;link&gt; を張り直し、パネルは読み込み直され、配信側は溜め込ませない。色の値は書かない。</summary>
    [Fact]
    public async Task SwitchingThemesChangesBothTheShellAndThePanels()
    {
        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();
        await Assertions.Expect(Page.Locator("iframe.view[data-id='videourl']")).ToBeVisibleAsync();

        var before = await BackgroundOfShell();

        var applied = await Page.EvaluateAsync<string>(
            "async () => JSON.stringify(await bridge.applyConfigChanges(JSON.stringify({ host: { Theme: 'light' } })))");

        // 張り直しと読み込み直しは非同期に走るので、変わるまで待つ。
        try
        {
            await Page.WaitForFunctionAsync(
                "expected => getComputedStyle(document.body).backgroundColor !== expected", before, new() { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            var link = await Page.EvaluateAsync<string>("() => document.querySelector('link[href^=\"/_host/theme.css\"]')?.href ?? '(none)'");
            Assert.Fail($"シェルの配色が変わらない: 前={before} 適用の返事={applied} theme.css={link}");
        }

        var shell = await BackgroundOfShell();
        Assert.NotEqual(before, shell);

        await ExpectPanelToFollow(shell);

        // 戻したことを確かめてから終わる（DisposeAsync 任せだと次の試験が途中状態を見る）。
        await Page.EvaluateAsync(
            "() => bridge.applyConfigChanges(JSON.stringify({ host: { Theme: 'dark' } }))");
        await Page.WaitForFunctionAsync(
            "expected => getComputedStyle(document.body).backgroundColor === expected", before);
        await ExpectPanelToFollow(before);
    }

    /// <summary>ready の前に送った知らせは消えるので、シェルが覚えて ready で渡す。読み込みの速さに任せると運で緑になるので、読み込み中に戻してから切り替え、ready を手で送る。</summary>
    [Fact]
    public async Task APanelStillLoadingCatchesUpWithTheTheme()
    {
        var dark = await OpenThemedPanel();

        try
        {
            await Page.EvaluateAsync("() => { panels.get('videourl').ready = false; }");
            await Page.EvaluateAsync("() => bridge.applyConfigChanges(JSON.stringify({ host: { Theme: 'light' } }))");
            await Page.WaitForFunctionAsync("expected => getComputedStyle(document.body).backgroundColor !== expected", dark);
            var light = await BackgroundOfShell();

            // 読み込み中のパネルには知らせを送らない。ここが崩れると、この試験は何も見ていない。
            await Task.Delay(300);
            Assert.Equal(dark, await BackgroundOfPanel());

            await PanelFrame().EvaluateAsync("() => window.spoitable.ready()");
            await ExpectPanelToFollow(light);
        }
        finally
        {
            await Page.EvaluateAsync("() => { panels.get('videourl').ready = true; }");
            await Page.EvaluateAsync("() => bridge.applyConfigChanges(JSON.stringify({ host: { Theme: 'dark' } }))");
            await ExpectPanelToFollow(dark);
        }
    }

    /// <summary>張り直しの URL の番号は読み込みごとに 1 から数え直すので、前に取った同じ URL（前の配色のまま CEF に残る）とぶつかると変わらない。パネルに「1 = 暗 / 2 = 明 / 3 = 暗」を取らせてから読み込み直す。</summary>
    [Fact]
    public async Task AReloadedPanelStillFollowsTheTheme()
    {
        var dark = await OpenThemedPanel();

        try
        {
            await Page.EvaluateAsync("() => broadcastToPanels('configChanged', {})");
            await Page.EvaluateAsync("() => bridge.applyConfigChanges(JSON.stringify({ host: { Theme: 'light' } }))");
            await Page.WaitForFunctionAsync("expected => getComputedStyle(document.body).backgroundColor !== expected", dark);
            var light = await BackgroundOfShell();
            await ExpectPanelToFollow(light);

            await Page.EvaluateAsync("() => bridge.applyConfigChanges(JSON.stringify({ host: { Theme: 'dark' } }))");
            await ExpectPanelToFollow(dark);

            try
            {
                await PanelFrame().EvaluateAsync("() => { window.__beforeReload = true; location.reload(); }");
            }
            catch (PlaywrightException)
            {
                // reload で実行コンテキストが消えることがある。
            }

            await PanelFrame().WaitForFunctionAsync(
                "() => window.__panelReady === true && window.__beforeReload === undefined",
                null, new FrameWaitForFunctionOptions { Timeout = 20_000 });

            await Page.EvaluateAsync("() => bridge.applyConfigChanges(JSON.stringify({ host: { Theme: 'light' } }))");
            await ExpectPanelToFollow(light);
        }
        finally
        {
            await Page.EvaluateAsync("() => bridge.applyConfigChanges(JSON.stringify({ host: { Theme: 'dark' } }))");
            await ExpectPanelToFollow(dark);
        }
    }

    private async Task<string> OpenThemedPanel()
    {
        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();
        await Assertions.Expect(Page.Locator("iframe.view[data-id='videourl']")).ToBeVisibleAsync();

        var element = await Page.Locator("iframe.view[data-id='videourl']").ElementHandleAsync();
        _panelFrame = await element.ContentFrameAsync() ?? throw new InvalidOperationException("動画のパネルに frame が無い");

        await PanelFrame().WaitForFunctionAsync("() => window.__panelReady === true", null,
            new FrameWaitForFunctionOptions { Timeout = 20_000 });

        var dark = await BackgroundOfShell();
        await ExpectPanelToFollow(dark);
        return dark;
    }

    /// <summary>URL で探さない（iframe が見えた直後はまだ URL が付いていないことがある）。要素から直に取れば、読み込み直しても同じ frame が返る。</summary>
    private IFrame PanelFrame() => _panelFrame ?? throw new InvalidOperationException("OpenThemedPanel を先に呼ぶ");

    private IFrame? _panelFrame;

    private async Task ExpectPanelToFollow(string expected)
    {
        for (var attempt = 0; ; attempt++)
        {
            var panel = await BackgroundOfPanel();
            if (panel == expected) return;

            Assert.True(attempt < 100, $"パネルがテーマに追従しなかった: パネル={panel} 期待={expected}");
            await Task.Delay(100);
        }
    }

    private Task<string> BackgroundOfShell() =>
        Page.EvaluateAsync<string>("() => getComputedStyle(document.body).backgroundColor");

    private Task<string> BackgroundOfPanel() =>
        Page.FrameLocator("iframe.view[data-id='videourl']").Locator("body")
            .EvaluateAsync<string>("() => getComputedStyle(document.body).backgroundColor");

    /// <summary>件数の札があるので applyFilter() はイベントのたびに走る。全部差し替えると押す直前に別の要素になり、件数の桁が増えて並びが動くと狙った隣を押す。同じ要素のままかを印で見る。</summary>
    [Fact]
    public async Task TheFilterItemsAreNotRebuiltWhenEventsArrive()
    {
        await Page.Locator("#sidebar .item[data-id='log']").ClickAsync();
        await Page.Locator("#logFilter").ClickAsync();

        var item = Page.Locator("#filters .fitem[data-kind='PlayerJoined']");
        await Assertions.Expect(item).ToBeVisibleAsync();

        await item.EvaluateAsync("node => node.dataset.mark = 'before'");
        var world = Page.Locator("#filters .fitem[data-kind='WorldJoined'] .n");
        var before = await world.TextContentAsync();

        try
        {
            await Page.EvaluateAsync("""
                () => {
                  const saved = seam.vrc.seen;
                  try {
                    for (let i = 0; i < 12; i++) {
                      stk.receive('logEvents', [{
                        id: 990600 + i, kind: 'WorldJoined', ts: new Date().toISOString(),
                        title: 'e2e-chip-' + i, detail: '',
                      }]);
                    }
                  } finally {
                    seam.vrc.seen = saved;
                  }
                }
                """);

            await Assertions.Expect(world).Not.ToHaveTextAsync(before ?? "");

            Assert.Equal("before", await item.GetAttributeAsync("data-mark"));
        }
        finally
        {
            await Page.EvaluateAsync(
                "() => document.querySelectorAll(\"#rowsVrc .row\").forEach(r => { if (+r.dataset.id >= 990600 && +r.dataset.id < 990620) r.remove(); })");
        }
    }

    /// <summary>空の span は幅 0 なので、hidden を外しても ToBeHidden では「見えていない」まま。箱を持っているかで見る。</summary>
    [Fact]
    public async Task AFilterItemWithNoCountCarriesNoBadge()
    {
        await Page.Locator("#sidebar .item[data-id='log']").ClickAsync();

        try
        {
            // INF は起動の行で必ずある、CRT は無い。
            await Page.Locator("#logTabs .spoitable-tab[data-source='app']").ClickAsync();
            await Page.Locator("#logFilter").ClickAsync();
            await Assertions.Expect(Page.Locator("#filters .fitem[data-kind='INF']")).ToBeVisibleAsync();

            const string Boxes = "kind => document.querySelector(`#filters .fitem[data-kind=\"${kind}\"] .n`).getClientRects().length";
            Assert.Equal(1, await Page.EvaluateAsync<int>(Boxes, "INF"));
            Assert.Equal(0, await Page.EvaluateAsync<int>(Boxes, "CRT"));
        }
        finally
        {
            if (await Page.Locator("#filterModal").IsVisibleAsync()) await Page.Locator("#filterClose").ClickAsync();
            await Page.Locator("#logTabs .spoitable-tab[data-source='vrc']").ClickAsync();
        }
    }

    private async Task ExpectHiddenKinds(Func<string[], bool> expected)
    {
        var file = Path.Combine(app.DataDirectory, "config.json");
        var waited = System.Diagnostics.Stopwatch.StartNew();

        while (waited.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (ReadHiddenKinds(file) is { } saved && expected(saved)) return;
            await Task.Delay(100);
        }

        // 「読めない」は 2 通り（まだ保存されていない / 書き込みの最中）だが結論は同じなので、待った時間を出す。
        var last = ReadHiddenKinds(file) is { } kinds ? string.Join(", ", kinds) : "(鍵がまだ無い)";
        Assert.Fail($"HiddenKinds が {waited.Elapsed.TotalSeconds:F1} 秒待っても期待どおりにならなかった: {last}");
    }

    private static string[]? ReadHiddenKinds(string file)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(file))?["HiddenKinds"].Deserialize<string[]>();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }
}
