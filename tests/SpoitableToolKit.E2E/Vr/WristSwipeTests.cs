using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Vr;

/// <summary>
/// 手首のページ（<c>wrist.html</c>）を <see cref="WristPageTests"/> と同じくシェルの中の iframe に読み込み、偽の台帳を並べる
/// （1 ページ目は押すだけのタイル 5 つとドラッグだけのタイル 1 つ、2 ページ目は 2 つ）。押す（<c>pressShortcut</c>）とドラッグ（<c>shortcutPointer</c>）は控えるだけ。
/// 引きは本物のマウスで通す。押した所と離した所が同じタイルなら、ブラウザは click を出す（引いたあとの click を飲むのはそこを見る）。
/// 速さは離す直前 100 ms の動きから取るので、「ゆっくり」は離す前に 200 ms 止め、「払う」は 16 ms おきに動かしてすぐ離す。
/// iframe は画面の左上（0, 0）に置くので、手首の板は x = 0〜746。タイルの列は 12 + 122 × 列、2 ページ目は 746 px 右に並ぶ。
/// </summary>
public sealed class WristSwipeTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private const string FrameName = "e2eWristSwipe";

    /// <summary>1 ページの送り幅（ページの幅 722 + ページの間 24）。半分は 373。</summary>
    private const double Stride = 746;

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

        // 起動（boot）が token 無しで止まってから差し替える。
        await Wrist.WaitForFunctionAsync("() => typeof renderShortcuts === 'function' && window.__stkError !== undefined");
        await Wrist.EvaluateAsync("""
            () => {
              window.__pressed = [];
              window.__pointers = [];
              bridge.pressShortcut = key => window.__pressed.push(key);
              bridge.shortcutPointer = (page, key, seq, kind) => window.__pointers.push(key + ' ' + kind);
              bridge.toggleVrWindow = () => {};
              bridge.getVrWindowView = async () => null;

              const tile = (key, more) => ({ key, name: key, fallback: key, icon: '■', view: null, runs: true, draggable: false, text: '', state: 'off', ...more });
              catalogue = [
                tile('a1', { page: '/_host/theme.css' }), tile('a2'), tile('a3'), tile('a4'), tile('a5'), tile('knob', { draggable: true }),
                tile('b1', { page: '/_host/theme.css' }), tile('b2'),
              ];
              renderShortcuts(['a1', 'a2', 'a3', 'a4', 'a5', 'knob', 'b1', 'b2']);
            }
            """);
    }

    public Task DisposeAsync() => Page.EvaluateAsync($"() => document.querySelector('iframe[name=\"{FrameName}\"]')?.remove()");

    /// <summary>止めてから離すので、払いではなく半分で決まる。引いている間は並びが指についてくる（越えた所から、指と同じだけ）。</summary>
    [Fact]
    public async Task ASwipePastHalfAPageOnAPressOnlyTileTurnsToTheNextPage()
    {
        var start = await CentreAsync("a5");
        double following = 0;

        await WithMouseAsync(async () =>
        {
            await DownAsync(start);
            // 40 px ずつ 12 回。2 回目（80 px）で引き始め、そこから 400 px ついてくる。
            await MoveAsync(-80, 0, steps: 2);
            await DraggingAsync();
            await MoveAsync(-100, 0, steps: 3);
            await PulledAsync(-100);
            following = await Wrist.EvaluateAsync<double>("() => pageOffset(pagePull)");
            await MoveAsync(-300, 0, steps: 7);
            await UpAsync(holdMs: 200);
        });

        Assert.Equal(-100, following, 1);
        await SettledAsync();
        Assert.Equal(1, await Wrist.EvaluateAsync<int>("() => page"));
        Assert.Equal("translateX(-746px)", await Wrist.EvaluateAsync<string>("() => el.tiles.style.transform"));
        Assert.Empty(await PressedAsync());
    }

    [Fact]
    public async Task ASlowSwipeShorterThanHalfAPageGoesBack()
    {
        var start = await CentreAsync("a5");

        await WithMouseAsync(async () =>
        {
            await DownAsync(start);
            await MoveAsync(-280, 0, steps: 7);   // 越えて 200 px
            await UpAsync(holdMs: 200);
        });

        await SettledAsync();
        Assert.Equal(0, await Wrist.EvaluateAsync<int>("() => page"));
        Assert.Empty(await PressedAsync());
    }

    [Fact]
    public async Task AQuickShortFlickTurnsThePage()
    {
        var start = await CentreAsync("a5");

        AssertFlicked(await FlickAsync(start, (-40, 4, 0)));

        await SettledAsync();
        Assert.Equal(1, await Wrist.EvaluateAsync<int>("() => page"));
        Assert.Empty(await PressedAsync());
    }

    [Fact]
    public async Task AMoveSmallerThanTheSwipeDistanceIsStillAPress()
    {
        var start = await CentreAsync("a5");

        await WithMouseAsync(async () =>
        {
            await DownAsync(start);
            await MoveAsync(-40, 0, steps: 2);
            Assert.Equal(0, await Wrist.EvaluateAsync<double>("() => pagePull"));
            await UpAsync();
        });

        var pressed = await PressedAsync();
        Assert.Equal(["a5"], pressed);
        Assert.Equal(0, await Wrist.EvaluateAsync<int>("() => page"));
    }

    /// <summary>
    /// 並びが指についてくるので、離した所が押した所と同じタイルになり、ブラウザはそこで click を出す。それを飲む。
    /// 戻した所は先頭のページの外向き（+80 px）なので、並びは抵抗で 32 px しかずれず、離す所は押したタイル（a5）の上のまま。
    /// 離す直前にそれを確かめる（別のタイルの上なら click は共通の親に出て、飲まなくても押しにならない）。
    /// </summary>
    [Fact]
    public async Task ComingBackToTheTileTheSwipeStartedOnDoesNotPressIt()
    {
        var start = await CentreAsync("a5");
        string under = "";

        await WithMouseAsync(async () =>
        {
            await DownAsync(start);
            await MoveAsync(-80, 0, steps: 2);
            await DraggingAsync();
            await MoveAsync(-120, 0, steps: 3);
            await MoveAsync(200, 0, steps: 5);
            await PulledAsync(80);
            await Task.Delay(200);
            under = await Wrist.EvaluateAsync<string>($"() => document.elementFromPoint({start.X}, {start.Y})?.closest('.tile')?.dataset.id ?? ''");
            await UpAsync();
        });

        Assert.Equal("a5", under);
        await SettledAsync();
        Assert.Equal(0, await Wrist.EvaluateAsync<int>("() => page"));
        Assert.Empty(await PressedAsync());
    }

    [Fact]
    public async Task ASwipeThatMovesMoreUpOrDownThanSidewaysDoesNotTurnThePage()
    {
        var start = await CentreAsync("a5");
        var pulls = new List<double>();

        await WithMouseAsync(async () =>
        {
            await DownAsync(start);
            await MoveAsync(-40, 70, steps: 2);
            pulls.Add(await Wrist.EvaluateAsync<double>("() => pagePull"));
            await MoveAsync(-200, 0, steps: 5);
            pulls.Add(await Wrist.EvaluateAsync<double>("() => pagePull"));
            await MoveAsync(240, -70, steps: 5);
            await UpAsync(holdMs: 200);
        });

        Assert.Equal([0d, 0d], pulls.ToArray());
        Assert.Equal(0, await Wrist.EvaluateAsync<int>("() => page"));
        var pressed = await PressedAsync();
        Assert.Equal(["a5"], pressed);
    }

    /// <summary>外へ引いた分には手応えとして抵抗が付く（指より少ししかずれない）。</summary>
    [Fact]
    public async Task TheEndPagesGiveALittleButDoNotTurn()
    {
        var first = await CentreAsync("a1");
        double given = 0;

        await WithMouseAsync(async () =>
        {
            await DownAsync(first);
            await MoveAsync(300, 0, steps: 6);   // 2 回目（100 px）で越えて、そこから 200 px
            given = await Wrist.EvaluateAsync<double>("() => pageOffset(pagePull)");
            await UpAsync(holdMs: 200);
            await SettledAsync();

            await DownAsync(first);
            await MoveAsync(180, 0, steps: 6);
            await UpAsync();
        });

        Assert.InRange(given, 20, 100);
        await SettledAsync();
        Assert.Equal(0, await Wrist.EvaluateAsync<int>("() => page"));

        await Wrist.EvaluateAsync("() => { page = 1; renderShortcuts(); }");
        var last = await CentreAsync("b2");
        await WithMouseAsync(async () =>
        {
            await DownAsync(last);
            await MoveAsync(-180, 0, steps: 6);
            await UpAsync();
        });

        await SettledAsync();
        Assert.Equal(1, await Wrist.EvaluateAsync<int>("() => page"));
        Assert.Empty(await PressedAsync());
    }

    /// <summary>3 ページの真ん中で試す。端なら、どちらへ間違えても端で止まって見分けられない。</summary>
    [Fact]
    public async Task AFlickBackAgainstTheWayThePagesMovedStaysOnThePage()
    {
        await Wrist.EvaluateAsync("""
            () => {
              const keys = ['b3', 'b4', 'b5', 'b6', 'c1'];
              catalogue = [...catalogue, ...keys.map(key => ({ ...known('b2'), key, name: key, fallback: key }))];
              page = 1;
              renderShortcuts(['a1', 'a2', 'a3', 'a4', 'a5', 'knob', 'b1', 'b2', ...keys]);
            }
            """);
        var start = await CentreAsync("b5");

        // 越えて 280 px 左へ（3 ページ目が覗く）。150 ms 止めて（左へ動いた分は離す直前の速さに入れない）、右へ 120 px 払う。
        var speed = await FlickAsync(start, (-40, 9, 0), (40, 3, 150));
        Assert.True(speed >= 0.5, $"右へ払えていない（{speed:0.00} px/ms）— 試験の前提が崩れている");

        await SettledAsync();
        Assert.Equal(1, await Wrist.EvaluateAsync<int>("() => page"));
        Assert.Empty(await PressedAsync());
    }

    [Fact]
    public async Task TheStatusStripDoesNotTurnThePage()
    {
        var unlock = await Wrist.Locator("#unlock").BoundingBoxAsync() ?? throw new InvalidOperationException("配置のボタンが出ていない");
        double pull = -1;

        await WithMouseAsync(async () =>
        {
            await DownAsync(new Point(unlock.X + unlock.Width / 2, unlock.Y + unlock.Height / 2));
            await MoveAsync(-480, 0, steps: 12);
            pull = await Wrist.EvaluateAsync<double>("() => pagePull");
            await UpAsync(holdMs: 200);
        });

        Assert.Equal(0, pull);
        Assert.Equal(0, await Wrist.EvaluateAsync<int>("() => page"));
    }

    [Fact]
    public async Task ASwipeOnADraggableTileStaysWithTheModule()
    {
        var knob = await CentreAsync("knob");
        double pull = -1;

        await WithMouseAsync(async () =>
        {
            await DownAsync(knob);
            await MoveAsync(-240, 0, steps: 6);
            pull = await Wrist.EvaluateAsync<double>("() => pagePull");
            await MoveAsync(-240, 0, steps: 6);
            await UpAsync(holdMs: 200);
        });

        Assert.Equal(0, pull);
        Assert.Equal(0, await Wrist.EvaluateAsync<int>("() => page"));
        var pointers = await Wrist.EvaluateAsync<string[]>("() => window.__pointers");
        Assert.Equal("knob down", pointers[0]);
        Assert.Equal("knob up", pointers[^1]);
        Assert.Contains("knob move", pointers);
        Assert.Empty(await PressedAsync());
    }

    /// <summary>点の列は板の真ん中なので、半分までは引けない。払って送る。</summary>
    [Fact]
    public async Task TheGapsTheBoardAndTheDotsTurnThePageToo()
    {
        var a4 = await BoxAsync("a4");
        var gap = new Point(a4.X + a4.Width + 5, a4.Y + a4.Height / 2);
        var margin = new Point(720, a4.Y - 5);
        var row = await Wrist.EvaluateAsync<double[]>("() => { const b = el.dots.getBoundingClientRect(); return [b.left + b.width / 2, b.top + b.height / 2]; }");
        var dots = new Point(row[0], row[1]);

        Assert.Equal("pager", await TargetAsync(gap));
        Assert.Equal("board", await TargetAsync(margin));

        foreach (var (name, from) in new[] { ("gap", gap), ("margin", margin) })
        {
            await WithMouseAsync(async () =>
            {
                await DownAsync(from);
                await MoveAsync(-480, 0, steps: 12);
                await UpAsync(holdMs: 200);
            });

            await SettledAsync();
            Assert.Equal((name, 1), (name, await Wrist.EvaluateAsync<int>("() => page")));
            await Wrist.EvaluateAsync("() => { page = 0; renderShortcuts(); }");
        }

        AssertFlicked(await FlickAsync(dots, (-40, 4, 0)));
        await SettledAsync();
        Assert.Equal(("dots", 1), ("dots", await Wrist.EvaluateAsync<int>("() => page")));

        Assert.Empty(await PressedAsync());
    }

    /// <summary>
    /// 当たり（縦は 24 px、横は点の左右 9 px）は隣の点と重なるので、押した所にいちばん近い点に決める。
    /// 1 つ目の点そのものを押したのに、上に重なる 2 つ目の当たりに取られないことを見る。
    /// </summary>
    [Fact]
    public async Task TheDotsAreEasierToHitThanTheyLook()
    {
        var dots = await Wrist.EvaluateAsync<double[]>("""
            () => [...el.dots.children].flatMap(d => { const b = d.getBoundingClientRect(); return [b.left, b.top, b.width, b.height]; })
            """);
        var (top, firstWidth, height) = (dots[1], dots[2], dots[3]);
        var (secondLeft, secondWidth) = (dots[4], dots[6]);
        Assert.Equal((18d, 6d, 6d), (firstWidth, secondWidth, height));

        // 2 つ目の点の右 7 px・下 9 px（板の余白）。
        await ClickAsync(new Point(secondLeft + secondWidth + 7, top + height + 9));
        await SettledAsync();
        Assert.Equal(1, await Wrist.EvaluateAsync<int>("() => page"));

        // いまは 1 つ目が 6 px、2 つ目が 18 px。1 つ目の点の上（右の端）は 2 つ目の当たりにも入っている。
        var now = await Wrist.EvaluateAsync<double[]>("() => { const b = el.dots.children[0].getBoundingClientRect(); return [b.left, b.width, b.top]; }");
        var onFirst = new Point(now[0] + now[1] - 1, now[2] + 3);
        Assert.Equal(1, await Wrist.EvaluateAsync<int>($"() => [...el.dots.children].indexOf(document.elementFromPoint({onFirst.X}, {onFirst.Y}))"));
        await ClickAsync(onFirst);
        await SettledAsync();
        Assert.Equal(0, await Wrist.EvaluateAsync<int>("() => page"));
    }

    /// <summary>
    /// 隣のページのタイルは先に作ってあり、送る途中で iframe が空白にならない。
    /// 次のページへ送って戻っても、今のページのタイルの iframe は同じもので、中の印が残り、load が来ない。
    /// </summary>
    [Fact]
    public async Task TheNextPageIsThereWhileSwipingAndTheShownPageIsNotReloaded()
    {
        await Wrist.WaitForFunctionAsync("""
            () => [...el.tiles.querySelectorAll('.tile.page > iframe')].length === 2
              && [...el.tiles.querySelectorAll('.tile.page > iframe')].every(f => f.contentDocument?.readyState === 'complete')
            """);
        await Wrist.EvaluateAsync("""
            () => {
              window.__frames = [...el.tiles.querySelectorAll('.tile.page > iframe')];
              window.__loads = 0;
              for (const frame of window.__frames) {
                frame.contentWindow.__mark = frame.parentElement.dataset.id;
                frame.addEventListener('load', () => window.__loads++);
              }
            }
            """);

        var start = await CentreAsync("a5");
        double nextLeft = 0;
        await WithMouseAsync(async () =>
        {
            await DownAsync(start);
            await MoveAsync(-80, 0, steps: 2);
            await DraggingAsync();
            await MoveAsync(-200, 0, steps: 5);
            await PulledAsync(-200);
            nextLeft = await Wrist.EvaluateAsync<double>("() => el.tiles.querySelector(\".tile[data-id='b1']\").getBoundingClientRect().left");
            await MoveAsync(-200, 0, steps: 5);
            await UpAsync(holdMs: 200);
            await SettledAsync();
        });

        // 右へ払って 1 ページ目へ戻る。
        AssertFlicked(await FlickAsync(await CentreAsync("b2"), (40, 4, 0)));
        await SettledAsync();

        // 越えて 200 px 引いた所: 2 ページ目の頭（並びの上で 746）は、板の右（746）より 200 px 内側に見えている。
        Assert.Equal(12 + Stride - 200, nextLeft, 1);
        Assert.Equal(0, await Wrist.EvaluateAsync<int>("() => page"));
        var kept = await Wrist.EvaluateAsync<string>("""
            () => window.__frames.map(f => `${f.isConnected && el.tiles.contains(f)} ${f.contentWindow?.__mark ?? '-'}`).join(' | ') + ' loads ' + window.__loads
            """);
        Assert.Equal("true a1 | true b1 loads 0", kept);
    }

    /// <summary>
    /// 同じ名前のタイルが別のページに 2 つあっても（設定を手で書いたとき）、遠くへ外れる方を使い回して置き直すと、
    /// 残るはずのタイルが読み込み直しになる。ここでは 1 ページ目と 2 ページ目に a1（ページを持つタイル）がある。
    /// </summary>
    [Fact]
    public async Task ATileWhoseNameAppearsTwiceStaysPutWhenThePagesTurn()
    {
        await Wrist.EvaluateAsync("""
            () => {
              page = 1;
              renderShortcuts(['a1', 'a2', 'a3', 'a4', 'a5', 'knob', 'a1', 'b2', 'b1', 'a2', 'a3', 'a4', 'a5']);
            }
            """);
        const string Second = "[...el.tiles.querySelectorAll(\".tile[data-id='a1']\")].find(t => t.style.left === '746px')";
        await Wrist.WaitForFunctionAsync($"() => {Second}?.querySelector('iframe').contentDocument?.readyState === 'complete'");

        var kept = await Wrist.EvaluateAsync<string>($$"""
            () => {
              const second = {{Second}};
              second.querySelector('iframe').contentWindow.__mark = 'second';
              page = 2;
              renderShortcuts();
              return `${second.isConnected} ${second.style.left} ${second.querySelector('iframe').contentWindow?.__mark ?? '-'}`;
            }
            """);

        Assert.Equal("true 746px second", kept);
    }

    /// <summary>
    /// 先へ行った分を覚えていると、その分を戻るまで並びが動かない。
    /// 手首の絵は板より広い（右に通知の板がある）ので、レーザーは板の外まで引ける。ここはページの出来事を直に投げる。
    /// </summary>
    [Fact]
    public async Task PullingBackAfterGoingPastTheNextPageMovesThePagesAtOnce()
    {
        var pulls = await Wrist.EvaluateAsync<double[]>("""
            () => {
              const tile = el.tiles.querySelector(".tile[data-id='a5']");
              const box = tile.getBoundingClientRect();
              const at = x => ({ button: 0, pointerId: 1, bubbles: true, clientX: x, clientY: box.top + 20 });
              const x = box.left + 50;
              tile.dispatchEvent(new PointerEvent('pointerdown', at(x)));
              window.dispatchEvent(new PointerEvent('pointermove', at(x - 80)));
              window.dispatchEvent(new PointerEvent('pointermove', at(x - 80 - 900)));
              const far = pagePull;
              window.dispatchEvent(new PointerEvent('pointermove', at(x - 80 - 800)));
              const back = pagePull;
              tile.dispatchEvent(new PointerEvent('pointerup', at(x - 80 - 800)));
              return [far, back];
            }
            """);

        Assert.Equal([-746d, -646d], pulls);
        await SettledAsync();
        Assert.Equal(1, await Wrist.EvaluateAsync<int>("() => page"));
    }

    /// <summary>
    /// 並びの窓が巻かれると、並びのずれ（transform）と関係なくタイルがずれたままになる
    /// （ボタンは押すと focus を取るので、送っている途中に半分見えているタイルを押すと起きうる）。
    /// </summary>
    [Fact]
    public async Task FocusingATileOfTheNextPageDoesNotScrollThePages()
    {
        var scrolled = await Wrist.EvaluateAsync<double>("""
            () => {
              el.tiles.querySelector(".tile[data-id='b2']").focus();
              return el.tiles.parentElement.scrollLeft;
            }
            """);

        Assert.Equal(0, scrolled);
    }

    [Fact]
    public async Task LayoutModeTurnsSwipingOffAndDropsASwipeUnderway()
    {
        var start = await CentreAsync("a5");
        string dropped = "";

        await WithMouseAsync(async () =>
        {
            await Wrist.EvaluateAsync("() => renderLayout({ on: true, width: 0.24 })");
            await DownAsync(start);
            await MoveAsync(-480, 0, steps: 12);
            await UpAsync(holdMs: 200);
            Assert.Equal("0 0 none", await Wrist.EvaluateAsync<string>("() => `${page} ${pagePull} ${pageSwipe?.phase ?? 'none'}`"));
            await Wrist.EvaluateAsync("() => renderLayout({ on: false, width: 0.24 })");

            await DownAsync(start);
            await MoveAsync(-200, 0, steps: 5);
            await DraggingAsync();
            await Wrist.EvaluateAsync("() => renderLayout({ on: true, width: 0.24 })");
            dropped = await Wrist.EvaluateAsync<string>("() => `${pagePull} ${pageSwipe?.phase} ${el.board.classList.contains('swiping')}`");
            await Wrist.EvaluateAsync("() => renderLayout({ on: false, width: 0.24 })");
            await MoveAsync(-280, 0, steps: 7);
            await UpAsync(holdMs: 200);
        });

        Assert.Equal("0 off false", dropped);
        await SettledAsync();
        Assert.Equal(0, await Wrist.EvaluateAsync<int>("() => page"));
        Assert.Empty(await PressedAsync());
    }

    [Fact]
    public async Task RenderingTheShortcutsWhileSwipingDropsTheSwipe()
    {
        var start = await CentreAsync("a5");
        string dropped = "";

        await WithMouseAsync(async () =>
        {
            await DownAsync(start);
            await MoveAsync(-200, 0, steps: 5);
            await DraggingAsync();
            await Wrist.EvaluateAsync("() => renderShortcuts()");
            dropped = await Wrist.EvaluateAsync<string>("() => `${pagePull} ${el.tiles.style.transform} ${pageSwipe?.phase} ${el.board.classList.contains('swiping')}`");
            await MoveAsync(-280, 0, steps: 7);
            await UpAsync(holdMs: 200);
            await SettledAsync();
            Assert.Equal(0, await Wrist.EvaluateAsync<int>("() => page"));

            await DownAsync(start);
            await MoveAsync(-200, 0, steps: 5);
            await DraggingAsync();
            await Wrist.EvaluateAsync("() => renderShortcuts()");
            await MoveAsync(200, 0, steps: 5);
            await UpAsync(holdMs: 200);
        });

        Assert.Equal("0 translateX(0px) off false", dropped);
        await SettledAsync();
        Assert.Equal(0, await Wrist.EvaluateAsync<int>("() => page"));
        Assert.Empty(await PressedAsync());
    }

    /// <summary>
    /// 滑り始め（まだ半分より手前）を掴んで離しても、送る前のページへは戻らず、向かっていたページに着く。
    /// 滑りの途中の位置を決めて押すため、ここだけはページの出来事を直に投げる（指の離しで出る click も投げる。ブラウザが出すかもしれない）。
    /// </summary>
    [Fact]
    public async Task PressingThePagesWhileTheySlideCatchesThem()
    {
        var caught = await Wrist.EvaluateAsync<double[]>("""
            async () => {
              const tile = el.tiles.querySelector(".tile[data-id='a5']");
              const box = tile.getBoundingClientRect();
              const at = x => ({ button: 0, pointerId: 1, bubbles: true, clientX: x, clientY: box.top + 20 });
              // 1 ページ目から 2 ページ目へ送った直後（並びはまだ 2 ページ目の 600 px 手前 — 1 ページ目の方が多く見えている）。
              page = 1;
              renderShortcuts();
              slidePull(600);
              const x = box.left + 20;
              tile.dispatchEvent(new PointerEvent('pointerdown', at(x)));
              const held = pagePull;
              await new Promise(r => setTimeout(r, 100));
              const still = pagePull;
              window.dispatchEvent(new PointerEvent('pointermove', at(x - 10)));
              const followed = pagePull;
              tile.dispatchEvent(new PointerEvent('pointerup', at(x - 10)));
              tile.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, detail: 1 }));
              return [held, still, followed, pageSlide === null ? 0 : 1];
            }
            """);

        Assert.Equal([600d, 600d, 590d, 1d], caught);
        await SettledAsync();
        Assert.Equal(1, await Wrist.EvaluateAsync<int>("() => page"));

        // 2 ページ目へ着く手前（100 px）を掴み、1 ページ目の側へ 400 px 引いて離す。
        await Wrist.EvaluateAsync("""
            () => {
              const tile = el.tiles.querySelector(".tile[data-id='b2']");
              const box = tile.getBoundingClientRect();
              const at = x => ({ button: 0, pointerId: 1, bubbles: true, clientX: x, clientY: box.top + 20 });
              slidePull(100);
              tile.dispatchEvent(new PointerEvent('pointerdown', at(box.left)));
              window.dispatchEvent(new PointerEvent('pointermove', at(box.left + 400)));
              window.dispatchEvent(new PointerEvent('pointerup', at(box.left + 400)));
            }
            """);
        await SettledAsync();
        Assert.Equal(0, await Wrist.EvaluateAsync<int>("() => page"));

        // 1 ページ目から 2 ページ目へ送った直後（600 px 手前）を掴み、向かっていた 2 ページ目の側へ 400 px 引いて離す — 2 ページ目に着く
        // （掴んだ所から半分より多く動いたが、隣のページ（1 ページ目）の側ではない）。
        await Wrist.EvaluateAsync("""
            () => {
              const tile = el.tiles.querySelector(".tile[data-id='a5']");
              const box = tile.getBoundingClientRect();
              const at = x => ({ button: 0, pointerId: 1, bubbles: true, clientX: x, clientY: box.top + 20 });
              page = 1;
              renderShortcuts();
              slidePull(600);
              tile.dispatchEvent(new PointerEvent('pointerdown', at(box.left + 450)));
              window.dispatchEvent(new PointerEvent('pointermove', at(box.left + 50)));
              window.dispatchEvent(new PointerEvent('pointerup', at(box.left + 50)));
            }
            """);
        await SettledAsync();
        Assert.Equal(1, await Wrist.EvaluateAsync<int>("() => page"));
        Assert.Empty(await PressedAsync());
    }

    /// <summary>飲むのは指の離しで出る click だけで、位置の無い click（キーボードの Enter・<c>click()</c>）は飲まない。</summary>
    [Fact]
    public async Task AClickWithoutAPointerRightAfterASwipeStillPresses()
    {
        await Wrist.EvaluateAsync("""
            () => {
              const tile = el.tiles.querySelector(".tile[data-id='a5']");
              const box = tile.getBoundingClientRect();
              const at = x => ({ button: 0, pointerId: 1, bubbles: true, clientX: x, clientY: box.top + 20 });
              tile.dispatchEvent(new PointerEvent('pointerdown', at(box.left + 50)));
              window.dispatchEvent(new PointerEvent('pointermove', at(box.left - 30)));
              window.dispatchEvent(new PointerEvent('pointermove', at(box.left - 130)));
              tile.dispatchEvent(new PointerEvent('pointerup', at(box.left - 130)));
              tile.click();
            }
            """);

        var pressed = await PressedAsync();
        Assert.Equal(["a5"], pressed);
    }

    /// <summary>
    /// VR のレーザーは、押している間の動きも「ボタンを押していない動き」として送る（<c>VrSurface.Send</c> の move は <c>CefEventFlags.None</c>）。
    /// CDP で同じ形（button none・buttons 0）の動きを入れる。押しと離しはボタン付き。手首のページは <c>buttons</c> を見ていない。
    /// </summary>
    [Fact]
    public async Task MovesThatCarryNoButtonSwipeAsTheHeadsetSendsThem()
    {
        await Wrist.EvaluateAsync("() => { window.__buttons = new Set(); window.addEventListener('pointermove', e => window.__buttons.add(e.buttons)); }");
        var start = await CentreAsync("a5");
        var cdp = await Page.Context.NewCDPSessionAsync(Page);
        string during = "";

        await WithMouseAsync(async () =>
        {
            await SendMouseAsync(cdp, "mouseMoved", start, "none", 0);
            await SendMouseAsync(cdp, "mousePressed", start, "left", 1);
            var at = start;
            for (var i = 0; i < 12; i++)
            {
                await Task.Delay(16);
                at = at with { X = at.X - 40 };
                await SendMouseAsync(cdp, "mouseMoved", at, "none", 0);
                if (i == 1) await DraggingAsync();   // 2 回目（80 px）で引き始める
            }

            await PulledAsync(-400);
            during = await Wrist.EvaluateAsync<string>("() => `${pageSwipe?.phase} ${[...window.__buttons].join(',')}`");
            await Task.Delay(200);
            await SendMouseAsync(cdp, "mouseReleased", at, "left", 0);
        });

        Assert.Equal("drag 0", during);
        await SettledAsync();
        Assert.Equal(1, await Wrist.EvaluateAsync<int>("() => page"));
        Assert.Empty(await PressedAsync());
    }

    private static Task SendMouseAsync(ICDPSession cdp, string type, Point at, string button, int buttons) =>
        cdp.SendAsync("Input.dispatchMouseEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["x"] = at.X,
            ["y"] = at.Y,
            ["button"] = button,
            ["buttons"] = buttons,
            ["clickCount"] = type == "mouseMoved" ? 0 : 1,
        });

    /// <summary>離しは本物のマウスでは落とせないので、ページの出来事を直に投げる（デスクトップで窓の外で離したときに当たる）。</summary>
    [Fact]
    public async Task APressWhoseReleaseNeverCameEndsWithTheNextPress()
    {
        var moved = await Wrist.EvaluateAsync<double[]>("""
            () => {
              const tile = el.tiles.querySelector(".tile[data-id='a5']");
              const box = tile.getBoundingClientRect();
              const at = x => ({ button: 0, pointerId: 1, bubbles: true, clientX: x, clientY: box.top + 20 });
              tile.dispatchEvent(new PointerEvent('pointerdown', at(box.left + 50)));
              window.dispatchEvent(new PointerEvent('pointermove', at(box.left - 30)));
              window.dispatchEvent(new PointerEvent('pointermove', at(box.left - 130)));
              const dragged = pagePull;
              // 離しは来ないまま、帯を押して動かす。
              el.unlock.dispatchEvent(new PointerEvent('pointerdown', at(700)));
              window.dispatchEvent(new PointerEvent('pointermove', at(500)));
              return [dragged, pageSwipe === null ? 0 : 1, pageSlide === null ? 0 : 1];
            }
            """);

        Assert.Equal([-100d, 0d, 1d], moved);
        await SettledAsync();
        Assert.Equal(0, await Wrist.EvaluateAsync<int>("() => page"));
    }

    [Fact]
    public async Task OnOnePageNothingSwipes()
    {
        await Wrist.EvaluateAsync("() => renderShortcuts(['a1', 'a2', 'a3', 'a4', 'a5'])");
        var start = await CentreAsync("a5");
        double pull = -1;

        await WithMouseAsync(async () =>
        {
            await DownAsync(start);
            await MoveAsync(-200, 0, steps: 5);
            pull = await Wrist.EvaluateAsync<double>("() => pagePull");
            await MoveAsync(200, 0, steps: 5);
            await UpAsync(holdMs: 200);
        });

        Assert.Equal(0, pull);
        var pressed = await PressedAsync();
        Assert.Equal(["a5"], pressed);
        Assert.True(await Wrist.EvaluateAsync<bool>("() => el.dots.hidden"));
    }

    private readonly record struct Point(double X, double Y);

    private Point _at;

    private async Task<LocatorBoundingBoxResult> BoxAsync(string key) =>
        await Wrist.Locator($".tile[data-id='{key}']").BoundingBoxAsync() ?? throw new InvalidOperationException($"{key} のタイルが出ていない");

    private async Task<Point> CentreAsync(string key)
    {
        var box = await BoxAsync(key);
        return new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    private Task<string> TargetAsync(Point at) =>
        Wrist.EvaluateAsync<string>($"() => {{ const e = document.elementFromPoint({at.X}, {at.Y}); return e.className || e.id; }}");

    private Task<string[]> PressedAsync() => Wrist.EvaluateAsync<string[]>("() => window.__pressed");

    /// <summary>
    /// 引き始めた所（越えた瞬間の指）を決めてから先へ動かす。Chromium は pointermove を描く周ごとにまとめるので、
    /// 続けて動かすと、越えた瞬間がまとめた先の位置になることがある。
    /// </summary>
    private Task DraggingAsync() => Wrist.WaitForFunctionAsync("() => pageSwipe?.phase === 'drag'");

    /// <summary>まとめられた pointermove が届き終わるまで待つ。</summary>
    private Task PulledAsync(double pull) => Wrist.WaitForFunctionAsync($"() => pagePull === {pull}");

    private Task SettledAsync() => Wrist.WaitForFunctionAsync("() => pageSlide === null && pagePull === 0 && pageSwipe === null");

    private async Task DownAsync(Point at)
    {
        _at = at;
        await Page.Mouse.MoveAsync((float)at.X, (float)at.Y);
        await Page.Mouse.DownAsync();
    }

    private async Task MoveAsync(double dx, double dy, int steps, int stepMs = 16)
    {
        var to = new Point(_at.X + dx, _at.Y + dy);
        var from = _at;
        for (var i = 1; i <= steps; i++)
        {
            await Task.Delay(stepMs);
            _at = new Point(from.X + (to.X - from.X) * i / steps, from.Y + (to.Y - from.Y) * i / steps);
            await Page.Mouse.MoveAsync((float)_at.X, (float)_at.Y);
        }
    }

    /// <summary>止めてから離す（<paramref name="holdMs"/> が離す直前の速さを取る 100 ms より長ければ、速さは 0）。</summary>
    private async Task UpAsync(int holdMs = 0)
    {
        if (holdMs > 0) await Task.Delay(holdMs);
        await Page.Mouse.UpAsync();
    }

    /// <summary>
    /// 時間はページの中で刻み、出来事を直に投げる。Playwright の往復はアプリが混むと遅れ、払いが払いでなくなる。click は投げない（払う試験は押しを見ない）。
    /// 返すのは、投げた出来事から測った離す直前の速さ（px/ms。右へが正）。
    /// </summary>
    private Task<double> FlickAsync(Point from, params (double StepPx, int Steps, int PauseMs)[] segments)
    {
        var parts = string.Join(", ", segments.Select(s => $"[{s.StepPx}, {s.Steps}, {s.PauseMs}]"));
        return Wrist.EvaluateAsync<double>($$"""
            async () => {
              const wait = ms => new Promise(r => setTimeout(r, ms));
              const target = document.elementFromPoint({{from.X}}, {{from.Y}});
              const samples = [];
              const send = (node, type, x) => {
                const event = new PointerEvent(type, { button: 0, pointerId: 1, bubbles: true, clientX: x, clientY: {{from.Y}} });
                node.dispatchEvent(event);
                samples.push({ p: x, t: event.timeStamp });
                return event;
              };
              let x = {{from.X}};
              send(target, 'pointerdown', x);
              for (const [step, steps, pause] of [{{parts}}]) {
                if (pause > 0) await wait(pause);
                for (let i = 0; i < steps; i++) { await wait(16); x += step; send(window, 'pointermove', x); }
              }
              const up = send(window, 'pointerup', x);
              return releaseSpeed(samples, up.timeStamp);
            }
            """);
    }

    private static void AssertFlicked(double speed) =>
        Assert.True(Math.Abs(speed) >= 0.5, $"払えていない（{speed:0.00} px/ms）— 試験の前提が崩れている");

    private Task ClickAsync(Point at) => WithMouseAsync(async () =>
    {
        await Page.Mouse.MoveAsync((float)at.X, (float)at.Y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.UpAsync();
    });

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
}
