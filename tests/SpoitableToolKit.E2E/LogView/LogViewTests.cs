using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.LogView;

public sealed class LogViewTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private IPage Page => app.Page;

    public async Task InitializeAsync()
    {
        await Page.Locator("#sidebar .item[data-id='log']").ClickAsync();
        await SelectSource("vrc");
    }

    /// <summary>面も検索も画面の状態なので、落ちたときに後続へ漏れないよう、ここで戻す。</summary>
    public async Task DisposeAsync()
    {
        await Page.Locator("#logSearch").FillAsync("");
        await Page.EvaluateAsync("() => applySearch()");
        await Page.EvaluateAsync("() => closeSheet()");
        if (await Page.Locator("#filterModal").IsVisibleAsync()) await Page.Locator("#filterClose").ClickAsync();
        await SelectSource("vrc");
    }

    /// <summary>前に押した分が回り終わってから押す（回っている間の押下は無視される）。</summary>
    private async Task PressReloadAsync()
    {
        await Assertions.Expect(Page.Locator("#logReload")).Not.ToHaveAttributeAsync("aria-busy", "true", new() { Timeout = 10_000 });
        await Page.Locator("#logReload").ClickAsync();
    }

    /// <summary>絞り込みの窓で 1 つ切り替えて閉じる。畳んでいるかの印まで待つ（押した先が合っているか）。</summary>
    private async Task ToggleKindAsync(string kind)
    {
        await Page.Locator("#logFilter").ClickAsync();
        var item = Page.Locator($"#filters .fitem[data-kind='{kind}']");
        var wasOff = (await item.GetAttributeAsync("class"))?.Split(' ').Contains("off") == true;

        await item.Locator("input.spoitable-switch").ClickAsync();

        var off = new System.Text.RegularExpressions.Regex("(^| )off( |$)");
        if (wasOff) await Assertions.Expect(item).Not.ToHaveClassAsync(off);
        else await Assertions.Expect(item).ToHaveClassAsync(off);

        await Page.Locator("#filterClose").ClickAsync();
    }

    private Task<bool> KindHiddenAsync(string kind) =>
        Page.EvaluateAsync<bool>("kind => hidden.has(kind)", kind);

    private ILocator Sheet => Page.Locator("#logSheet");

    private async Task SelectSource(string name)
    {
        await Page.Locator($"#logTabs .spoitable-tab[data-source='{name}']").ClickAsync();
        await Assertions.Expect(Page.Locator($"#logTabs .spoitable-tab[data-source='{name}']")).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(@"\bspoitable-tab-on\b"));
    }

    private ILocator Rows(string source) => Page.Locator($"#rows{(source == "app" ? "App" : "Vrc")} .row");

    /// <summary>モジュールの読み込みも CEF の起動も画面より先で、push では一度も届いていない。<c>getStatus</c> の台帳から追いつく道が生きていないと空になる。</summary>
    [Fact]
    public async Task TheAppLogLaneShowsTheStartupLines()
    {
        await SelectSource("app");

        await Assertions.Expect(Rows("app").First).ToBeVisibleAsync(new() { Timeout = 10_000 });
        Assert.True(await Rows("app").CountAsync() > 0);

        // 出所（カテゴリ）は末尾だけを出す。完全名は開けば見えるし、ファイルにも残っている。
        var categories = await Page.Locator("#rowsApp .row .cat").AllTextContentsAsync();
        Assert.Contains(categories, text => text.Contains('.') is false && text.Length > 0);
    }

    /// <summary><c>modules/broken/broken.dll</c>（dll でないもの）を置いてあるので、この行は起動のたびに必ず出る。</summary>
    [Fact]
    public async Task OpeningARowWithAnExceptionShowsTheStackTrace()
    {
        await SelectSource("app");

        var row = Page.Locator("#rowsApp .row.has-exception").First;
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await row.Locator(".line").ClickAsync();

        var trace = Sheet.Locator(".trace");
        await Assertions.Expect(trace).ToBeVisibleAsync();
        Assert.Contains("BadImageFormatException", await trace.TextContentAsync() ?? "");
    }

    /// <summary>行は自分で入れる（面の先頭の行は他の試験が入れたもので、単独で走らせると 1 本も無い）。</summary>
    [Fact]
    public async Task ClickingARowOpensItAndClickingAgainCollapsesIt()
    {
        await InjectRowAsync("'e2e-open-close'");

        try
        {
            var row = Page.Locator($"#rowsVrc .row[data-id='{InjectedId}']");
            await Assertions.Expect(row).ToBeVisibleAsync();

            await row.Locator(".line").ClickAsync();
            await Assertions.Expect(Sheet).ToBeVisibleAsync();
            await Assertions.Expect(Sheet.Locator(".title")).ToHaveTextAsync("e2e-open-close");

            // 片道だけだと「常に開く」実装でも緑になる。
            await row.Locator(".line").ClickAsync();
            await Assertions.Expect(Sheet).ToBeHiddenAsync();
        }
        finally
        {
            await RemoveInjectedRowAsync();
        }
    }

    /// <summary>既定で畳まれていることは <c>HostConfigTests</c> で見る（e2e からだと他の試験が書いた値を読むだけになるため）。</summary>
    [Fact]
    public async Task RawLinesShareTheListAsRawAndCollapseViaTheFilter()
    {
        Assert.True(await KindHiddenAsync("raw"), "生行が既定で畳まれていない（前の試験が戻し忘れている）");

        var raw = Page.Locator("#rowsVrc .row[data-kind='raw']");

        try
        {
            // 先に出してから流す。出す切り替えは記録から取り直すので、保存していない生行は取り直しより先に届くと消える。
            await ToggleKindAsync("raw");
            await Page.WaitForTimeoutAsync(500);

            // 構造化できない行を流す。イベントにはならないので、raw としてしか出ようがない。
            app.AppendLog("[E2E] a line that no parser turns into an event");
            await Assertions.Expect(raw.First).ToBeVisibleAsync(new() { Timeout = 20_000 });
            await Assertions.Expect(
                Page.Locator("#rowsVrc .row[data-kind='raw']", new() { HasTextString = "no parser turns" }).First)
                .ToBeVisibleAsync();

            // 畳み直すと消える。片道だけだと「常に出す」実装でも緑になる。
            await ToggleKindAsync("raw");
            await Assertions.Expect(raw.First).ToBeHiddenAsync();
        }
        finally
        {
            if (!await KindHiddenAsync("raw")) await ToggleKindAsync("raw");
        }
    }

    /// <summary>
    /// <c>getStatus</c> の追いつきだけ生きていても、動いている間に出た行が届かない。
    /// 存在しないテーマを選ぶと <c>HostThemes</c> が警告を出すので、それを当てにする（画面の操作から C# のログを 1 行出せる、数少ない道）。
    /// </summary>
    [Fact]
    public async Task AppLogLinesAlsoArriveByPush()
    {
        await SelectSource("app");

        // 検索で絞っておく。今から出る 1 行だけを見たいので、既にある行と区別する。
        await Page.Locator("#logSearch").FillAsync("zzz-missing-theme");
        await Page.EvaluateAsync("() => applySearch()");
        await Assertions.Expect(Page.Locator("#logCount")).ToContainTextAsync("0 件を表示");

        try
        {
            await Page.EvaluateAsync(
                "() => bridge.applyConfigChanges(JSON.stringify({ host: { Theme: 'zzz-missing-theme' } }))");

            await Assertions.Expect(Page.Locator("#logCount")).Not.ToContainTextAsync(
                "0 件を表示", new() { Timeout = 20_000 });
        }
        finally
        {
            await Page.EvaluateAsync(
                "() => bridge.applyConfigChanges(JSON.stringify({ host: { Theme: 'dark' } }))");
        }
    }

    [Fact]
    public async Task SwitchingLanesHidesTheOtherOne()
    {
        await Assertions.Expect(Page.Locator("#rowsVrc")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#rowsApp")).ToBeHiddenAsync();

        await SelectSource("app");

        await Assertions.Expect(Page.Locator("#rowsApp")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#rowsVrc")).ToBeHiddenAsync();
    }

    /// <summary>起動直後の行数ではこの窓は溢れないので、高さを縮めて溢れさせる。</summary>
    [Fact]
    public async Task TheBackToTopBarAppearsOnlyOnceYouHaveScrolledAway()
    {
        await SelectSource("app");
        await Assertions.Expect(Rows("app").First).ToBeVisibleAsync(new() { Timeout = 10_000 });

        try
        {
            await Page.EvaluateAsync(
                "() => { el.rowsApp.style.maxHeight = '48px';"
                + " el.rowsApp.scrollTop = el.rowsApp.scrollHeight;"
                + " el.rowsApp.dispatchEvent(new Event('scroll')); }");

            await Assertions.Expect(Page.Locator("#toTop")).ToBeVisibleAsync();

            await Page.Locator("#toTop").ClickAsync();
            await Assertions.Expect(Page.Locator("#toTop")).ToBeHiddenAsync();

            // 隠れただけでは足りない。隠すのと動かすのは別の行なので、片方だけ生きていても緑になる。
            // 滑らかに巻くので、上端に着くまで待つ。
            await Page.WaitForFunctionAsync("() => el.rowsApp.scrollTop < 2", null, new() { Timeout = 5_000 });
        }
        finally
        {
            await Page.EvaluateAsync("() => { el.rowsApp.style.maxHeight = ''; }");
        }
    }

    private async Task ScrollTheAppLaneAwayAsync()
    {
        await SelectSource("app");
        await Assertions.Expect(Rows("app").First).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.EvaluateAsync(
            "() => { el.rowsApp.style.maxHeight = '48px';"
            + " el.rowsApp.scrollTop = el.rowsApp.scrollHeight;"
            + " el.rowsApp.dispatchEvent(new Event('scroll')); }");
        await Assertions.Expect(Page.Locator("#toTop")).ToBeVisibleAsync();
    }

    private async Task PutTheAppLaneBackAsync()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Null });
        await Page.EvaluateAsync(
            "() => { headingTop.app = false; el.rowsApp.style.maxHeight = ''; el.rowsApp.scrollTop = 0;"
            + " el.rowsApp.dispatchEvent(new Event('scroll')); }");
    }

    /// <summary>出ても一覧の高さが変わらない（浮かせてあるので、出た瞬間に読んでいる行がずれない）。</summary>
    [Fact]
    public async Task TheBackToTopButtonFloatsAtTheBottomWithoutMovingTheRows()
    {
        await SelectSource("app");
        await Assertions.Expect(Rows("app").First).ToBeVisibleAsync(new() { Timeout = 10_000 });

        try
        {
            const string measure =
                """
                () => {
                  const r = el.rowsApp.getBoundingClientRect();
                  const f = document.querySelector('#viewLog .tfoot').getBoundingClientRect();
                  return { top: r.top, height: r.height, foot: f.top };
                }
                """;
            await Page.EvaluateAsync("() => { el.rowsApp.style.maxHeight = '48px'; }");
            var before = await Page.EvaluateAsync<JsonElement>(measure);

            await ScrollTheAppLaneAwayAsync();
            var after = await Page.EvaluateAsync<JsonElement>(measure);

            Assert.Equal(before.GetProperty("top").GetDouble(), after.GetProperty("top").GetDouble(), 1);
            Assert.Equal(before.GetProperty("height").GetDouble(), after.GetProperty("height").GetDouble(), 1);
            Assert.Equal(before.GetProperty("foot").GetDouble(), after.GetProperty("foot").GetDouble(), 1);

            var place = await Page.EvaluateAsync<JsonElement>(
                """
                () => {
                  const b = el.toTop.getBoundingClientRect();
                  const f = document.querySelector('#viewLog .tfoot').getBoundingClientRect();
                  const em = parseFloat(getComputedStyle(el.toTop).fontSize);
                  return { gap: f.top - b.bottom, em };
                }
                """);
            var gap = place.GetProperty("gap").GetDouble();
            Assert.InRange(gap, 0, 2 * place.GetProperty("em").GetDouble());
        }
        finally
        {
            await PutTheAppLaneBackAsync();
        }
    }

    /// <summary>
    /// 滑っている途中で追従が切れない。途中の巻きの知らせはまだ上端から遠いので、そのまま読むとボタンがまた出て、
    /// 届いた行の <c>scrollTop +=</c> が滑りを止めて途中に置き去りにする。
    /// 滑らかかどうかは時間で測らない（<c>behavior: 'smooth'</c> はその場では scrollTop を変えず、即時なら変える）。
    /// OS の「動きを減らす」に左右されないよう、no-preference に固定する。
    /// </summary>
    [Fact]
    public async Task BackToTopGlidesAndTheButtonStaysAwayOnTheWay()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });

        try
        {
            await ScrollTheAppLaneAwayAsync();

            var onClick = await Page.EvaluateAsync<JsonElement>(
                """
                () => {
                  const from = el.rowsApp.scrollTop;
                  el.toTop.click();
                  const at = el.rowsApp.scrollTop;
                  el.rowsApp.dispatchEvent(new Event('scroll'));   // 滑っている途中の知らせ
                  return { from, at, hidden: el.toTopWrap.hidden, stuck: stuck.app };
                }
                """);
            Assert.True(onClick.GetProperty("from").GetDouble() > 50, "下端まで巻けていない");
            Assert.True(onClick.GetProperty("at").GetDouble() > 50, "押したその場で上端に飛んだ（滑らかでない）");
            Assert.True(onClick.GetProperty("hidden").GetBoolean(), "滑っている途中でボタンがまた出た");
            Assert.True(onClick.GetProperty("stuck").GetBoolean(), "滑っている途中で追従が切れた");

            await Page.WaitForFunctionAsync("() => el.rowsApp.scrollTop < 2 && !headingTop.app", null, new() { Timeout = 5_000 });

            await Page.EvaluateAsync(
                "() => { el.rowsApp.scrollTop = el.rowsApp.scrollHeight; el.rowsApp.dispatchEvent(new Event('scroll')); }");
            await Assertions.Expect(Page.Locator("#toTop")).ToBeVisibleAsync();

            var grabbed = await Page.EvaluateAsync<JsonElement>(
                """
                () => {
                  el.toTop.click();
                  el.rowsApp.dispatchEvent(new WheelEvent('wheel', { deltaY: 100 }));
                  el.rowsApp.dispatchEvent(new Event('scroll'));
                  return { hidden: el.toTopWrap.hidden, stuck: stuck.app, heading: headingTop.app };
                }
                """);
            Assert.False(grabbed.GetProperty("heading").GetBoolean(), "巻いたのに滑りの印が残った");
            Assert.False(grabbed.GetProperty("stuck").GetBoolean(), "巻いたのに追従したまま");
            Assert.False(grabbed.GetProperty("hidden").GetBoolean(), "巻いたのにボタンが出ない");

            // 滑りが上端に着く前に終わったら（何かに止められたら）、終わった位置で決め直す。
            await Page.WaitForFunctionAsync("() => el.rowsApp.scrollTop < 2", null, new() { Timeout = 5_000 });
            await Page.EvaluateAsync(
                "() => { el.rowsApp.scrollTop = el.rowsApp.scrollHeight; el.rowsApp.dispatchEvent(new Event('scroll')); }");
            var stopped = await Page.EvaluateAsync<JsonElement>(
                """
                () => {
                  el.toTop.click();
                  el.rowsApp.dispatchEvent(new Event('scrollend'));   // まだ下端に居るうちに終わった
                  return { hidden: el.toTopWrap.hidden, stuck: stuck.app };
                }
                """);
            Assert.False(stopped.GetProperty("stuck").GetBoolean(), "途中で終わったのに追従したまま");
            Assert.False(stopped.GetProperty("hidden").GetBoolean(), "途中で終わったのにボタンが出ない");
        }
        finally
        {
            await PutTheAppLaneBackAsync();
        }
    }

    /// <summary>
    /// 隠すと滑りが止まり、scrollend が来ないことがある。来ないと「上に居る」扱いのまま残り、
    /// 戻ってきたときにボタンが出ず、届いた行で上端へ引き戻される。
    /// </summary>
    [Fact]
    public async Task SwitchingLanesMidGlideFinishesTheGlide()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });

        try
        {
            await ScrollTheAppLaneAwayAsync();

            await Page.EvaluateAsync("() => { el.toTop.click(); selectSource('vrc'); }");
            Assert.False(await Page.EvaluateAsync<bool>("() => headingTop.app"), "面を替えても滑りの印が残った");

            await SelectSource("app");
            Assert.True(await Page.EvaluateAsync<bool>("() => el.rowsApp.scrollTop < 2"), "上端で済んでいない");
            await Assertions.Expect(Page.Locator("#toTop")).ToBeHiddenAsync();
        }
        finally
        {
            await PutTheAppLaneBackAsync();
        }
    }

    [Fact]
    public async Task WithReducedMotionTheJumpsAreInstant()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });

        try
        {
            await ScrollTheAppLaneAwayAsync();

            var onClick = await Page.EvaluateAsync<JsonElement>(
                "() => { el.toTop.click(); return { at: el.rowsApp.scrollTop, heading: headingTop.app }; }");
            Assert.True(onClick.GetProperty("at").GetDouble() < 2, "動きを減らしているのに滑った");
            Assert.False(onClick.GetProperty("heading").GetBoolean());

            Assert.True(await StepSheetMovesAtOnceAsync(), "動きを減らしているのにシートの ↑↓ が滑った");
        }
        finally
        {
            await Page.EvaluateAsync("() => closeSheet()");
            await PutTheAppLaneBackAsync();
        }
    }

    [Fact]
    public async Task TheSheetArrowsGlideToTheNextRow()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });

        try
        {
            await ScrollTheAppLaneAwayAsync();
            Assert.False(await StepSheetMovesAtOnceAsync(), "シートの ↑↓ がその場で飛んだ（滑らかでない）");
        }
        finally
        {
            await Page.EvaluateAsync("() => closeSheet()");
            await PutTheAppLaneBackAsync();
        }
    }

    /// <summary>3 行目のシートを開いて ↓ で 4 行目へ。その場で巻けたら true（面は 48px なので 4 行目は必ず外にある）。</summary>
    private Task<bool> StepSheetMovesAtOnceAsync() =>
        Page.EvaluateAsync<bool>(
            """
            () => {
              const rows = visibleRows();
              el.rowsApp.scrollTo({ top: 0, behavior: 'instant' });
              openSheet(rows[2]);
              el.rowsApp.scrollTo({ top: 0, behavior: 'instant' });
              stepSheet(1);
              return el.rowsApp.scrollTop > 0;
            }
            """);

    [Fact]
    public async Task SearchingShowsBothTheFilteredAndTheTotalCount()
    {
        await SelectSource("app");
        await Assertions.Expect(Rows("app").First).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // 数えるのと確かめるのを 1 回にまとめる。アプリのログは試験の最中にも増えるので、先に数えた数と食い違う。
        const string Count = """
            shown => {
              const total = document.querySelectorAll('#rowsApp .row').length;
              return document.querySelector('#logCount').textContent === `${shown ?? total} 件を表示（${total} 件中）`;
            }
            """;

        await Page.WaitForFunctionAsync(Count);

        await Page.Locator("#logSearch").FillAsync("まず一致しない文字列 zzz-no-match");
        await Page.WaitForFunctionAsync(Count, 0);

        await Page.Locator("#logSearch").FillAsync("");
        await Page.WaitForFunctionAsync(Count);
    }

    /// <summary>
    /// 1 列にしたので生行とイベントが同じ枠（<c>MAX_ROWS</c>）を取り合う。実ログの比は生行 15,000 に対してイベント 19 なので、
    /// 古い順に捨てると枠が全部 raw で埋まる。偽のログでは比が出ないので、ここでは本物と同じだけ生行を流し込む。
    /// </summary>
    [Fact]
    public async Task CollapsedRawLinesDoNotPushRowsOut()
    {
        await EmptyTheVrcLaneOnScreenAsync();

        app.AppendLog(
            "[Behaviour] Entering Room: E2E Squeeze World",
            "[Behaviour] OnPlayerJoined e2e-squeeze-user");

        var target = Page.Locator("#rowsVrc .row[data-kind='PlayerJoined']",
            new() { HasTextString = "e2e-squeeze-user" });
        await Assertions.Expect(target).ToHaveCountAsync(1, new() { Timeout = 20_000 });

        // 枠（500）を大きく超える生行で押す。畳んである方から先に捨てないと、これで流される。
        for (var batch = 0; batch < 4; batch++)
        {
            app.AppendLog([.. Enumerable.Range(0, 200).Select(n => $"[E2E] squeeze {batch}-{n}")]);
        }

        // 枠が埋まり切るまで待つ。「raw が 1 行でも出たら」で判定すると、押し出しが起きる前に通ってしまう。
        // 打ち切りはちょうど MAX_ROWS で止まるので、その数に達したこと自体が「捨てが走った」の証拠になる。
        await Assertions.Expect(Page.Locator("#rowsVrc .row:not([data-paged])"))
            .ToHaveCountAsync(500, new() { Timeout = 30_000 });

        await Assertions.Expect(target).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task ReloadingBringsBackTheClearedRows()
    {
        await SelectSource("app");
        await Assertions.Expect(Rows("app").First).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var before = await Rows("app").CountAsync();
        Assert.True(before > 0);

        await Page.EvaluateAsync("() => { el.rowsApp.replaceChildren(); updateCount(); }");
        Assert.Equal(0, await Rows("app").CountAsync());

        try
        {
            await PressReloadAsync();
            await Assertions.Expect(Rows("app")).ToHaveCountAsync(before, new() { Timeout = 10_000 });
        }
        finally
        {
            // 引き直しが壊れていても戻す（同じアプリを共有する、アプリ側の行を要る他の試験が道連れになる）。
            // reloadRows そのものは使わない。壊れているときに戻せなくなる。
            await Page.EvaluateAsync(
                "async () => { const s = await bridge.getStatus();"
                + " el.rowsApp.replaceChildren();"
                + " addRows('app', [...(s.appLogs ?? [])].reverse(), renderAppLog, { older: true }); }");
        }
    }

    /// <summary>
    /// 前の起動の行は本物の起動し直しでしか作れないので、台帳の中身だけ差し替える（形は本物と同じ）。
    /// 印は行ではない: 打ち切り（500 行）に数えない・境目の片側が無くなったら残さない。
    /// </summary>
    [Fact]
    public async Task CaughtUpRowsComeNewestFirstWithTheRestartMarked()
    {
        await SelectSource("app");
        await Assertions.Expect(Rows("app").First).ToBeVisibleAsync(new() { Timeout = 10_000 });

        Assert.True(await Page.EvaluateAsync<bool>(
            "async () => (await bridge.getStatus()).appLogs.every(a => a.run > 0)"));

        var seen = await Page.EvaluateAsync<string[]>("""
            async () => {
              const realGetStatus = bridge.getStatus;
              const savedSeen = seam.app.seen;
              const shape = () => {
                const kids = [...el.rowsApp.children];
                const seqs = kids.filter(c => c.classList.contains('row')).map(c => Number(c.dataset.seq));
                const marks = kids.flatMap((c, i) => c.classList.contains('run-mark')
                  ? [`${kids[i - 1]?.dataset.seq}|${c.textContent}|${kids[i + 1]?.dataset.seq}`] : []);
                const descending = seqs.every((seq, i) => i === 0 || seqs[i - 1] > seq);
                return `rows=${seqs.length} top=${seqs[0]} bottom=${seqs.at(-1)} descending=${descending} marks=${marks.join(',')}`;
              };
              try {
                // 重複弾きを戻す。偽の番号は本物より小さいので、そのままだと流れの 1 行が「もう見た」で捨てられる。
                seam.app.seen = 0;
                const earlier = new Date('2026-11-09T22:15:30').getTime();
                const later = new Date('2026-11-10T08:05:09').getTime();
                const row = (seq, run) => ({
                  seq, run, ts: new Date(run + seq * 1000).toISOString(), level: 'Information', shortLevel: 'INF',
                  category: 'E2E', fullCategory: 'SpoitableToolKit.Host.E2E', thread: 1, message: 'e2e-run-' + seq, exception: null,
                });

                // 台帳と同じ向き（古い順）。前の起動の 300 行と、この起動の 200 行で、ちょうど打ち切り（500）。
                const ledger = [
                  ...Array.from({ length: 300 }, (_, i) => row(i + 1, earlier)),
                  ...Array.from({ length: 200 }, (_, i) => row(i + 301, later)),
                ];
                bridge.getStatus = async () => ({ ...(await realGetStatus()), appLogs: ledger });
                await fillFromStore('app', { replace: true });
                const filled = shape();

                // 流れてきた 1 行（この起動）。上に挿さり、一番古い 1 行だけが打ち切りで落ちる —
                // **印は行に数えない**（数えると 2 行落ちる）。印は 1 つのまま。
                stk.receive('appLogs', [row(501, later)]);
                const pushed = shape();

                // 境目の下の行が無くなったら（打ち切り・消す）、印も残さない。
                for (const old of el.rowsApp.querySelectorAll(':scope > .row')) if (Number(old.dataset.seq) <= 300) old.remove();
                markRuns();
                const alone = shape();

                return [filled, pushed, alone];
              } finally {
                bridge.getStatus = realGetStatus;
                seam.app.seen = savedSeen;
                await fillFromStore('app', { replace: true });
              }
            }
            """);

        const string mark = "301|起動 2026/11/10 08:05:09|300";
        Assert.Equal(
            [
                $"rows=500 top=500 bottom=1 descending=true marks={mark}",
                $"rows=500 top=501 bottom=2 descending=true marks={mark}",
                "rows=201 top=501 bottom=301 descending=true marks=",
            ],
            seen);
    }

    /// <summary>続きは一番下の行の番号から引くので、打ち切りで落ちた行も穴にならない。</summary>
    [Fact]
    public async Task ScrollingDownTheAppLaneGoesBackThroughTheRecord()
    {
        await SelectSource("app");
        await Assertions.Expect(Rows("app").First).ToBeVisibleAsync(new() { Timeout = 10_000 });

        try
        {
            var second = await Page.EvaluateAsync<long>("""
                () => {
                  const rows = [...el.rowsApp.querySelectorAll(':scope > .row')];
                  for (const row of rows.slice(2)) row.remove();
                  appPage.exhausted = false;
                  el.rowsApp.scrollTop = el.rowsApp.scrollHeight;
                  el.rowsApp.dispatchEvent(new Event('scroll'));
                  return Number(rows[1].dataset.seq);
                }
                """);

            await Assertions.Expect(Page.Locator("#rowsApp .row[data-paged]")).Not.ToHaveCountAsync(0, new() { Timeout = 10_000 });

            var ledger = await Page.EvaluateAsync<long[]>(
                $"async () => (await bridge.getStatus()).appLogs.map(a => a.seq).filter(seq => seq < {second})");
            var paged = await Page.EvaluateAsync<long[]>(
                "() => [...document.querySelectorAll('#rowsApp .row[data-paged]')].map(row => Number(row.dataset.seq))");
            Assert.NotEmpty(ledger);
            Assert.All(ledger, seq => Assert.Contains(seq, paged));

            Assert.True(await Page.EvaluateAsync<bool>("""
                () => {
                  const seqs = [...el.rowsApp.querySelectorAll(':scope > .row')].map(row => Number(row.dataset.seq));
                  return seqs.every((seq, i) => i === 0 || seqs[i - 1] > seq);
                }
                """), "遡りの継ぎ目で並びが崩れている");

            if (await Page.EvaluateAsync<int>("() => page.size") > ledger.Length + 100)
                await Assertions.Expect(Page.Locator("#logMoreText")).ToHaveTextAsync("これより古いログはありません");

            await Page.EvaluateAsync("() => { moreText.vrc = ''; }");
            await SelectSource("vrc");
            await Assertions.Expect(Page.Locator("#logMore")).ToBeHiddenAsync();
        }
        finally
        {
            await SelectSource("app");
            await Page.EvaluateAsync("() => fillFromStore('app', { replace: true })");
        }
    }

    [Fact]
    public async Task NewRowsStackOnTop()
    {
        app.AppendLog("[Behaviour] OnPlayerJoined e2e-order-first");
        await Assertions.Expect(Joined("e2e-order-first")).ToHaveCountAsync(1, new() { Timeout = 20_000 });

        app.AppendLog("[Behaviour] OnPlayerJoined e2e-order-second");
        await Assertions.Expect(Joined("e2e-order-second")).ToHaveCountAsync(1, new() { Timeout = 20_000 });

        // 面には前の試験の行も並んでいる。**自分の 2 行の順**だけを見る。
        var order = await Page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll(\"#rowsVrc .row[data-kind='PlayerJoined'] .body\")]"
            + ".map(b => b.textContent).filter(text => text.startsWith('e2e-order-'))");

        Assert.Equal(["e2e-order-second", "e2e-order-first"], order);
    }

    /// <summary>
    /// 畳んである行から先に捨てるだけだと、raw を出した瞬間に守りが消える。raw は「捨てていい行」から外れ、古い順の方が働いてイベント行を食う。
    /// 落としたイベントは DB から引き直せるが、落とした生行は引き直せない。それでも生行を先に手放すのは、比が生行 15,000 に対してイベント 19 だから。
    /// </summary>
    [Fact]
    public async Task TheRowsYouWantSurviveEvenWithRawLinesShown()
    {
        await EmptyTheVrcLaneOnScreenAsync();

        await ToggleKindAsync("raw");

        try
        {
            app.AppendLog(
                "[Behaviour] Entering Room: E2E Raw World",
                "[Behaviour] OnPlayerJoined e2e-raw-user");

            var target = Joined("e2e-raw-user");
            await Assertions.Expect(target).ToHaveCountAsync(1, new() { Timeout = 20_000 });

            // 枠（500）を大きく超える生行で押す。
            for (var batch = 0; batch < 4; batch++)
                app.AppendLog([.. Enumerable.Range(0, 200).Select(n => $"[E2E] raw-visible {batch}-{n}")]);

            // 枠が埋まり切るまで待つ（途中で見ると、押し出しが起きる前に通ってしまう）。
            // 枠が効くのは流れてきた行だけ。遡って取りに行った行は数えないし捨てない（下へ辿ったのに上へ戻ると無い、を避けるため）。
            // 取り直しが走ると混ざる。
            await Assertions.Expect(Page.Locator("#rowsVrc .row:not([data-paged])"))
                .ToHaveCountAsync(500, new() { Timeout = 30_000 });

            await Assertions.Expect(target).ToHaveCountAsync(1);
        }
        finally
        {
            // 畳んだ種別はホストの設定に残る。戻さないと後続の試験が別の既定で走る。
            if (!await KindHiddenAsync("raw")) await ToggleKindAsync("raw");
        }
    }

    /// <summary>読み戻す経路と流れてくる経路は別なので、境目で同じ行が 2 回出る。DB が採番した id で弾いている。</summary>
    [Fact]
    public async Task RefetchingDoesNotDuplicateRows()
    {
        app.AppendLog("[Behaviour] OnPlayerJoined e2e-dup-user");
        await Assertions.Expect(Joined("e2e-dup-user")).ToHaveCountAsync(1, new() { Timeout = 20_000 });

        await PressReloadAsync();
        await Assertions.Expect(Joined("e2e-dup-user")).ToHaveCountAsync(1, new() { Timeout = 20_000 });

        // 継ぎ目の重複は「読み戻した分と押し出しの分」なので、1 行だけでなく面全体で通し番号が重なっていないことまで見る。
        var duplicated = await Page.EvaluateAsync<int>(
            "() => { const ids = [...document.querySelectorAll('#rowsVrc .row[data-id]')]"
            + ".map(r => r.dataset.id); return ids.length - new Set(ids).size; }");

        Assert.Equal(0, duplicated);
    }

    /// <summary>入れる行の id。<b>本物と当たらない大きさ</b>にしてある。</summary>
    private const int InjectedId = 990401;

    /// <summary>行を 1 本入れる。並んでいる行は他の試験が入れたもので、単独で走らせると 1 本も無いので、面が出ていることだけ待つ。</summary>
    private async Task InjectRowAsync(string title, string kind = "PlayerJoined")
    {
        await Assertions.Expect(Page.Locator("#rowsVrc")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await Page.EvaluateAsync($$"""
            () => {
              // **見た印は戻す。** 大きい id を残すと、後続の試験で本物の行が
              // 「もう見た」と判定されて出なくなる。
              const saved = seam.vrc.seen;
              try {
                stk.receive('logEvents', [{
                  id: {{InjectedId}}, kind: '{{kind}}', ts: new Date().toISOString(),
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

    private async Task<double> MeasureInjectedRowAsync(string script, string title, string kind = "PlayerJoined")
    {
        await InjectRowAsync(title, kind);

        try
        {
            return await Page.EvaluateAsync<double>($$"""
                () => {
                  const row = document.querySelector("#rowsVrc .row[data-id='{{InjectedId}}']");
                  return row ? ({{script}})(row) : -1;
                }
                """);
        }
        finally
        {
            await RemoveInjectedRowAsync();
        }
    }

    /// <summary>開く前と後の高さの差で見る。変わらないことが要件そのもので、字の大きさにも縛られない。</summary>
    [Fact]
    public async Task OpeningARowDoesNotUnfoldItsPreview()
    {
        // 折り返せば必ず伸びる長さ。切られていれば 1 行のまま。
        const string longTitle = "'https://example.test/' + 'x'.repeat(2000)";

        var grew = await MeasureInjectedRowAsync(
            """
            row => {
              const body = row.querySelector('.body');
              const closed = body.getBoundingClientRect().height;
              row.querySelector('.line').click();
              return body.getBoundingClientRect().height - closed;
            }
            """,
            longTitle,
            "VideoPlaybackFailed");

        Assert.True(grew >= 0, "行が出ていない");
        Assert.Equal(0, grew, 1);
    }

    /// <summary>
    /// 折り返せる文字列で見る。切れ目の無い文字列だと、切る指定を外しても折り返しようがなく横へ溢れるだけなので、
    /// 「切っている」と「溢れている」の区別が付かない。語の切れ目があれば、切る指定を外した瞬間に何行にもなる。
    /// </summary>
    [Fact]
    public async Task ALongPreviewIsKeptToOneLine()
    {
        var height = await MeasureInjectedRowAsync(
            """
            row => row.querySelector('.body').getBoundingClientRect().height
            """,
            "'word '.repeat(400)");

        Assert.InRange(height, 1, 30);
    }

    /// <summary>入りきらない分が実際にある。これが無いと、短い行しか出ていない画面でも上の試験は通る。</summary>
    [Fact]
    public async Task ALongPreviewActuallyHasMoreToShow()
    {
        var overflow = await MeasureInjectedRowAsync(
            """
            row => {
              const body = row.querySelector('.body');
              return body.scrollWidth - body.clientWidth;
            }
            """,
            "'word '.repeat(400)");

        Assert.True(overflow > 0, "入りきっている（試験になっていない）");
    }

    /// <summary>シートにしたので、開いても一覧の並びは動かない。代わりに、シートが行に被さると開いた行そのものが読めなくなる。</summary>
    [Fact]
    public async Task TheDetailOpensInASheetBesideTheList()
    {
        await InjectRowAsync("'e2e-detail-sheet'");

        try
        {
            var row = Page.Locator($"#rowsVrc .row[data-id='{InjectedId}']");
            await row.Locator(".line").ClickAsync();
            await Assertions.Expect(Sheet).ToBeVisibleAsync();

            var (sheet, line) = (await Sheet.BoundingBoxAsync(), await row.Locator(".line").BoundingBoxAsync());
            Assert.True(sheet!.X >= line!.X + line.Width - 1,
                $"シートが行に被さっている（行の右端 {line.X + line.Width}、シートの左端 {sheet.X}）");

            await Assertions.Expect(row).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bsel\b"));

            await Page.Keyboard.PressAsync("Escape");
            await Assertions.Expect(Sheet).ToBeHiddenAsync();
        }
        finally
        {
            await RemoveInjectedRowAsync();
        }
    }

    /// <summary>DB を遡ると何日も前の行が同じ一覧に並ぶので、日付が無いと 8/30 の 16:17 と今日の 16:17 が見分けられない。常に日付を出す。</summary>
    [Fact]
    public async Task EveryRowCarriesTheSameShapeOfTimestamp()
    {
        // 90 日前。どの時間帯で走らせても「今日ではない」ことが決まる取り方。
        var past = await Page.EvaluateAsync<string>(
            "() => stamp(new Date(Date.now() - 90 * 86400000).toISOString())");
        var today = await Page.EvaluateAsync<string>("() => stamp(new Date().toISOString())");

        Assert.Matches(@"^\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2}$", past);
        Assert.Matches(@"^\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2}$", today);

        Assert.StartsWith($"{DateTime.Now:yyyy/MM/dd}", today);
    }

    [Fact]
    public async Task TheAppLaneUsesTheSameTimestampShape()
    {
        await SelectSource("app");
        await Assertions.Expect(Rows("app").First).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var stamps = await Page.Locator("#rowsApp .row .ts").AllTextContentsAsync();

        Assert.NotEmpty(stamps);
        Assert.All(stamps, s => Assert.Matches(@"^\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2}$", s));

        // 列が足りているか。日付が付いて 19 桁になったので、狭いままだと折り返して行の高さが倍になる。
        var fits = await Page.EvaluateAsync<bool>(
            "() => { const t = document.querySelector('#rowsApp .row .ts'); "
            + "return t.scrollWidth <= t.clientWidth; }");

        Assert.True(fits, "時刻の列が狭くて溢れている");
    }

    /// <summary>
    /// 打ち切りの最後の段。ふだんは畳んである行と生行だけで枠が空くので通らないが、見える種別だけで枠を超えるとここだけが働く。
    /// 向きを間違えると、いちばん見たい「いま来た行」から消えていく。
    /// </summary>
    [Fact]
    public async Task OverTheCapTheOldestRowsAreDropped()
    {
        await EmptyTheVrcLaneOnScreenAsync();

        // 生行は既定で畳んであるので、先に手放されて枠が空く。見えるイベントだけで枠（500）を超えさせて、最後の段まで進める。
        app.AppendLog([.. Enumerable.Range(0, 600).Select(n => $"[Behaviour] OnPlayerJoined e2e-flood-{n:D3}")]);

        // 一番新しい行が届いてから数える。面には前から行が並んでいるので、流し終える前に 500 に届くことがある。
        await Assertions.Expect(Joined("e2e-flood-599")).ToHaveCountAsync(1, new() { Timeout = 60_000 });
        await Assertions.Expect(Page.Locator("#rowsVrc .row[data-kind='PlayerJoined']:not([data-paged])"))
            .ToHaveCountAsync(500, new() { Timeout = 20_000 });

        // 新しい方が残り、古い方が消えている（600 行のうち新しい 500 行で枠が埋まる）。
        await Assertions.Expect(Joined("e2e-flood-100")).ToHaveCountAsync(1);
        await Assertions.Expect(Joined("e2e-flood-000")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// C# 側は読んだ順（古い順）で渡してくるので、そのまま上へ挿すと束の中だけ順が逆になる。
    /// ワールド遷移の瞬間は 1 秒で数百行が届くので、束はふつうに複数行になる。
    /// </summary>
    [Fact]
    public async Task WithinOneBatchTheNewestStillComesFirst()
    {
        // 1 回の呼び出しで届いたことにする。ファイルに 2 行書いても、同じ束に入るかは押し出しの周期次第なので、運に任せない。
        var order = await Page.EvaluateAsync<string[]>("""
            () => {
              const at = s => new Date(Date.now() - s * 1000).toISOString();
              addRows('vrc', [
                { id: 990001, kind: 'PlayerJoined', ts: at(2), title: 'e2e-batch-older', detail: '' },
                { id: 990002, kind: 'PlayerJoined', ts: at(1), title: 'e2e-batch-newer', detail: '' },
              ], renderEvent, {});

              const mine = [...document.querySelectorAll("#rowsVrc .row[data-kind='PlayerJoined'] .body")]
                .map(b => b.textContent).filter(text => text.startsWith('e2e-batch-'));
              document.querySelectorAll("#rowsVrc .row[data-id='990001'], #rowsVrc .row[data-id='990002']").forEach(row => row.remove());
              return mine;
            }
            """);

        Assert.Equal(["e2e-batch-newer", "e2e-batch-older"], order);
    }

    /// <summary>読み戻した分と押し出しの分は必ず重なる。DB が採番した id で弾いている。</summary>
    [Fact]
    public async Task TheSameRowArrivingTwiceIsShownOnce()
    {
        var rows = await Page.EvaluateAsync<int>("""
            () => {
              // **見た印は戻す。** 大きい id を入れたままにすると、
              // 後続の試験で本物の行が「もう見た」と判定されて出なくなる。
              const saved = seam.vrc.seen;
              try {
                const row = { id: 990101, kind: 'PlayerJoined',
                              ts: new Date().toISOString(), title: 'e2e-seam-user', detail: '' };
                stk.receive('logEvents', [row]);
                stk.receive('logEvents', [row]);
                return document.querySelectorAll("#rowsVrc .row[data-id='990101']").length;
              } finally {
                seam.vrc.seen = saved;
                document.querySelectorAll("#rowsVrc .row[data-id='990101']").forEach(row => row.remove());
              }
            }
            """);

        Assert.Equal(1, rows);
    }

    /// <summary><c>getStatus</c> を呼んでから描き終わるまでの隙に届いた行は、読み戻した側にも入っている。読み戻しのあいだ push を溜めて塞いでいる。</summary>
    [Fact]
    public async Task ARowArrivingDuringABackfillIsNotDuplicated()
    {
        await SelectSource("app");
        await Assertions.Expect(Rows("app").First).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // 「まだ見ていない行」でないと意味がない。既に見た行は重複弾きだけで落ちるので、読み戻しを止めているかどうかが効かない。
        // 台帳の側にも同じ行が居る状況を作る。
        var shown = await Page.EvaluateAsync<int>("""
            async () => {
              const realGetStatus = bridge.getStatus;
              const savedSeen = seam.app.seen;
              try {
                // 台帳に既に居る行を 1 つ選ぶ。**台帳にも push にも同じ行が来る**のが継ぎ目の形。
                const seed = await realGetStatus();
                const replay = (seed.appLogs ?? []).at(-1);

                // **起動直後と同じ道を通す**（`replace: false`）。引き直しは途中で
                // 行を捨てるので、隙に届いた行も一緒に消えてしまい、この守りが効かない。
                // 実際に二重に出たのは起動直後だった。
                el.rowsApp.replaceChildren();
                seam.app.seen = 0;

                const pending = fillFromStore('app', { replace: false });
                stk.receive('appLogs', [replay]);
                await pending;

                // **追いつきが終わった直後に届く分も同じ。** 台帳へ入ったのが読み出しより先で、
                // 押し出しが届いたのが後、という並びは普通に起きる。
                stk.receive('appLogs', [replay]);

                return [...document.querySelectorAll('#rowsApp .row[data-seq]')]
                  .filter(row => row.dataset.seq === String(replay.seq)).length;
              } finally {
                bridge.getStatus = realGetStatus;
                seam.app.seen = savedSeen;
              }
            }
            """);

        Assert.Equal(1, shown);
    }

    /// <summary>
    /// JS は <c>{"limit":100}</c> を送るが、C# の既定の JSON 設定は大文字小文字を区別するので <c>Limit</c> に入らず、既定値のまま動く。
    /// 落ちないので、選んでも件数が変わらないという形でしか出ない。
    /// </summary>
    [Fact]
    public async Task ChoosingAPageSizeFetchesThatMany()
    {
        app.AppendLog([.. Enumerable.Range(0, 150).Select(n => $"[Behaviour] OnPlayerJoined e2e-size-{n:D3}")]);
        await Assertions.Expect(Joined("e2e-size-149")).ToHaveCountAsync(1, new() { Timeout = 60_000 });

        try
        {
            await InPage.PickAsync(Page, "#logSize", "100");

            // 取り直しなので、出ているのは全部「遡って読んだ行」。
            await Assertions.Expect(Page.Locator("#rowsVrc .row[data-paged]"))
                .ToHaveCountAsync(100, new() { Timeout = 20_000 });
        }
        finally
        {
            await InPage.PickAsync(Page, "#logSize", "500");
        }
    }

    [Fact]
    public async Task ScrollingDownAppendsOlderRows()
    {
        app.AppendLog([.. Enumerable.Range(0, 250).Select(n => $"[Behaviour] OnPlayerJoined e2e-page-{n:D3}")]);
        await Assertions.Expect(Joined("e2e-page-249")).ToHaveCountAsync(1, new() { Timeout = 60_000 });

        try
        {
            await InPage.PickAsync(Page, "#logSize", "100");
            await Assertions.Expect(Page.Locator("#rowsVrc .row[data-paged]"))
                .ToHaveCountAsync(100, new() { Timeout = 20_000 });

            // 継ぎ足しはスクロールで走る。
            await Page.EvaluateAsync(
                "() => { el.rowsVrc.scrollTop = el.rowsVrc.scrollHeight;"
                + " el.rowsVrc.dispatchEvent(new Event('scroll')); }");

            await Assertions.Expect(Page.Locator("#rowsVrc .row[data-paged]"))
                .ToHaveCountAsync(200, new() { Timeout = 20_000 });

            // 継ぎ目の検算。通し番号が厳密に減り続けるなら、同じ行が 2 回出ても、間が飛んでも通らない。
            var strictlyDescending = await Page.EvaluateAsync<bool>("""
                () => {
                  const ids = [...document.querySelectorAll('#rowsVrc .row[data-paged][data-id]')]
                    .map(row => Number(row.dataset.id));
                  return ids.every((id, i) => i === 0 || ids[i - 1] > id);
                }
                """);

            Assert.True(strictlyDescending, "遡りの継ぎ目で並びが崩れている");
        }
        finally
        {
            await InPage.PickAsync(Page, "#logSize", "500");
        }
    }

    /// <summary>
    /// SQLite では TEXT は常に INTEGER より大きいので、比較の片方を整数にすると常に真になり、絞ったつもりで全部出る。
    /// 範囲の意味そのものは <c>VrcLogStoreTests</c> で見ているので、ここは配線を見る。
    /// </summary>
    [Fact]
    public async Task SettingATimeRangeShowsOnlyThatPeriod()
    {
        // 記録に 1 件は必ずある状態にしてから引き直す。
        app.AppendLog("[Behaviour] OnPlayerJoined e2e-range-user");
        await Assertions.Expect(Joined("e2e-range-user")).ToHaveCountAsync(1, new() { Timeout = 60_000 });

        await PressReloadAsync();
        await Assertions.Expect(Page.Locator("#rowsVrc .row[data-paged]"))
            .Not.ToHaveCountAsync(0, new() { Timeout = 20_000 });

        try
        {
            await Page.EvaluateAsync(
                "() => { const d = new Date(Date.now() + 86400000);"
                + " el.logFrom.value = d.toISOString().slice(0, 16);"
                + " el.logFrom.dispatchEvent(new Event('change')); }");

            await Assertions.Expect(Page.Locator("#rowsVrc .row[data-paged]"))
                .ToHaveCountAsync(0, new() { Timeout = 20_000 });
        }
        finally
        {
            await Page.Locator("#logRangeClear").ClickAsync();
        }

        await Assertions.Expect(Page.Locator("#rowsVrc .row[data-paged]"))
            .Not.ToHaveCountAsync(0, new() { Timeout = 20_000 });
    }

    /// <summary>
    /// ネイティブのカレンダーは VR の面に乗らない（CEF から別レイヤーで飛んでくる）ので、ページの中に描いたものへ差し替えてある。
    /// 押した日が隠してある元の欄まで届くことを見る。「今」だと分の丸めでその行が入るか入らないかが揺れるので、先月の 1 日を押して確実に過去へ置く。
    /// </summary>
    [Fact]
    public async Task ThePickedDayLandsInTheField()
    {
        // 前の試験が残した値で見ている月が変わるので、空から始める。
        await Page.Locator("#logRangeClear").ClickAsync();

        try
        {
            await InPage.OpenWhenAsync(Page, "#logFrom");
            await InPage.WhenPreviousMonthAsync(Page);
            await InPage.WhenDayAsync(Page, 1);

            var expected = $"{DateTime.Now.AddMonths(-1):yyyy-MM}-01T00:00";
            Assert.Equal(expected, await Page.EvaluateAsync<string>("() => el.logFrom.value"));
        }
        finally
        {
            await Page.Locator("#logRangeClear").ClickAsync();
        }
    }

    /// <summary>入れる道だけあって外す道が無いと、一度絞ったら畳めなくなる（元の欄は隠れているので手では消せない）。</summary>
    [Fact]
    public async Task ClearingInTheCalendarEmptiesTheField()
    {
        await InPage.OpenWhenAsync(Page, "#logFrom");
        await InPage.WhenNowAsync(Page);

        Assert.NotEqual("", await Page.EvaluateAsync<string>("() => el.logFrom.value"));

        await InPage.WhenClearAsync(Page);

        Assert.Equal("", await Page.EvaluateAsync<string>("() => el.logFrom.value"));
    }

    /// <summary>
    /// 一覧も暦も <c>body</c> に <c>fixed</c> で置いてあるので、面が動いたら付いていくか閉じるかしかない。
    /// 「動いたら閉じる」を素直に書くと、ログの行が届くたびに閉じる（面が自分で追従して動くため）。
    /// </summary>
    [Theory]
    [InlineData("pick")]
    [InlineData("when")]
    public async Task WhatIsOpenSurvivesTheRowsScrolling(string which)
    {
        var open = Page.Locator(which == "pick" ? ".spoitable-pick-list:not([hidden])" : ".spoitable-when-pop:not([hidden])");

        try
        {
            if (which == "pick") await Page.Locator(".spoitable-pick-box:has(#logSize) .spoitable-pick-value").ClickAsync();
            else await InPage.OpenWhenAsync(Page, "#logFrom");

            await Assertions.Expect(open).ToBeVisibleAsync();

            // 行が届いたときと同じ動き。面が自分で巻くので、利用者は何も触っていない。
            await Page.EvaluateAsync("() => el.rowsVrc.dispatchEvent(new Event('scroll'))");

            await Assertions.Expect(open).ToBeVisibleAsync();
        }
        finally
        {
            await Page.Keyboard.PressAsync("Escape");
        }
    }

    /// <summary>分は 60 通りあって箱に収まらないので、送らないといつも 00 分から探すことになる。VR ではレーザーで巻くので、そのぶんがまるごと手間になる。</summary>
    [Fact]
    public async Task TheMinuteListOpensAtTheChosenMinute()
    {
        await Page.Locator("#logRangeClear").ClickAsync();

        try
        {
            // 一覧の終わりの方を選んでおく。先頭に近いと、送らなくても見えてしまう。
            await Page.EvaluateAsync(
                "() => { el.logFrom.value = '2026-09-06T10:55';"
                + " el.logFrom.dispatchEvent(new Event('change')); }");

            await InPage.OpenWhenAsync(Page, "#logFrom");
            await Page.Locator(".spoitable-when-pop:not([hidden]) .spoitable-when-time .spoitable-pick-box").Last
                .Locator(".spoitable-pick-value").ClickAsync();

            var inView = await Page.EvaluateAsync<bool>(
                """
                () => {
                  const list = document.querySelector('.spoitable-pick-list:not([hidden])');
                  const chosen = list.querySelector('[aria-selected="true"]');
                  const outer = list.getBoundingClientRect();
                  const inner = chosen.getBoundingClientRect();

                  return inner.top >= outer.top - 1 && inner.bottom <= outer.bottom + 1;
                }
                """);

            Assert.True(inView, "選んである分が一覧の外に居る");
        }
        finally
        {
            await Page.Locator("#logRangeClear").ClickAsync();
        }
    }

    /// <summary>
    /// 時・分の一覧は暦の中に居ない。どちらも <c>body</c> へ出してある（囲いに切り取られないため）ので、
    /// 「外を押したら閉じる」を素直に書くと、時刻を選んだ瞬間に暦が畳まれ、押し直さないと分が選べない。
    /// </summary>
    [Fact]
    public async Task ChoosingAnHourKeepsTheCalendarOpen()
    {
        await Page.Locator("#logRangeClear").ClickAsync();

        try
        {
            await InPage.OpenWhenAsync(Page, "#logFrom");

            // 日が無いと時刻だけでは値にならないので、先に入れておく。
            await InPage.WhenNowAsync(Page);
            await InPage.WhenHourAsync(Page, 5);

            // 形は yyyy-MM-ddTHH:mm。分は「今」のままなので、時のところだけ見る。
            var value = await Page.EvaluateAsync<string>("() => el.logFrom.value");
            Assert.Equal("05", value.Substring(11, 2));

            await Assertions.Expect(Page.Locator(".spoitable-when-pop:not([hidden])")).ToBeVisibleAsync();
        }
        finally
        {
            await Page.Locator("#logRangeClear").ClickAsync();
        }
    }

    /// <summary><c>title</c> の吹き出しも VR の面に乗らないので、<c>title</c> は奪ってこちらで描く。奪えているところまで見る（残っていると窓側で二重に出る）。</summary>
    [Fact]
    public async Task TheTooltipIsDrawnInsideThePage()
    {
        await Page.Locator(".spoitable-pick-box:has(#logSize) .spoitable-pick-value").HoverAsync();

        await Assertions.Expect(Page.Locator(".spoitable-tip")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(Page.Locator(".spoitable-tip")).ToHaveTextAsync(
            await Page.EvaluateAsync<string>("() => el.logSize.dataset.tip"));

        Assert.Equal(0, await Page.Locator("#logSize[title]").CountAsync());
    }

    /// <summary>保存していない生行はそのセッション限りなので遡っても出てこないし、畳んでいる種別を引いても枠を食うだけ。</summary>
    [Fact]
    public async Task RawLinesArePagedBackOnlyWhenStoringIsOnAndTheChipIsShown()
    {
        var asked = await Page.EvaluateAsync<bool[]>("""
            () => {
              const savedStore = storeAllLines;
              const rawWasHidden = hidden.has('raw');
              try {
                const ask = (store, showRaw) => {
                  storeAllLines = store;
                  showRaw ? hidden.delete('raw') : hidden.add('raw');
                  return JSON.parse(pageRequest({})).includeLines;
                };
                return [ask(false, false), ask(false, true), ask(true, false), ask(true, true)];
              } finally {
                storeAllLines = savedStore;
                rawWasHidden ? hidden.add('raw') : hidden.delete('raw');
              }
            }
            """);

        Assert.Equal([false, false, false, true], asked);
    }

    /// <summary>畳む種別は DB 側でも除いている（件数を「見える行」で数えるため）ので、CSS を直すだけでは戻した種別の過去が出てこない。押したら取り直す。</summary>
    [Fact]
    public async Task TurningAKindBackOnBringsBackThatKindsHistory()
    {
        app.AppendLog("[Behaviour] OnPlayerJoined e2e-chip-user");
        await Assertions.Expect(Joined("e2e-chip-user")).ToHaveCountAsync(1, new() { Timeout = 60_000 });

        try
        {
            // 畳んでいる間に画面から落ちる（打ち切りは畳んである行から捨てる）。記録には残っている。
            await ToggleKindAsync("PlayerJoined");
            await Page.EvaluateAsync("() => document.querySelectorAll(\"#rowsVrc .row[data-kind='PlayerJoined']\").forEach(row => row.remove())");
            Assert.Equal(0, await Joined("e2e-chip-user").CountAsync());

            // 戻す。取り直しが走らないと、消したままになる。
            await ToggleKindAsync("PlayerJoined");
            await Assertions.Expect(Joined("e2e-chip-user")).ToHaveCountAsync(1, new() { Timeout = 20_000 });
        }
        finally
        {
            // 畳んだ種別はホストの設定に残る。畳んだままだと後続の試験が別の既定で走る。
            if (await KindHiddenAsync("PlayerJoined")) await ToggleKindAsync("PlayerJoined");
        }
    }

    /// <summary>
    /// <c>log_events</c> と <c>log_lines</c> はそれぞれ 1 から採番するので、番号は必ずぶつかる。
    /// 1 つの印にまとめると、片方が追い越した瞬間に追い越された側が出なくなる。
    /// </summary>
    [Fact]
    public async Task RawLinesAndEventsAreNumberedSeparately()
    {
        var shown = await Page.EvaluateAsync<int>("""
            () => {
              const savedEvent = seam.vrc.seen;
              const savedLine = seam.vrc.seenLine;
              try {
                seam.vrc.seen = 0;
                seam.vrc.seenLine = 0;

                const ts = new Date().toISOString();

                // **同じ番号**のイベントと生行。別々に数えていれば、どちらも出る。
                stk.receive('logEvents', [{ id: 5, type: 'event', kind: 'PlayerJoined',
                                            ts, title: 'e2e-space-user', detail: '' }]);
                stk.receive('logLines', [{ id: 5, type: 'line', kind: 'raw',
                                           ts, level: 'Log', tag: null, message: 'e2e-space-line' }]);

                return [...document.querySelectorAll('#rowsVrc .row')]
                  .filter(row => /e2e-space-(user|line)/.test(row.textContent)).length;
              } finally {
                seam.vrc.seen = savedEvent;
                seam.vrc.seenLine = savedLine;
                [...document.querySelectorAll('#rowsVrc .row')]
                  .filter(row => /e2e-space-(user|line)/.test(row.textContent)).forEach(row => row.remove());
              }
            }
            """);

        Assert.Equal(2, shown);
    }

    /// <summary>表示名は変わるが id は変わらないので、後から辿れるのは id の方。一覧は表示名のままで、開いたときだけ出す（usr_ は 40 文字あって、常に出すと表示名が押される）。</summary>
    [Fact]
    public async Task OpeningAJoinRowShowsTheUserId()
    {
        const string user = "usr_0e2e0e2e-1111-2222-3333-444444444444";

        app.AppendLog($"[Behaviour] OnPlayerJoined e2e-userid-user ({user})");

        var row = Joined("e2e-userid-user");
        await Assertions.Expect(row).ToHaveCountAsync(1, new() { Timeout = 20_000 });

        Assert.DoesNotContain(user, await row.TextContentAsync() ?? "");

        await row.Locator(".line").ClickAsync();

        await Assertions.Expect(Sheet).ToBeVisibleAsync();
        await Assertions.Expect(Sheet.Locator("#sheetBody")).ToContainTextAsync(user);
    }

    /// <summary>
    /// アプリのログの行が、どのモジュールのものか（出どころの札・隠す設定が見る <c>data-src</c>）。カテゴリは <c>SpoitableToolKit.Modules.&lt;Id&gt;</c>（本体が作る <c>IModuleHost.Logger</c>）で、
    /// Id は点を含みうるので、頭のあとを切らない。入口の型の完全名の行（既存の記録に残る旧形式。<c>SpoitableToolKit.Modules.Hrs.HrsModule</c>）は、最初の段を Id として読む。それ以外は本体の行。
    /// </summary>
    [Fact]
    public async Task ALogLineBelongsToTheModuleTheCategoryNames()
    {
        var sources = await Page.EvaluateAsync<string[]>("""
            () => [
              'SpoitableToolKit.Modules.hrs',
              'SpoitableToolKit.Modules.my.module',
              'SpoitableToolKit.Modules.Hrs.HrsModule',
              'SpoitableToolKit.Host.Modules.ModuleManager',
              null,
            ].map(sourceOf)
            """);

        Assert.Equal(["hrs", "my.module", "hrs", "host", "host"], sources);
    }

    private ILocator Joined(string name) =>
        Page.Locator("#rowsVrc .row[data-kind='PlayerJoined']", new() { HasTextString = name });

    /// <summary>
    /// VRChat の面を画面の上だけ空にして、上に居る（流れてきた行を枠で切る）状態にする。記録と台帳には触らない。
    /// 打ち切りを見る試験は、見たい行が面でいちばん古い所から始める。前の試験の行が残っていると、そちらが先に切られて、切る向きを間違えても見たい行が無事なまま通ってしまう。
    /// 走りかけの作り直しの返事は捨てさせる（代を進める）。前の試験が頼んだ取り直しが後から着いて、空にした面を埋め直さないように。
    /// </summary>
    private Task EmptyTheVrcLaneOnScreenAsync() =>
        Page.EvaluateAsync("() => { generation.vrc++; el.rowsVrc.replaceChildren(); stuck.vrc = true; }");
}
