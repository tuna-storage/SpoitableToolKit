using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Hrs;

/// <summary>
/// 記録は過去の決まった日に置く。<see cref="AppFixture.SeedHeartRates"/> は「いま」から遡って積むので、
/// ほかの試験と同じ日に混ざり、最大や件数が実行順で変わる。
/// 置く記録: 10:00:00 → 60、10:00:01 → 0（計れていない）、10:00:02 → 80、10:00:03 → 100、10:10:00 → 92。
/// 0 を数えなければ 最小 60・平均 83・最大 100。
/// </summary>
public sealed class HrsStatsTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private IPage Page => app.Page;

    private IFrameLocator Panel => Page.FrameLocator("iframe.view[data-id='hrs']");

    /// <summary>
    /// パネルの中で JS を走らせる口。Locator の evaluate は 1 つ目の引数に要素が来るので、
    /// 値を渡すときは <c>(_, value) =&gt; …</c> と書く。
    /// </summary>
    private ILocator Inside => Panel.Locator("body");

    private ILocator Toasts => Page.Locator("#toasts .toast");

    private static readonly DateTime Day = new(2026, 9, 1);

    private static bool _seeded;

    private static DateTimeOffset At(int hour, int minute, int second) =>
        new(DateTime.SpecifyKind(Day.Add(new TimeSpan(hour, minute, second)), DateTimeKind.Local));

    private static readonly Regex ZoomedRange = new(@"^\d\d:\d\d(:\d\d)? – \d\d:\d\d(:\d\d)?$");

    public async Task InitializeAsync()
    {
        // 同じアプリ・同じ記録を全部の試験で使うので、1 回だけ置く（2 回置くと書き出しの件数が倍になる）。
        if (!_seeded)
        {
            app.SeedHeartRatesAt(
                (At(10, 0, 0), 60), (At(10, 0, 1), 0), (At(10, 0, 2), 80), (At(10, 0, 3), 100), (At(10, 10, 0), 92));
            _seeded = true;
        }

        if (await Page.Locator("#modal").IsVisibleAsync()) await Page.Locator("#modalNo").ClickAsync();

        await Page.Locator("#sidebar .item[data-id='hrs']").ClickAsync();
        await Assertions.Expect(Panel.Locator("#tabStats")).Not.ToHaveTextAsync("", new() { Timeout = 20_000 });
        await Panel.Locator("#tabStats").ClickAsync();
    }

    /// <summary>ほかの試験は「いま」のタイルや一覧を見るので、「いま」に戻しておく。</summary>
    public async Task DisposeAsync() => await Panel.Locator("#tabNow").ClickAsync();

    private async Task<IFrame> FrameAsync() =>
        await (await Page.Locator("iframe.view[data-id='hrs']").ElementHandleAsync()).ContentFrameAsync()
        ?? throw new InvalidOperationException("心拍のパネルに frame が無い");

    private async Task ShowDayAsync(DateTime day)
    {
        await Inside.EvaluateAsync("(_, d) => setDay(new Date(d[0], d[1] - 1, d[2]))", new[] { day.Year, day.Month, day.Day });
        await WaitForDayAsync(day);
    }

    private async Task WaitForDayAsync(DateTime day)
    {
        var from = new DateTimeOffset(DateTime.SpecifyKind(day, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        try
        {
            await (await FrameAsync()).WaitForFunctionAsync(
                "from => __stats.state.day[0] === from", from, new FrameWaitForFunctionOptions { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            // 読み終わらなかった: パネルの状態と、記録を直に読んだ結果を出す。
            var seen = await Inside.EvaluateAsync<string>(
                """
                async (_, from) => JSON.stringify({
                  state: __stats.state, day: el.day.value,
                  read: await spoitable.call('readRecords', { from, to: from + 86400000 - 1 }).catch(e => String(e)),
                })
                """, from);
            Assert.Fail($"{day:yyyy-MM-dd} を読み終えなかった: {seen}");
        }
    }

    [Fact]
    public async Task TheChosenDayIsSummedUpWithoutTheUnmeasuredBeats()
    {
        await ShowDayAsync(Day);

        await Assertions.Expect(Panel.Locator("#dayMin")).ToHaveTextAsync("60");
        await Assertions.Expect(Panel.Locator("#dayAvg")).ToHaveTextAsync("83");
        await Assertions.Expect(Panel.Locator("#dayMax")).ToHaveTextAsync("100");
        await Assertions.Expect(Panel.Locator("#dayRange")).ToHaveTextAsync("1 日全体");

        await Panel.Locator("#dayNext").ClickAsync();
        await WaitForDayAsync(Day.AddDays(1));

        await Assertions.Expect(Panel.Locator("#dayMin")).ToHaveTextAsync("—");
        await Assertions.Expect(Panel.Locator("#dayRange")).ToHaveTextAsync("データなし");

        await Panel.Locator("#dayPrev").ClickAsync();
        await WaitForDayAsync(Day);

        await Assertions.Expect(Panel.Locator("#dayMax")).ToHaveTextAsync("100");
    }

    /// <summary>1 回の evaluate で数え方と拡大の決まりをまとめて見る（変異試験は 1 通りが遅いので、1 本で多くの壊れ方を捕まえる）。</summary>
    [Fact]
    public async Task TheStatsAndZoomRulesHold()
    {
        var result = await Inside.EvaluateAsync<string>(
            """
            () => JSON.stringify({
              summary: __stats.summarize([[1, 0], [2, 60], [3, 80], [4, 100], [9, 50]], 1, 4),
              none: __stats.summarize([[1, 0]], 0, 10),
              widened: __stats.clampRange(5000, 5000, [0, 100000]),
              shifted: __stats.clampRange(98000, 99000, [0, 100000]),
              zoomIn: __stats.zoomAround([0, 100000], [0, 100000], 25000, 0.5),
              deepest: __stats.zoomAround([40000, 60000], [0, 100000], 50000, 0.01),
              out: __stats.zoomAround([40000, 60000], [0, 100000], 50000, 1000),
              day: (() => {
                const [from, to] = __stats.dayBounds('2026-09-01');
                return [new Date(from).getDate(), new Date(from).getHours(), new Date(to + 1).getDate(), new Date(to + 1).getHours()];
              })(),
            })
            """);

        Assert.Equal(
            """{"summary":{"min":60,"max":100,"avg":80,"count":3},"none":null,"widened":[0,10000],"shifted":[90000,100000],"zoomIn":[12500,62500],"deepest":[45000,55000],"out":[0,100000],"day":[1,0,2,0]}""",
            result);
    }

    /// <summary>グラフ上の右クリックのイベントは、`_host/bridge.js` が document で拾ってメニューを頼むので、そこまで上げない。</summary>
    [Fact]
    public async Task TheWheelZoomsAndARightClickGoesBackWithoutTheMenu()
    {
        await ShowDayAsync(Day);

        // document まで上がってきた分を数える。グラフで止めていれば 0 のまま。
        await Inside.EvaluateAsync(
            """
            () => {
              window.__wheelsLeft = [];
              window.__menusAsked = 0;
              document.addEventListener('wheel', event => __wheelsLeft.push(event.defaultPrevented));
              document.addEventListener('contextmenu', () => __menusAsked++);
            }
            """);

        var graph = Panel.Locator("#graph");
        await graph.HoverAsync();
        await Page.Mouse.WheelAsync(0, -600);

        await Assertions.Expect(Panel.Locator("#dayRange")).ToHaveTextAsync(ZoomedRange);
        await Assertions.Expect(Panel.Locator("#zoomReset")).ToBeVisibleAsync();
        var wheels = await Inside.EvaluateAsync<bool[]>("() => __wheelsLeft");
        Assert.NotEmpty(wheels);
        Assert.All(wheels, Assert.True);

        await graph.ClickAsync(new() { Button = MouseButton.Right });

        await Assertions.Expect(Panel.Locator("#dayRange")).ToHaveTextAsync("1 日全体");
        await Assertions.Expect(Panel.Locator("#zoomReset")).ToBeHiddenAsync();
        Assert.Equal(0, await Inside.EvaluateAsync<int>("() => __menusAsked"));
    }

    [Fact]
    public async Task ADragZoomsInAndADoubleClickStepsBack()
    {
        await ShowDayAsync(Day);

        var graph = Panel.Locator("#graph");
        var box = await graph.BoundingBoxAsync() ?? throw new InvalidOperationException("グラフが見えていない");
        var y = box.Y + box.Height / 2;

        await Page.Mouse.MoveAsync(box.X + box.Width * 0.4f, y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(box.X + box.Width * 0.6f, y, new() { Steps = 5 });
        await Page.Mouse.UpAsync();

        await Assertions.Expect(Panel.Locator("#dayRange")).ToHaveTextAsync(ZoomedRange);

        await graph.DblClickAsync();

        await Assertions.Expect(Panel.Locator("#dayRange")).ToHaveTextAsync("1 日全体");
    }

    /// <summary>寄っていればその範囲だけが出る（10:00:00〜10:00:03 の 4 件。日全体なら 5 件、「全部」ならほかの試験の分も入る）。</summary>
    [Fact]
    public async Task TheRangeOnTheGraphIsWhatGetsExported()
    {
        // 書き出しのボタンは件数が 0 だと押せない。アプリの外から書いた分は、開き直さないと数えない。
        await Page.EvaluateAsync(
            """
            () => {
              const frame = document.querySelector("iframe.view[data-id='hrs']");
              if (frame) frame.src = frame.src;
            }
            """);
        await Assertions.Expect(Panel.Locator("#tabStats")).Not.ToHaveTextAsync("", new() { Timeout = 20_000 });
        await Panel.Locator("#tabStats").ClickAsync();
        await ShowDayAsync(Day);

        await Inside.EvaluateAsync(
            "(_, r) => setView(__stats.clampRange(r[0], r[1], __stats.state.day), { push: true })",
            new[] { At(10, 0, 0).ToUnixTimeMilliseconds(), At(10, 0, 3).ToUnixTimeMilliseconds() });
        await Assertions.Expect(Panel.Locator("#dayRange")).ToHaveTextAsync(ZoomedRange);

        await Inside.EvaluateAsync("() => { el.range.value = 'view'; el.range.dispatchEvent(new Event('change')); }");

        var export = Panel.Locator("#export");
        await Assertions.Expect(export).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await export.ClickAsync();

        var toast = Toasts.First;
        await Assertions.Expect(toast.Locator(".title")).ToContainTextAsync("心拍 4 件", new() { Timeout = 15_000 });

        // 閉じておく（トーストの試験は 0 件から数える）。
        await toast.GetByRole(AriaRole.Button, new() { Name = "パスをコピー" }).ClickAsync();
        await Assertions.Expect(Toasts).ToHaveCountAsync(0, new() { Timeout = 10_000 });
    }

    /// <summary>
    /// グラフの範囲は読み終えるまで前の日のままなので、読み込みの返事を止めてその間に押す。
    /// 書き出しは C# へ渡さずに、頼んだ範囲だけを見る。
    /// </summary>
    [Fact]
    public async Task ExportingWhileTheNextDayLoadsGivesTheNextDay()
    {
        await ShowDayAsync(Day);

        var range = await Inside.EvaluateAsync<long[]>(
            """
            (_, d) => {
              const real = spoitable.call;
              let asked = null;
              spoitable.call = (method, payload) => {
                if (method === 'readRecords') return new Promise(() => {});
                if (method === 'exportCsv') { asked = payload; return Promise.resolve(null); }
                return real(method, payload);
              };
              try {
                el.range.value = 'view';
                setDay(new Date(d[0], d[1] - 1, d[2]));
                el.export.disabled = false;
                el.export.click();
              } finally {
                spoitable.call = real;
              }
              const want = __stats.dayBounds(el.day.value);
              return [asked?.from ?? -1, asked?.to ?? -1, want[0], want[1]];
            }
            """,
            new[] { Day.Year, Day.Month, Day.Day + 1 });

        Assert.Equal(range[2], range[0]);
        Assert.Equal(range[3], range[1]);

        // 次の試験のために、選んだ日を読み直させる（止めた読み込みは返ってこない）。
        await ShowDayAsync(Day);
    }

    [Fact]
    public async Task TheDayFieldIsADateOnlyCalendar()
    {
        await ShowDayAsync(Day);

        var value = Panel.Locator(".daynav .spoitable-when-value");
        await Assertions.Expect(value).ToHaveTextAsync("2026-09-01");
        await value.ClickAsync();

        var pop = Panel.Locator(".spoitable-when-pop");
        await Assertions.Expect(pop).ToBeVisibleAsync();
        await Assertions.Expect(pop.Locator(".spoitable-when-time")).ToBeHiddenAsync();
        await Assertions.Expect(pop.Locator(".spoitable-when-foot button").First).ToHaveTextAsync("今日");
        await Assertions.Expect(pop.Locator(".spoitable-when-foot button").Nth(1)).ToBeHiddenAsync();

        await pop.Locator(".spoitable-when-grid button:not(.spoitable-when-other)", new() { HasTextRegex = new Regex("^2$") }).ClickAsync();
        await WaitForDayAsync(Day.AddDays(1));

        await Assertions.Expect(value).ToHaveTextAsync("2026-09-02");
        await Assertions.Expect(Panel.Locator("#dayRange")).ToHaveTextAsync("データなし");

        await Page.Keyboard.PressAsync("Escape");
    }

    /// <summary>bridge.js が theme.css を引き直すのは push の後なので、読み終わるのを待ってから描く（すぐ読むと前の色が返る）。画素は読まず、最後に描いた色を見る。</summary>
    [Fact]
    public async Task TheGraphIsRedrawnInTheNewTheme()
    {
        await ShowDayAsync(Day);

        var frame = await FrameAsync();
        var before = await Inside.EvaluateAsync<string>("() => __stats.state.colors.grid");
        Assert.False(string.IsNullOrEmpty(before));

        const string follows =
            """
            before => {
              const now = getComputedStyle(document.body).getPropertyValue('--border').trim();
              return now !== before && __stats.state.colors?.grid === now;
            }
            """;

        try
        {
            await Page.EvaluateAsync("() => bridge.applyConfigChanges(JSON.stringify({ host: { Theme: 'light' } }))");
            await frame.WaitForFunctionAsync(follows, before, new FrameWaitForFunctionOptions { Timeout = 10_000 });
        }
        finally
        {
            await Page.EvaluateAsync("() => bridge.applyConfigChanges(JSON.stringify({ host: { Theme: 'dark' } }))");
            await frame.WaitForFunctionAsync(
                "before => __stats.state.colors?.grid === before", before, new FrameWaitForFunctionOptions { Timeout = 10_000 });
        }
    }
}
