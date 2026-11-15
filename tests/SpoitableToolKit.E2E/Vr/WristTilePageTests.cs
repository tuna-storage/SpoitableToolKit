using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Vr;

/// <summary>
/// 手首のタイルに描くモジュールのページ（<c>IWristShortcut.Page</c>）。例は心拍の <c>hrs/live</c>（<c>modules/hrs/web/tile.html</c>）。
/// 手首のページは <see cref="WristPageTests"/> と同じくシェルの中の iframe に読み込む。手首は token を持たないので、
/// タイルのページが頼む口（文言・モジュールの呼び出し）だけシェルの本物の bridge へ繋ぎ替える。中継（持ち主を origin で決める・
/// push を配る）は手首のコードのまま通る。C# の push が手首へ届くこと（<c>WristHost.Channels</c>）は unit で見る。
/// 押す（<c>pressShortcut</c>）とドラッグ（<c>shortcutPointer</c>）は控えるだけ（C# 側は unit の <c>ShortcutGesturesTests</c> と <see cref="WristGestureTests"/>）。
/// <c>hrs/live</c> は押すだけのタイル。ドラッグだけのページのタイルは本物のモジュールに無いので、ドラッグの試験では手首の台帳の上だけで
/// <c>hrs/live</c> をドラッグだけのものにして並べる（<see cref="PretendTheLiveTileIsDraggedAsync"/>）。
/// </summary>
public sealed class WristTilePageTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private const string FrameName = "e2eWristTiles";

    private const string Live = "hrs/live";

    private IPage Page => app.Page;

    private IFrame Wrist => Page.Frame(FrameName) ?? throw new InvalidOperationException("手首のページの iframe が無い");

    public async Task InitializeAsync()
    {
        await Page.EvaluateAsync($$"""
            () => new Promise(resolve => {
              const frame = document.createElement('iframe');
              frame.name = '{{FrameName}}';
              frame.style.cssText = 'position:fixed;left:0;top:0;width:1120px;height:240px;border:0;opacity:.01;pointer-events:none;z-index:9999';
              frame.onload = () => resolve();
              frame.src = '/wrist.html';
              document.body.appendChild(frame);
            })
            """);

        // 起動（boot）が token 無しで止まってから差し替える。止まる前に差し替えると、boot が差し替えた口で先へ進んでしまう。
        await Wrist.WaitForFunctionAsync("() => typeof pushToTiles === 'function' && window.__stkError !== undefined");
        await Wrist.EvaluateAsync("""
            async () => {
              const shell = parent.eval('bridge');
              window.__calls = [];
              window.__strings = 0;
              window.__pressed = [];
              window.__pointers = [];
              window.__open = null;
              window.__pointerData = [];
              bridge.shortcutPointer = (page, key, seq, kind, x, y, w, h) => {
                window.__pointers.push(`${key} ${seq} ${kind}`);
                window.__pointerData.push({ key, seq, kind, x, y, w, h });
              };
              bridge.getStrings = () => { window.__strings++; return shell.getStrings(); };
              bridge.callModule = (module, method, payload) => { window.__calls.push(module + ' ' + method); return shell.callModule(module, method, payload); };
              // 押したら、開くものも名乗ったもの（hrs/live のモジュールが開く）も、その画面が開いたことにする。
              bridge.pressShortcut = key => { window.__pressed.push(key); window.__open = known(key)?.view ?? known(key)?.marked ?? null; };
              bridge.toggleVrWindow = () => {};
              bridge.getVrWindowView = async () => window.__open;
              setCatalogue(await shell.getShortcuts());
            }
            """);
    }

    public Task DisposeAsync() => Page.EvaluateAsync($"() => document.querySelector('iframe[name=\"{FrameName}\"]')?.remove()");

    private async Task<IFrame> TileAsync(string key = Live)
    {
        await Wrist.WaitForFunctionAsync($"() => window.__stkTileReady?.['{key}'] === true");
        return Wrist.ChildFrames.Single(f => f.Url.StartsWith("spoitable://hrs/tile.html", StringComparison.Ordinal));
    }

    /// <summary>本物の台帳。<c>hrs/live</c> は <c>MarkedPanel</c> で心拍のパネルを名乗る。開くのはモジュールの処理なので、本体は名乗りでしか開く先を知らない。</summary>
    [Fact]
    public async Task TheLiveTileIsFramedWhileTheWindowShowsTheHeartRatePanel()
    {
        Assert.Equal("hrs", await Wrist.EvaluateAsync<string>($"() => known('{Live}')?.marked ?? ''"));

        await Wrist.EvaluateAsync($"() => renderShortcuts(['{Live}', 'hrs', 'videourl/resolve-latest'])");
        await TileAsync();

        var framed = await Wrist.EvaluateAsync<string[]>("""
            () => {
              const on = () => [...el.tiles.querySelectorAll('.tile.on')].map(t => t.dataset.id).sort().join(' ');
              stk.receive('wristView', { view: 'hrs' });
              const opened = on();
              stk.receive('wristView', { view: null });
              return [opened, on()];
            }
            """);

        Assert.Equal(["hrs hrs/live", ""], framed);
    }

    /// <summary>ページが開いたときの呼び出しは心拍に届き（手首が origin から決める）、push は心拍の分だけ受ける。別のモジュールの push は届かない。</summary>
    [Fact]
    public async Task ATilePageDrawsWhatItsModuleSaysAndHearsOnlyItsOwnPushes()
    {
        var page = await Wrist.EvaluateAsync<string>($"() => known('{Live}')?.page ?? ''");
        Assert.Equal("spoitable://hrs/tile.html", page);

        await Wrist.EvaluateAsync($"() => renderShortcuts(['{Live}', 'hrs'])");
        var tile = await TileAsync();

        await Assertions.Expect(Wrist.Locator($".tile.page[data-id='{Live}'] > iframe")).ToHaveCountAsync(1);
        await Assertions.Expect(Wrist.Locator("button.tile[data-id='hrs']")).ToHaveCountAsync(1);

        // e2e のアプリには心拍計が居ないので、繋がっていない形（--）で描く。
        await Assertions.Expect(tile.Locator("#bpm")).ToHaveTextAsync("--");
        var calls = await Wrist.EvaluateAsync<string[]>("() => window.__calls");
        Assert.Equal(["hrs getHeartRate"], calls);

        // 文言は手首が渡す（bridge.js は読み込むと必ず頼む）。
        Assert.Equal(1, await Wrist.EvaluateAsync<int>("() => window.__strings"));

        await tile.EvaluateAsync("() => { window.__seen = []; spoitable.onPush((channel, payload) => window.__seen.push(channel + ' ' + payload.heartRate)); }");

        await Wrist.EvaluateAsync("""
            () => {
              const beat = heartRate => ({ connected: true, heartRate, samples: [{ bpm: 80 }, { bpm: 84 }, { bpm: heartRate }] });
              stk.receive('modulePush', { module: 'hrs', channel: 'status', payload: beat(88) });
              stk.receive('modulePush', { module: 'videourl', channel: 'status', payload: beat(140) });
              stk.receive('modulePush', { module: 'hrs', channel: 'status', payload: beat(90) });
            }
            """);

        await Assertions.Expect(tile.Locator("#bpm")).ToHaveTextAsync("90");
        var seen = await tile.EvaluateAsync<string[]>("() => window.__seen");
        Assert.Equal(["status 88", "status 90"], seen);
        Assert.NotEqual("", await tile.Locator("#line").GetAttributeAsync("points"));
    }

    /// <summary>手首の台帳の上だけで <c>hrs/live</c> をドラッグだけのタイルにする（並べる前に呼ぶ）。C# の台帳は変えない。</summary>
    private Task PretendTheLiveTileIsDraggedAsync() => Wrist.EvaluateAsync($"() => {{ known('{Live}').draggable = true; }}");

    [Fact]
    public async Task AClickableTilePageIsPressedByAClickAndLightsAtOnce()
    {
        await Wrist.EvaluateAsync($"() => renderShortcuts(['{Live}', 'hrs'])");
        var tile = await TileAsync();

        await tile.EvaluateAsync("""
            () => {
              window.__got = [];
              for (const type of ['mousedown', 'mousemove', 'mouseup', 'click'])
                window.addEventListener(type, e => window.__got.push(type));
            }
            """);

        var box = await Wrist.Locator($".tile.page[data-id='{Live}']").BoundingBoxAsync() ?? throw new InvalidOperationException("タイルが出ていない");
        var lit = "";
        await WithMouseAsync(async () =>
        {
            await Page.Mouse.MoveAsync(box.X + 30, box.Y + 30);
            await Page.Mouse.DownAsync();
            await Page.Mouse.MoveAsync(box.X + 40, box.Y + 36, new() { Steps = 3 });
            await Page.Mouse.UpAsync();
            lit = await Wrist.EvaluateAsync<string>("() => [...el.tiles.querySelectorAll('.tile.on')].map(t => t.dataset.id).sort().join(' ')");

            // 押したまま外へ出て離す（板の右の外）。
            await Page.Mouse.MoveAsync(box.X + 30, box.Y + 30);
            await Page.Mouse.DownAsync();
            await Page.Mouse.MoveAsync(box.X + 400, box.Y + 30, new() { Steps = 4 });
            await Page.Mouse.UpAsync();
        });

        Assert.Equal("hrs hrs/live", lit);
        Assert.Equal([Live], await Wrist.EvaluateAsync<string[]>("() => window.__pressed"));
        Assert.Empty(await Wrist.EvaluateAsync<string[]>("() => window.__pointers"));
        Assert.Empty(await tile.EvaluateAsync<string[]>("() => window.__got"));
    }

    /// <summary>
    /// 送るのは押しの番号・種類・タイルの左上からの画素・タイルの大きさ。
    /// タイルの外へ出ても押したタイルが捕まえているので、動き・離しが届く。
    /// </summary>
    [Fact]
    public async Task PressesAndDragsOnATilePageGoToTheHostNotToThePage()
    {
        await PretendTheLiveTileIsDraggedAsync();
        await Wrist.EvaluateAsync($"() => renderShortcuts(['{Live}'])");
        var tile = await TileAsync();

        await tile.EvaluateAsync("""
            () => {
              window.__got = [];
              for (const type of ['mousedown', 'mousemove', 'mouseup', 'click'])
                window.addEventListener(type, e => window.__got.push(type));
            }
            """);

        var box = await Wrist.Locator($".tile.page[data-id='{Live}']").BoundingBoxAsync() ?? throw new InvalidOperationException("タイルが出ていない");
        await WithMouseAsync(async () =>
        {
            await Page.Mouse.MoveAsync(box.X + 20, box.Y + 30);
            await Page.Mouse.DownAsync();
            await Page.Mouse.MoveAsync(box.X + 60, box.Y + 40, new() { Steps = 4 });
            await Page.Mouse.MoveAsync(box.X + 200, box.Y + 40, new() { Steps = 4 });
            await Page.Mouse.UpAsync();

            await Wrist.WaitForFunctionAsync("() => window.__pointers.some(p => p.endsWith(' up'))");
        });

        var sent = System.Text.Json.JsonSerializer.Deserialize<Sent[]>(
            await Wrist.EvaluateAsync<string>("() => JSON.stringify(window.__pointerData)"),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.All(sent, p => Assert.Equal((Live, 1L), (p.Key, p.Seq)));
        Assert.Equal("down", sent[0].Kind);
        Assert.Equal("up", sent[^1].Kind);
        Assert.All(sent[1..^1], p => Assert.Equal("move", p.Kind));

        // 大きさは縮める前のタイル（押している間は :active で少し縮むが、測るのは縮める前の箱）。位置は縮みを戻した、タイル自身の画素。
        Assert.All(sent, p => Assert.Equal((Math.Round((double)box.Width), Math.Round((double)box.Height)), (p.W, p.H)));
        Near(sent[0], 20, 30);
        Near(sent[^1], 200, 40);
        Near(sent[^2], 200, 40);
        Assert.Contains(sent, p => Math.Abs(p.X - ((60 - p.W / 2) / 0.96 + p.W / 2)) <= 1 || Math.Abs(p.X - 60) <= 1);

        Assert.Empty(await tile.EvaluateAsync<string[]>("() => window.__got"));
        Assert.Empty(await Wrist.EvaluateAsync<string[]>("() => window.__pressed"));
    }

    private sealed record Sent(string Key, long Seq, string Kind, double X, double Y, double W, double H);

    /// <summary>
    /// 押している間はタイルが真ん中を軸に 0.96 倍に縮む（:active）ので、縮んだ見た目の上での位置（真ん中からの差 ÷ 0.96）になる。
    /// 押した瞬間にもう縮んでいたかはブラウザ次第なので、縮んでいない値も通す。
    /// </summary>
    private static void Near(Sent at, double x, double y)
    {
        static bool Close(double got, double put, double size) =>
            Math.Abs(got - put) <= 1 || Math.Abs(got - ((put - size / 2) / 0.96 + size / 2)) <= 1;

        Assert.True(Close(at.X, x, at.W) && Close(at.Y, y, at.H), $"{at.Kind} at ({at.X:0.0}, {at.Y:0.0}), expected about ({x}, {y})");
    }

    /// <summary>
    /// Layout Mode の間はタイルの段ごと隠れるので、押しても何も送らない。
    /// ドラッグだけのタイルを押している最中に Layout Mode に入った・タイルが外れたら、離しはもう届かないので、
    /// その押しの番号で「途切れた」を送る（C# がドラッグを終わらせる）。
    /// </summary>
    [Fact]
    public async Task ThePageCannotPressAndAPressEndsWhenLayoutStartsOrItsTileGoes()
    {
        await PretendTheLiveTileIsDraggedAsync();
        await Wrist.EvaluateAsync($"() => renderShortcuts(['{Live}', 'hrs'])");
        var tile = await TileAsync();

        Assert.Equal("undefined", await tile.EvaluateAsync<string>("() => typeof spoitable.activate"));

        var box = await Wrist.Locator($".tile.page[data-id='{Live}']").BoundingBoxAsync() ?? throw new InvalidOperationException("タイルが出ていない");
        await WithMouseAsync(async () =>
        {
            await Page.Mouse.MoveAsync(box.X + 50, box.Y + 50);

            await Wrist.EvaluateAsync("() => document.body.classList.add('layout')");
            await Page.Mouse.DownAsync();
            await Page.Mouse.UpAsync();
            await Wrist.EvaluateAsync("() => document.body.classList.remove('layout')");
            Assert.Empty(await Wrist.EvaluateAsync<string[]>("() => window.__pointers"));

            await Page.Mouse.DownAsync();
            await Wrist.EvaluateAsync("() => { renderLayout({ on: true, width: 0.24 }); renderLayout({ on: false, width: 0.24 }); }");
            await Page.Mouse.UpAsync();

            await Page.Mouse.DownAsync();
            await Wrist.EvaluateAsync("() => renderShortcuts(['hrs'])");
            await Page.Mouse.UpAsync();
        });

        var pointers = await Wrist.EvaluateAsync<string[]>("() => window.__pointers");
        Assert.Equal([$"{Live} 1 down", $"{Live} 1 cancel", $"{Live} 2 down", $"{Live} 2 cancel"], pointers);
    }

    /// <summary>iframe は普段、他の試験の邪魔をしないよう押せなくしてある。</summary>
    private async Task WithMouseAsync(Func<Task> act)
    {
        await Page.EvaluateAsync($"() => document.querySelector('iframe[name=\"{FrameName}\"]').style.pointerEvents = 'auto'");
        try
        {
            await act();
        }
        finally
        {
            await Page.Mouse.MoveAsync(1, 1);
            await Page.EvaluateAsync($"() => document.querySelector('iframe[name=\"{FrameName}\"]').style.pointerEvents = 'none'");
        }
    }

    /// <summary>
    /// iframe の contentWindow は移っても同じものが残るので、origin まで見る。
    /// 動画 URL のパネルは読み込むと文言を頼み、届いたら <c>getFailures</c> を呼ぶ。手首が聞いてしまうと、心拍宛の呼び出しとして通る。
    /// </summary>
    [Fact]
    public async Task APageFromAnotherOriginInTheTileIsNotHeard()
    {
        await Wrist.EvaluateAsync($"() => renderShortcuts(['{Live}'])");
        var tile = await TileAsync();
        var before = await Wrist.EvaluateAsync<int>("() => window.__strings");

        await tile.EvaluateAsync("() => { location.href = 'spoitable://videourl/index.html'; }");

        // 別の origin へ移ると、frame は作り直されることがある（別のレンダラーへ移る）。前の frame は掴まず、URL で探し直す。
        IFrame? other = null;
        for (var i = 0; i < 100 && other is null; i++)
        {
            other = Wrist.ChildFrames.FirstOrDefault(f => f.Url.StartsWith("spoitable://videourl/", StringComparison.Ordinal));
            if (other is null) await Task.Delay(100);
        }

        Assert.NotNull(other);
        await other.WaitForFunctionAsync("() => window.__panelReady === true");
        await Task.Delay(1000);

        var calls = await Wrist.EvaluateAsync<string[]>("() => window.__calls");
        Assert.Equal(["hrs getHeartRate"], calls);
        Assert.Equal(before, await Wrist.EvaluateAsync<int>("() => window.__strings"));
    }

    /// <summary>
    /// 読み込み中の iframe へ送っても、中はまだ about:blank で黙って消える（シェルのパネルと同じ）ので、覚えておいて ready で渡す。
    /// 渡ったかは、bridge.js がテーマを引き直した印（<c>?v=&lt;読み込みの印&gt;-&lt;回数&gt;</c>）で見る。配ったままは <c>?v=&lt;テーマの版&gt;</c>。
    /// </summary>
    [Fact]
    public async Task AThemeChangeWhileATilePageLoadsReachesItOnceReady()
    {
        await Wrist.EvaluateAsync($"() => {{ renderShortcuts(['{Live}']); pushToTiles(null, 'configChanged', {{}}); }}");
        var tile = await TileAsync();

        await tile.WaitForFunctionAsync("""
            () => /\?v=[0-9a-z]+-\d+$/.test(document.querySelector('link[href^="/_host/theme.css"]').getAttribute('href'))
            """, null, new() { Timeout = 10_000 });
    }

    /// <summary>
    /// iframe は DOM の中で動かすと読み込み直す。動いたのに「読み終わった」のままだと、消える postMessage で送ってしまうので、
    /// 読み込み直している間に来た設定の変更（テーマ）も、読み終わったら渡す。
    /// </summary>
    [Fact]
    public async Task AThemeChangeRightAfterATileMovesReachesItsReloadedPage()
    {
        await Wrist.EvaluateAsync($"() => renderShortcuts(['log', '{Live}'])");
        await TileAsync();

        var moved = await Wrist.EvaluateAsync<bool>($$"""
            () => {
              const frame = el.tiles.querySelector('.tile.page > iframe');
              window.__stkTileReady = {};
              renderShortcuts(['{{Live}}', 'log']);
              pushToTiles(null, 'configChanged', {});
              return el.tiles.querySelector('.tile.page > iframe') === frame && el.tiles.firstElementChild.dataset.id === '{{Live}}';
            }
            """);
        Assert.True(moved);

        var tile = await TileAsync();
        await tile.WaitForFunctionAsync("""
            () => /\?v=[0-9a-z]+-\d+$/.test(document.querySelector('link[href^="/_host/theme.css"]').getAttribute('href'))
            """, null, new() { Timeout = 10_000 });
    }

    /// <summary>
    /// 13 個（3 ページ）なら、1 ページ目では 2 ページ目まで作り、3 ページ目は作らない。3 ページ目へ送ると、2 つ離れた 1 ページ目のものは閉じる。
    /// 設定が変わって並べ直しても（<c>configChanged</c>）同じ並びならページを読み込み直さず、変わったこと（テーマ）は push で伝える。
    /// 言語が変わったときだけ作り直す（名前と、ページがもらう文言が変わる）。
    /// </summary>
    [Fact]
    public async Task OnlyTheShownPageAndItsNeighboursHaveFramesAndARefreshKeepsThem()
    {
        var count = await Wrist.EvaluateAsync<int>($$"""
            () => {
              const live = known('{{Live}}');
              catalogue = [...catalogue, ...Array.from({ length: 12 }, (_, i) => ({ ...live, key: 'hrs/t' + (i + 2) }))];
              window.__keys = ['{{Live}}', ...Array.from({ length: 12 }, (_, i) => 'hrs/t' + (i + 2))];
              bridge.getStatus = async () => ({ wristShortcuts: window.__keys, wristStatus: [], wristHiddenNotes: [] });
              renderShortcuts(window.__keys);
              window.__before = [...el.tiles.querySelectorAll('iframe')];
              return window.__before.length;
            }
            """);
        Assert.Equal(12, count);

        foreach (var key in Enumerable.Range(2, 11).Select(i => $"hrs/t{i}").Prepend(Live))
            await Wrist.WaitForFunctionAsync($"() => window.__stkTileReady?.['{key}'] === true");

        var first = Wrist.ChildFrames.First(f => f.Url.StartsWith("spoitable://hrs/tile.html", StringComparison.Ordinal));
        const string Theme = "() => document.querySelector('link[href^=\"/_host/theme.css\"]').getAttribute('href')";
        var themeBefore = await first.EvaluateAsync<string>(Theme);

        // 設定が変わった。手首は読み直して（getStatus）同じ並びで並べ直す。タイルのページには configChanged を渡す（テーマを引き直す）。
        var kept = await Wrist.EvaluateAsync<bool>("""
            async () => {
              stk.receive('configChanged', {});
              await new Promise(r => setTimeout(r, 300));
              const now = [...el.tiles.querySelectorAll('iframe')];
              return now.length === 12 && now.every((f, i) => f === window.__before[i] && f.isConnected);
            }
            """);
        Assert.True(kept);
        await first.WaitForFunctionAsync($"before => ({Theme})() !== before", themeBefore);

        // 2 ページ目へ。前後（1・3 ページ目）も持つ: 作ってあった 12 はそのまま、3 ページ目の 1 つを足す。
        var second = await Wrist.EvaluateAsync<int[]>("""
            () => {
              page = 1;
              renderShortcuts();
              return [el.tiles.querySelectorAll('iframe').length, window.__before.filter(f => f.isConnected).length];
            }
            """);
        Assert.Equal([13, 12], second);

        // 3 ページ目へ。2 つ離れた 1 ページ目の 6 つは閉じ、2 ページ目の 6 つは残る。
        var third = await Wrist.EvaluateAsync<int[]>("""
            () => {
              page = 2;
              renderShortcuts();
              return [el.tiles.querySelectorAll('iframe').length, window.__before.slice(0, 6).filter(f => f.isConnected).length,
                window.__before.slice(6).filter(f => f.isConnected).length];
            }
            """);
        Assert.Equal([7, 0, 6], third);

        // 言語が変わったら作り直す。
        var rebuilt = await Wrist.EvaluateAsync<bool>("""
            async () => {
              const shown = [...el.tiles.querySelectorAll('iframe')];
              stk.receive('configChanged', { language: true });
              for (let i = 0; i < 60 && shown.some(f => f.isConnected); i++) await new Promise(r => setTimeout(r, 50));
              const now = [...el.tiles.querySelectorAll('iframe')];
              return now.length === 7 && shown.every(f => !f.isConnected);
            }
            """);
        Assert.True(rebuilt);
    }
}
