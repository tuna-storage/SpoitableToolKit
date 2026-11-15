using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Vr;

/// <summary>
/// 手首のページ（<c>wrist.html</c>）は VR を繋がないと開かないので、シェルと同じ origin の iframe に読み込んで、ページの関数を直に呼ぶ。
/// token が無いのでホストの窓口は呼べず、起動（<c>boot</c>）は途中で止まる（<c>__stkError</c> が残る）。
/// 並べる・描く・引くはホストを通らないので、ここで見られる。
/// 見られないもの: ホストから届く報せ・眠ったら一番上へ戻す（<c>document.hidden</c> は iframe では変えられない）。
/// </summary>
public sealed class WristPageTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private const string FrameName = "e2eWrist";

    private IPage Page => app.Page;

    private IFrame Wrist => Page.Frame(FrameName) ?? throw new InvalidOperationException("手首のページの iframe が無い");

    /// <summary>
    /// 絵と同じ大きさ（1120 × 240）で、画面の左上に置く。画面外の iframe は描くのを間引かれて、
    /// scroll の出来事が来ないことがある。押せないようにして薄くするので、他の試験の邪魔はしない。
    /// </summary>
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

        await Wrist.WaitForFunctionAsync("() => typeof renderNotes === 'function' && typeof renderShortcuts === 'function'");
    }

    public Task DisposeAsync() => Page.EvaluateAsync($"() => document.querySelector('iframe[name=\"{FrameName}\"]')?.remove()");

    /// <summary>板の幅は 6 つ分で固定し、足りないときは左に詰める。足しても今あるタイルが動かない（VR では手が位置で覚える）。</summary>
    [Fact]
    public async Task AddingAShortcutMovesNeitherTheBoardNorTheTilesAlready()
    {
        var one = await LayoutAsync("['log']");
        var three = await LayoutAsync("['log', 'settings', 'dashboard']");

        Assert.Equal(746, one.Board.Width, 1);
        Assert.Equal(one.Board.Left, three.Board.Left, 1);
        Assert.Equal(one.Board.Width, three.Board.Width, 1);

        // 1 つ目のタイルは板の左（余白と枠 12）に付く。真ん中寄せなら、1 つのときは板の真ん中に来る。
        Assert.Equal(one.Board.Left + 12, one.Tiles[0], 1);
        Assert.Equal(one.Tiles[0], three.Tiles[0], 1);
        Assert.Equal(three.Tiles[0] + 122, three.Tiles[1], 1);

        // 通知の板は手首の板の右に、間 12 を空けて。
        Assert.Equal(one.Board.Left + 746 + 12, one.NotesLeft, 1);
    }

    /// <summary>
    /// 手首で試すショートカット。心拍のパネル（中身あり）・動画 URL の処理（押すだけ。中身なし）・心拍のパネルを名乗る押すだけのもの・
    /// ドラッグだけのもの（偽の名前）。
    /// </summary>
    private const string Catalogue = """
        [
          { key: 'hrs', name: 'hrs.name', fallback: 'Heart Rate', iconPath: 'M1 1L2 2', view: 'hrs', runs: false, draggable: false, text: '72 bpm', state: 'ok' },
          { key: 'videourl/resolve-latest', name: 'videourl.shortcut.resolveLatest', icon: '★', view: null, runs: true, draggable: false, text: '', state: 'off' },
          { key: 'hrs/big', name: 'big', icon: '♥', view: null, marked: 'hrs', runs: true, draggable: false, text: '', state: 'off' },
          { key: 'osc/height', name: 'height', icon: '↕', view: null, runs: true, draggable: true, text: '', state: 'off' },
        ]
        """;

    [Fact]
    public async Task ATileShowsWhatTheModuleWritesInTheColourOfItsState()
    {
        await Wrist.EvaluateAsync($$"""
            () => {
              catalogue = {{Catalogue}};
              renderShortcuts(['hrs', 'videourl/resolve-latest', 'gone/away']);
            }
            """);

        await Assertions.Expect(Wrist.Locator(".tile")).ToHaveCountAsync(2);
        await Assertions.Expect(Wrist.Locator(".tile[data-id='hrs']")).ToContainTextAsync("Heart Rate");

        var bpm = Wrist.Locator(".tile[data-id='hrs'] .val");
        await Assertions.Expect(bpm).ToHaveTextAsync("72 bpm");
        await Assertions.Expect(bpm).ToHaveAttributeAsync("data-state", "ok");
        await Assertions.Expect(Wrist.Locator(".tile[data-id='videourl/resolve-latest'] .val")).ToBeHiddenAsync();
    }

    /// <summary>タイルを作り直すと、乗せている指の :hover が次に動かすまで外れる（心拍は数秒おきに変わる）ので、中身の行だけ書き換える。</summary>
    [Fact]
    public async Task NewContentRewritesOnlyTheValueOfTheTile()
    {
        var same = await Wrist.EvaluateAsync<bool>($$"""
            () => {
              catalogue = {{Catalogue}};
              renderShortcuts(['hrs', 'videourl/resolve-latest']);
              const before = el.tiles.querySelector(".tile[data-id='hrs']");
              stk.receive('shortcutStatus', [
                { key: 'hrs', text: '120 bpm', state: 'warn' },
                { key: 'videourl/resolve-latest', text: '解決中…', state: 'ok' },
              ]);
              return before === el.tiles.querySelector(".tile[data-id='hrs']");
            }
            """);

        Assert.True(same);
        var bpm = Wrist.Locator(".tile[data-id='hrs'] .val");
        await Assertions.Expect(bpm).ToHaveTextAsync("120 bpm");
        await Assertions.Expect(bpm).ToHaveAttributeAsync("data-state", "warn");
        await Assertions.Expect(Wrist.Locator(".tile[data-id='videourl/resolve-latest'] .val")).ToHaveTextAsync("解決中…");
    }

    /// <summary>台帳（getShortcuts）を待つ間はまだ台帳に名前が無く、当てる先が無い。届いた中身は捨てずに、引いた台帳の上に当て直す。</summary>
    [Fact]
    public async Task ContentThatArrivesBeforeTheCatalogueIsKept()
    {
        await Wrist.EvaluateAsync($$"""
            () => {
              stk.receive('shortcutStatus', [{ key: 'hrs', text: '99 bpm', state: 'warn' }]);
              setCatalogue({{Catalogue}});
              renderShortcuts(['hrs', 'videourl/resolve-latest']);
            }
            """);

        var bpm = Wrist.Locator(".tile[data-id='hrs'] .val");
        await Assertions.Expect(bpm).ToHaveTextAsync("99 bpm");
        await Assertions.Expect(bpm).ToHaveAttributeAsync("data-state", "warn");
    }

    /// <summary>
    /// 開くタイルと、開く画面を名乗ったタイル（<c>marked</c>）は押した瞬間に枠を塗り、もう一度押すと消す。名乗らない押すだけのタイルは塗らない。
    /// ドラッグだけのタイル（<c>draggable</c>）は押した・離したを番号付きで送るだけで、名前を渡さず、塗らない。
    /// </summary>
    [Fact]
    public async Task PressingATileHandsItsNameToTheHost()
    {
        var result = await Wrist.EvaluateAsync<string[]>($$"""
            () => {
              catalogue = {{Catalogue}};
              window.__pressed = [];
              window.__toggled = [];
              window.__pointers = [];
              bridge.pressShortcut = key => window.__pressed.push(key);
              bridge.toggleVrWindow = view => window.__toggled.push(view);
              bridge.shortcutPointer = (page, key, seq, kind) => window.__pointers.push(`${page === pointerPage} ${key} ${seq} ${kind}`);
              renderShortcuts(['hrs', 'videourl/resolve-latest', 'hrs/big', 'osc/height']);

              const tile = id => el.tiles.querySelector(`.tile[data-id='${id}']`);
              const on = () => [...el.tiles.querySelectorAll('.tile.on')].map(t => t.dataset.id).sort().join(' ');
              const touch = (node, id) => {
                const box = node.getBoundingClientRect();
                const at = { button: 0, pointerId: id, clientX: box.left + 10, clientY: box.top + 10, bubbles: true };
                node.dispatchEvent(new PointerEvent('pointerdown', at));
                node.dispatchEvent(new PointerEvent('pointerup', at));
                node.click();
              };

              touch(tile('videourl/resolve-latest'), 7);
              const ran = on();
              touch(tile('osc/height'), 8);
              const dragged = on();
              tile('hrs/big').click();
              const marked = on();
              tile('hrs').click();
              const closed = on();
              return [window.__pressed.join(' '), window.__pointers.join(' | '), window.__toggled.join(' '), ran, dragged, marked, closed];
            }
            """);

        Assert.Equal(
            ["videourl/resolve-latest hrs/big hrs", "true osc/height 1 down | true osc/height 1 up", "", "", "", "hrs hrs/big", ""],
            result);
    }

    /// <summary>番号は読み込みごとに 1 から数え直すので、C# は読み込みの印で見分ける（SteamVR に入り直すと手首の browser は作り直される）。</summary>
    [Fact]
    public async Task ThePageMarkChangesWhenTheWristPageLoadsAgain()
    {
        var first = await Wrist.EvaluateAsync<string>("() => pointerPage");

        await Page.EvaluateAsync($$"""
            () => new Promise(resolve => {
              const frame = document.querySelector('iframe[name="{{FrameName}}"]');
              frame.onload = () => resolve();
              frame.contentWindow.location.reload();
            })
            """);
        await Wrist.WaitForFunctionAsync("() => typeof pointerPage === 'string'");
        var second = await Wrist.EvaluateAsync<string>("() => pointerPage");

        Assert.NotEqual("", first);
        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// 押している間はタイルが真ん中を軸に縮む（:active の scale）ので、見た目の箱から測ると大きさも位置もずれる。
    /// タイルを半分に縮めて、見た目の真ん中から右へ 25 画素の所を押すと、タイルの上では 50 画素右。
    /// </summary>
    [Fact]
    public async Task APressOnAShrunkTileIsMeasuredInTheTilesOwnPixels()
    {
        var sent = await Wrist.EvaluateAsync<double[]>($$"""
            () => {
              catalogue = {{Catalogue}};
              let got = null;
              bridge.shortcutPointer = (page, key, seq, kind, x, y, w, h) => { got = [x, y, w, h]; };
              renderShortcuts(['osc/height']);

              const tile = el.tiles.querySelector(".tile[data-id='osc/height']");
              tile.style.transform = 'scale(0.5)';
              tile.style.transition = 'none';
              const box = tile.getBoundingClientRect();
              const at = { button: 0, pointerId: 3, bubbles: true, clientX: box.left + box.width / 2 + 25, clientY: box.top + box.height / 2 };
              tile.dispatchEvent(new PointerEvent('pointerdown', at));
              tile.dispatchEvent(new PointerEvent('pointerup', at));
              return [...got, tile.offsetWidth, tile.offsetHeight];
            }
            """);

        var (x, y, w, h, width, height) = (sent[0], sent[1], sent[2], sent[3], sent[4], sent[5]);
        Assert.Equal((width, height), (w, h));
        Assert.Equal(width / 2 + 50, x, 1);
        Assert.Equal(height / 2, y, 1);
    }

    /// <summary>窓を ✕ で閉じたときは押した回数が変わらないので、閉じる前に読んだ答え（hrs）が「閉じた」の push のあとに着いても、タイルに枠を戻さない。</summary>
    [Fact]
    public async Task AnAnswerReadBeforeTheWindowClosedDoesNotLightTheTileAgain()
    {
        var lit = await Wrist.EvaluateAsync<string>($$"""
            async () => {
              catalogue = {{Catalogue}};
              renderShortcuts(['hrs', 'videourl/resolve-latest']);
              let release;
              bridge.getVrWindowView = () => new Promise(resolve => { release = resolve; });

              const polling = pollView();
              stk.receive('wristView', { view: null });
              release('hrs');
              await polling;

              return [...el.tiles.querySelectorAll('.tile.on')].map(tile => tile.dataset.id).join(' ');
            }
            """);

        Assert.Equal("", lit);
    }

    /// <summary>同じパネルの別の場所を指すタイル（<c>hrs#week</c>）は、名前ではなく開く画面で見分ける。</summary>
    [Fact]
    public async Task TheTileForWhatTheWindowShowsIsMarked()
    {
        var marked = await Wrist.EvaluateAsync<string[]>($$"""
            () => {
              catalogue = [
                ...{{Catalogue}},
                { key: 'hrs/week', name: 'week', view: 'hrs#week', runs: false, text: '', state: 'off' },
              ];
              renderShortcuts(['hrs', 'hrs/week', 'videourl/resolve-latest']);
              const on = () => [...el.tiles.querySelectorAll('.tile.on')].map(t => t.dataset.id).join(' ');
              stk.receive('wristView', { view: 'hrs#week' });
              const week = on();
              stk.receive('wristView', { view: 'hrs' });
              return [week, on()];
            }
            """);

        Assert.Equal(["hrs/week", "hrs"], marked);
    }

    /// <summary>失敗がまだ無いので、押すと「解くものが無い」と知らせるトーストが出る。</summary>
    [Fact]
    public async Task TheHostRunsWhatAModuleOffersWhenItIsPressed()
    {
        var keys = await Page.EvaluateAsync<string[]>("async () => (await bridge.getShortcuts()).map(s => s.key)");

        Assert.Equal(["log", "settings", "dashboard"], keys.Take(3));
        Assert.Contains("hrs", keys);
        Assert.True(Array.IndexOf(keys, "videourl") < Array.IndexOf(keys, "videourl/resolve-latest"));

        var none = await Page.EvaluateAsync<string>("() => t('videourl.shortcut.resolveLatest.none')");
        await Page.EvaluateAsync("() => bridge.pressShortcut('videourl/resolve-latest')");

        await Assertions.Expect(Page.Locator("#toasts .toast").Filter(new() { HasText = none })).ToBeVisibleAsync();
    }

    private sealed record Box(double Left, double Width);

    private sealed record Layout(Box Board, double[] Tiles, double NotesLeft);

    private async Task<Layout> LayoutAsync(string ids)
    {
        var numbers = await Wrist.EvaluateAsync<double[]>($$"""
            () => {
              renderShortcuts({{ids}});
              const board = el.board.getBoundingClientRect();
              const tiles = [...el.tiles.querySelectorAll('.tile')].map(t => t.getBoundingClientRect().left);
              return [board.left, board.width, el.notes.getBoundingClientRect().left, ...tiles];
            }
            """);

        return new Layout(new Box(numbers[0], numbers[1]), numbers[3..], numbers[2]);
    }

    /// <summary>
    /// 手首の板では自前のレーザーが巻かない（タイルを押した瞬間に Down を送るため）ので、ページが自分で巻く。
    /// 引いた拍子の小さな振れでは巻かない。離したあとは動かしても巻かない。
    /// </summary>
    [Fact]
    public async Task DraggingOverTheNotesScrollsThem()
    {
        await FillNotesAsync(10);
        var start = await Wrist.EvaluateAsync<int[]>(
            "() => [el.noteList.scrollTop, el.noteList.classList.contains('below') ? 1 : 0]");
        Assert.Equal([0, 1], start);

        // 出来事は同じ瞬間に続けて投げる。離す直前の速さは取れない（間が無い）ので流れない —
        // 間が無いのを割ると速さが果てしなくなる（1 周の頭に Move と Up がまとめて届いたときと同じ形）。
        var tops = await Wrist.EvaluateAsync<int[]>("""
            async () => {
              const list = el.noteList;
              const row = list.querySelector('.note');
              const box = row.getBoundingClientRect();
              const x = box.left + 20;
              const at = y => ({ bubbles: true, cancelable: true, clientX: x, clientY: y, button: 0 });

              row.dispatchEvent(new MouseEvent('mousedown', at(150)));
              window.dispatchEvent(new MouseEvent('mousemove', at(147)));
              const shaken = list.scrollTop;
              window.dispatchEvent(new MouseEvent('mousemove', at(90)));
              const dragged = list.scrollTop;
              window.dispatchEvent(new MouseEvent('mouseup', at(90)));
              window.dispatchEvent(new MouseEvent('mousemove', at(20)));
              const released = list.scrollTop;
              await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(() => setTimeout(r, 100))));
              return [shaken, dragged, released, list.scrollTop];
            }
            """);

        Assert.Equal([0, 60, 60, 60], tops);

        // 続きが上にも下にもある（両端を薄くする）。
        await Wrist.WaitForFunctionAsync("() => el.noteList.classList.contains('above') && el.noteList.classList.contains('below')");
    }

    /// <summary>新しいものは上に挿さるので、そのままだと中身が 1 行ずつ下へずれて、読んでいた行が逃げる。</summary>
    [Fact]
    public async Task ANewNoteLeavesTheRowBeingReadWhereItIs()
    {
        await FillNotesAsync(10);

        var keys = await Wrist.EvaluateAsync<string[]>("""
            () => {
              const list = el.noteList;
              const firstShown = () => {
                const row = [...list.children].find(r => r.offsetTop + r.offsetHeight > list.scrollTop);
                return [row.dataset.key, Math.round(row.offsetTop - list.scrollTop)].join('@');
              };
              list.scrollTop = 110;
              const before = firstShown();
              addNote(fromHistory({ seq: 100, title: 'newer', at: new Date().toISOString(), kind: { id: 'info', tone: 'gray' } }, true));
              renderNotes();
              return [before, firstShown()];
            }
            """);

        Assert.Equal(keys[0], keys[1]);
    }

    // 以降の慣性の試験は、出来事の間を本当に空けて投げる（同じ瞬間に投げると、速さが取れないので流れない）。

    [Fact]
    public async Task LettingGoWhileMovingKeepsTheNotesGoing()
    {
        await FillNotesAsync(40);

        var released = await FlingAsync(stepPx: 15);
        Assert.True(released > 0, "引いても巻いていない（試験の前提が崩れている）");

        await Wrist.WaitForFunctionAsync($"() => el.noteList.scrollTop > {released} + 20", null, new() { Timeout = 3_000 });
        await Wrist.WaitForFunctionAsync("() => noteFling === null", null, new() { Timeout = 5_000 });

        var end = await Wrist.EvaluateAsync<double[]>("() => [el.noteList.scrollTop, el.noteList.scrollHeight - el.noteList.clientHeight]");
        Assert.True(end[0] > released + 20, $"流れていない（離した所 {released}、止まった所 {end[0]}）");
        Assert.True(end[0] < end[1], $"端まで流れた（{end[0]} / {end[1]}）— 緩めていない");
    }

    /// <summary>VR のレーザーは手首を少し回すだけで板の上を大きく動くので、振った速さのまま流すと、通知の板（高さ 200 px ほど）では何も読めないまま端へ着く。</summary>
    [Fact]
    public async Task AFastFlickDoesNotThrowTheNotesToTheEnd()
    {
        // 覚えているのは 40 件（NOTES_KEPT）で、端は 1870 px ほど。引くので 600、上限（3 px/ms）なら流れて +1000 ほどで止まる。
        // 振った速さ（6 px/ms ほど）のまま流すと +2000 で端を越える。
        await FillNotesAsync(40);

        var released = await FlingAsync(stepPx: 100);
        await Wrist.WaitForFunctionAsync("() => noteFling === null", null, new() { Timeout = 5_000 });

        var end = await Wrist.EvaluateAsync<double[]>("() => [el.noteList.scrollTop, el.noteList.scrollHeight - el.noteList.clientHeight]");
        Assert.True(end[0] > released + 20, $"流れていない（離した所 {released}、止まった所 {end[0]}）");
        Assert.True(end[0] < end[1] - 20, $"端まで飛んだ（離した所 {released}、止まった所 {end[0]}、端 {end[1]}）");
    }

    [Fact]
    public async Task PressingAgainStopsTheFling()
    {
        await FillNotesAsync(40);

        var released = await FlingAsync(stepPx: 15);
        await Wrist.WaitForFunctionAsync($"() => el.noteList.scrollTop > {released} + 5", null, new() { Timeout = 3_000 });

        var tops = await Wrist.EvaluateAsync<double[]>("""
            async () => {
              const list = el.noteList;
              const row = list.querySelector('.note');
              const at = { bubbles: true, cancelable: true, clientX: row.getBoundingClientRect().left + 20, clientY: 100, button: 0 };
              row.dispatchEvent(new MouseEvent('mousedown', at));
              const pressed = list.scrollTop;
              await new Promise(r => setTimeout(r, 200));
              const later = list.scrollTop;
              window.dispatchEvent(new MouseEvent('mouseup', at));
              await new Promise(r => setTimeout(r, 200));
              return [pressed, later, list.scrollTop, noteFling === null ? 1 : 0];
            }
            """);

        Assert.Equal(tops[0], tops[1]);
        Assert.Equal(tops[0], tops[2]);   // 動かさずに離したら流れない
        Assert.Equal(1, tops[3]);
    }

    [Fact]
    public async Task HoldingStillBeforeLettingGoDoesNotFling()
    {
        await FillNotesAsync(40);

        var tops = await Wrist.EvaluateAsync<double[]>("""
            async () => {
              const list = el.noteList;
              const row = list.querySelector('.note');
              const x = row.getBoundingClientRect().left + 20;
              const at = y => ({ bubbles: true, cancelable: true, clientX: x, clientY: y, button: 0 });
              const wait = ms => new Promise(r => setTimeout(r, ms));

              row.dispatchEvent(new MouseEvent('mousedown', at(200)));
              for (let i = 1; i <= 6; i++) { await wait(16); window.dispatchEvent(new MouseEvent('mousemove', at(200 - i * 15))); }
              await wait(200);
              window.dispatchEvent(new MouseEvent('mouseup', at(110)));
              const released = list.scrollTop;
              await wait(300);
              return [released, list.scrollTop];
            }
            """);

        Assert.True(tops[0] > 0, "引いても巻いていない（試験の前提が崩れている）");
        Assert.Equal(tops[0], tops[1]);
    }

    [Fact]
    public async Task TheFlingStopsAtEitherEnd()
    {
        await FillNotesAsync(14);

        // 引いただけでは端に届かず、流れて届く（引いて届いたなら、流れの端を見ていない）。
        // 着いたらすぐ止まる（着いたあとも速さが落ちきるまで回り続けない）。着いた時点ではまだ速いので、緩めて止まるなら 1 秒近くかかる。
        var released = await FlingAsync(stepPx: 40);
        await Wrist.WaitForFunctionAsync("() => el.noteList.scrollTop >= el.noteList.scrollHeight - el.noteList.clientHeight - 0.5", null, new() { Timeout = 3_000 });
        await Wrist.WaitForFunctionAsync("() => noteFling === null", null, new() { Timeout = 300 });
        var bottom = await Wrist.EvaluateAsync<double[]>("() => [el.noteList.scrollTop, el.noteList.scrollHeight - el.noteList.clientHeight]");
        Assert.True(released < bottom[1] - 20, $"引いただけで端に届いた（試験の前提が崩れている。離した所 {released}、端 {bottom[1]}）");
        Assert.Equal(bottom[1], bottom[0], 1);

        released = await FlingAsync(stepPx: -40);
        Assert.True(released > 20, $"引いただけで上の端に届いた（試験の前提が崩れている。離した所 {released}）");
        await Wrist.WaitForFunctionAsync("() => el.noteList.scrollTop <= 0", null, new() { Timeout = 3_000 });
        await Wrist.WaitForFunctionAsync("() => noteFling === null", null, new() { Timeout = 300 });
    }

    /// <summary>16 ms おきに <paramref name="stepPx"/> ずつ 6 回指を上げ（負なら下げ）、動かしたまま離す。離した瞬間の位置を返す。</summary>
    private Task<double> FlingAsync(int stepPx) => Wrist.EvaluateAsync<double>($$"""
        async () => {
          const list = el.noteList;
          const row = list.querySelector('.note');
          const x = row.getBoundingClientRect().left + 20;
          const at = y => ({ bubbles: true, cancelable: true, clientX: x, clientY: y, button: 0 });
          const wait = ms => new Promise(r => setTimeout(r, ms));

          row.dispatchEvent(new MouseEvent('mousedown', at(400)));
          for (let i = 1; i <= 6; i++) { await wait(16); window.dispatchEvent(new MouseEvent('mousemove', at(400 - i * {{stepPx}}))); }
          window.dispatchEvent(new MouseEvent('mouseup', at(400 - 6 * {{stepPx}})));
          return list.scrollTop;
        }
        """);

    [Fact]
    public async Task AtTheTopANewNoteShowsFirst()
    {
        await FillNotesAsync(10);

        var shown = await Wrist.EvaluateAsync<string[]>("""
            () => {
              addNote(fromHistory({ seq: 100, title: 'newer', at: new Date().toISOString(), kind: { id: 'info', tone: 'gray' } }, true));
              renderNotes();
              return [String(el.noteList.scrollTop), el.noteList.firstElementChild.dataset.key];
            }
            """);

        Assert.Equal(["0", "h100"], shown);
    }

    /// <summary>通知を <paramref name="count"/> 件（新しい順に 1 秒ずつ古く）。ホストからは届かないので、ページの関数で直に足す。</summary>
    private Task FillNotesAsync(int count) => Wrist.EvaluateAsync($$"""
        () => {
          notes = [];
          hiddenNotes = new Set();
          for (let i = 0; i < {{count}}; i++) {
            addNote(fromHistory({ seq: i, title: 'note ' + i, at: new Date(Date.now() - (i + 1) * 1000).toISOString(), kind: { id: 'info', tone: 'gray' } }, false));
          }
          el.noteList.scrollTop = 0;
          renderNotes();
        }
        """);
}
