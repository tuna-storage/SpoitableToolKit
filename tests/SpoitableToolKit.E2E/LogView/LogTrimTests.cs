using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.LogView;

/// <summary>
/// 行はページの関数で直に作る（決まった番号で、穴が空いたかを数で見られるように）。アプリは自分の行を出し続けるので、
/// 作ってから数えるまでは 1 回の evaluate の中で、await を挟まずにやる（JS は 1 本なので、その間に push は割り込めない）。
/// 返事の順は <c>bridge</c> の口を「あとで返す promise」に差し替えて決める。
/// </summary>
public sealed class LogTrimTests(AppFixture app) : IClassFixture<AppFixture>
{
    private IPage Page => app.Page;

    /// <summary>行の材料（アプリの面と VRChat の面）。evaluate の頭に置く。</summary>
    private const string Makers = """
        const appRow = seq => ({
          seq, run: 1, ts: new Date(1700000000000 + seq * 1000).toISOString(), level: 'Information', shortLevel: 'INF',
          category: 'E2E', fullCategory: 'SpoitableToolKit.Host.E2E', thread: 1, message: 'e2e-trim-' + seq, exception: null,
        });
        const appRange = (from, to) => Array.from({ length: from - to + 1 }, (_, i) => appRow(from - i));
        const ev = id => ({ type: 'event', id, kind: 'PlayerJoined', ts: new Date(1700000000000 + id * 1000).toISOString(), title: 'e2e-trim-' + id, detail: '' });
        const line = n => ({ type: 'line', id: null, ts: new Date(1600000000000 + n * 1000).toISOString(), level: 'INF', tag: 'E2E', message: 'e2e-trim-raw-' + n });
        const appSeqs = () => [...el.rowsApp.querySelectorAll(':scope > .row')].map(row => Number(row.dataset.seq));
        const contiguous = list => list.every((seq, i) => i === 0 || list[i - 1] - 1 === seq);
        """;

    private async Task SelectSource(string name)
    {
        await Page.Locator($"#logTabs .spoitable-tab[data-source='{name}']").ClickAsync();
        await Assertions.Expect(Page.Locator($"#logTabs .spoitable-tab[data-source='{name}']")).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(@"\bspoitable-tab-on\b"));
    }

    private Task RebuildAsync(string name) =>
        Page.EvaluateAsync($"async () => {{ stuck.{name} = true; await fillFromStore('{name}', {{ replace: true }}); }}");

    /// <summary>どちらのときも番号は 1 つずつ並ぶ（穴が無い）。切ったら「これより古い記録はありません」も戻す（切った分をまた読めるように）。</summary>
    [Fact]
    public async Task ReadingKeepsEveryRowAndBackAtTheTopTheOldestGoFirst()
    {
        await SelectSource("app");

        try
        {
            var shape = await Page.EvaluateAsync<long[]>($$"""
                () => {
                  {{Makers}}
                  el.rowsApp.replaceChildren();
                  stuck.app = true;
                  addRows('app', appRange(10500, 10001), renderAppLog, { older: true });
                  stuck.app = false;                                                             // 下へ巻いて読んでいる
                  addRows('app', appRange(10000, 9901), renderAppLog, { older: true, paged: true }); // 遡った 100 行
                  for (let seq = 10501; seq <= 10550; seq++) addRows('app', [appRow(seq)], renderAppLog); // 流れてきた 50 行
                  const reading = appSeqs();

                  appPage.exhausted = true;                                                      // 下の端まで読み切っていた
                  stuck.app = true;                                                              // 上に戻った
                  addRows('app', [appRow(10551)], renderAppLog);
                  const back = appSeqs();

                  return [reading.length, contiguous(reading) ? 1 : 0, reading[0], reading.at(-1),
                          back.length, contiguous(back) ? 1 : 0, back[0], back.at(-1), appPage.exhausted ? 1 : 0];
                }
                """);

            Assert.Equal([650, 1, 10550, 9901, 500, 1, 10551, 10052, 0], shape);
        }
        finally
        {
            await RebuildAsync("app");
        }
    }

    /// <summary>
    /// 続きの位置（<c>page.oldestId</c> / <c>oldestTs</c>）は一番下の出来事の行に合わせ直す。古いままだと、次の遡りで切った分が飛ぶ。
    /// 出来事の行より下に残った生行も外す（続きの生行の窓に入って二重に並ぶ）。
    /// </summary>
    [Fact]
    public async Task CuttingTheVrChatLaneMovesWhereTheNextPageStarts()
    {
        await SelectSource("vrc");

        try
        {
            var shape = await Page.EvaluateAsync<string[]>($$"""
                () => {
                  {{Makers}}
                  el.rowsVrc.replaceChildren();
                  stuck.vrc = true;
                  page.oldestId = 1;
                  page.oldestTs = ev(1).ts;
                  page.exhausted = true;

                  // 出来事 499 行の下に生行 2 行（501 行）。1 行あふれるので、まず畳んである生行が 1 行落ちる。
                  const events = Array.from({ length: 499 }, (_, i) => ev(1000 - i));
                  addRows('vrc', [...events, line(2), line(1)], renderRow, { older: true });

                  const rows = [...el.rowsVrc.querySelectorAll(':scope > .row')];
                  return [String(rows.length), rows.at(-1).dataset.id ?? 'raw', String(page.oldestId),
                          String(page.oldestTs === ev(502).ts), String(page.exhausted),
                          String(rows.filter(row => row.dataset.kind === 'raw').length)];
                }
                """);

            Assert.Equal(["499", "502", "502", "true", "false", "0"], shape);
        }
        finally
        {
            await RebuildAsync("vrc");
        }
    }

    /// <summary>作り直す前に頼んだ遡りの返事は捨てる（作り直した面の下に古い行を足さない）。足元も遡りの返事では決めない（作り直しが戻したまま）。</summary>
    [Fact]
    public async Task ARebuildOfTheAppLaneWinsOverAnOlderPageThatArrivesLate()
    {
        await SelectSource("app");

        try
        {
            var shape = await Page.EvaluateAsync<string[]>($$"""
                async () => {
                  {{Makers}}
                  const real = bridge.queryAppLogs;
                  let release;
                  bridge.queryAppLogs = () => new Promise(resolve => { release = resolve; });
                  try {
                    addRows('app', [appRow(20000)], renderAppLog, { older: true });
                    appPage.exhausted = false;
                    appPage.loading = false;

                    const going = loadOlderApp();
                    await fillFromStore('app', { replace: true });
                    release({ rows: [{ ...appRow(19999), message: 'e2e-stale' }], hasMore: false });
                    await going;

                    const stale = [...el.rowsApp.querySelectorAll('.row')].filter(row => row.textContent.includes('e2e-stale')).length;
                    return [String(stale), String(appPage.loading), String(appPage.exhausted), el.logMoreText.textContent];
                  } finally {
                    bridge.queryAppLogs = real;
                  }
                }
                """);

            Assert.Equal(["0", "false", "false", ""], shape);
        }
        finally
        {
            await RebuildAsync("app");
        }
    }

    /// <summary>遅れた返事が続きの位置を古い方で上書きすると、次の遡りで行が飛ぶ。</summary>
    [Fact]
    public async Task ARebuildWinsOverAnOlderPageThatArrivesLate()
    {
        await SelectSource("vrc");

        try
        {
            var shape = await Page.EvaluateAsync<string[]>($$"""
                async () => {
                  {{Makers}}
                  const real = bridge.queryLog;
                  const calls = [];
                  bridge.queryLog = () => new Promise(resolve => calls.push(resolve));
                  try {
                    page.oldestId = 900;
                    page.oldestTs = ev(900).ts;
                    page.exhausted = false;
                    page.loading = false;

                    const older = loadOlder();
                    const rebuilt = fillFromStore('vrc', { replace: true });
                    calls[1]({ rows: [ev(2000)], oldestId: 2000, oldestTs: ev(2000).ts, hasMore: true });
                    await rebuilt;
                    calls[0]({ rows: [ev(1)], oldestId: 1, oldestTs: ev(1).ts, hasMore: false });
                    await older;

                    const ids = [...el.rowsVrc.querySelectorAll(':scope > .row')].map(row => row.dataset.id);
                    return [String(ids.includes('2000')), String(ids.includes('1')), String(page.oldestId), String(page.exhausted)];
                  } finally {
                    bridge.queryLog = real;
                  }
                }
                """);

            Assert.Equal(["true", "false", "2000", "false"], shape);
        }
        finally
        {
            await RebuildAsync("vrc");
        }
    }

    /// <summary>先に終わった方が溜めるのをやめると、その間に届いた行は直に描かれ、遅れた返事の作り直しに消される。</summary>
    [Fact]
    public async Task OverlappingRebuildsKeepTheRowThatArrivedBetween()
    {
        await SelectSource("vrc");

        try
        {
            var shape = await Page.EvaluateAsync<string[]>($$"""
                async () => {
                  {{Makers}}
                  const real = bridge.queryLog;
                  const calls = [];
                  bridge.queryLog = () => new Promise(resolve => calls.push(resolve));
                  try {
                    const first = fillFromStore('vrc', { replace: true });
                    const second = fillFromStore('vrc', { replace: true });
                    calls[1]({ rows: [ev(3000)], oldestId: 3000, oldestTs: ev(3000).ts, hasMore: false });
                    await new Promise(resolve => setTimeout(resolve, 0));
                    stk.receive('logEvents', [ev(3001)]);
                    calls[0]({ rows: [ev(5)], oldestId: 5, oldestTs: ev(5).ts, hasMore: false });
                    await Promise.all([first, second]);

                    const ids = [...el.rowsVrc.querySelectorAll(':scope > .row')].map(row => row.dataset.id);

                    // 逆の順: 先に頼んだ（古い代の）方が先に着いて捨てられ、その間も**後の作り直しが終わるまで溜める**。
                    // 溜めるのをやめると、届いた行は直に描かれ、後の作り直しの replace で消える。
                    const third = fillFromStore('vrc', { replace: true });
                    const fourth = fillFromStore('vrc', { replace: true });
                    calls[2]({ rows: [ev(6)], oldestId: 6, oldestTs: ev(6).ts, hasMore: false });
                    await new Promise(resolve => setTimeout(resolve, 0));
                    stk.receive('logEvents', [ev(4001)]);
                    calls[3]({ rows: [ev(4000)], oldestId: 4000, oldestTs: ev(4000).ts, hasMore: false });
                    await Promise.all([third, fourth]);

                    const later = [...el.rowsVrc.querySelectorAll(':scope > .row')].map(row => row.dataset.id);
                    return [String(ids.includes('3000')), String(ids.includes('3001')), String(ids.includes('5')), String(seam.vrc.catching),
                            String(later.includes('4000')), String(later.includes('4001')), String(later.includes('6'))];
                  } finally {
                    bridge.queryLog = real;
                  }
                }
                """);

            Assert.Equal(["true", "true", "false", "false", "true", "true", "false"], shape);
        }
        finally
        {
            await RebuildAsync("vrc");
        }
    }

    /// <summary>
    /// 読む位置を戻す <c>scrollTop +=</c> が滑りを止めると、選んだ行が見えない所で止まる。
    /// 滑らかかどうかに左右されないよう、OS の「動きを減らす」は no-preference に固定する。
    /// </summary>
    [Fact]
    public async Task ARowArrivingWhileTheSheetStepsStillLandsOnTheChosenRow()
    {
        await SelectSource("app");
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });

        try
        {
            await Page.EvaluateAsync($$"""
                () => {
                  {{Makers}}
                  el.rowsApp.replaceChildren();
                  stuck.app = true;
                  addRows('app', appRange(30500, 30001), renderAppLog, { older: true });
                  el.rowsApp.style.maxHeight = '160px';

                  // 50 番目の行が窓の下の端にちょうど収まる所まで巻く（次の行は窓の外）。
                  const rows = [...el.rowsApp.querySelectorAll(':scope > .row')];
                  const box = el.rowsApp.getBoundingClientRect();
                  el.rowsApp.scrollTop += rows[50].getBoundingClientRect().bottom - box.bottom;
                  el.rowsApp.dispatchEvent(new Event('scroll'));

                  openSheet(rows[50]);
                  stepSheet(1);
                  addRows('app', [appRow(30501), appRow(30502)], renderAppLog);   // 滑り始めた途端に届いた
                  window.__e2eTarget = rows[51];
                }
                """);

            await Page.WaitForFunctionAsync(
                """
                () => {
                  const row = window.__e2eTarget.getBoundingClientRect();
                  const box = el.rowsApp.getBoundingClientRect();
                  return row.top >= box.top - 1 && row.bottom <= box.bottom + 1;
                }
                """, null, new() { Timeout = 5_000 });

            // 滑り終わったら（scrollend）向け直しはやめる。やめないと、離れた所を読んでいるときに届いた行で、選んだ行へ引き戻される。
            Assert.True(await Page.EvaluateAsync<bool>(
                "() => { el.rowsApp.dispatchEvent(new Event('scrollend')); return sheetGlide.app === null; }"));
        }
        finally
        {
            await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Null });
            await Page.EvaluateAsync("() => { closeSheet(); el.rowsApp.style.maxHeight = ''; delete window.__e2eTarget; }");
            await RebuildAsync("app");
        }
    }
}
