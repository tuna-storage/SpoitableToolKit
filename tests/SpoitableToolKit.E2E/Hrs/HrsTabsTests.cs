using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Hrs;

/// <summary>
/// 心拍のパネルの 4 つのタブ（現在｜統計｜心拍計｜配信）。e2e のアプリは居ない心拍計を指していて本物の BLE は見えないので、
/// 状態は <c>render</c> を止めて渡し、心拍計の一覧を読み直す口（<c>getMonitors</c>）は <c>spoitable.call</c> を包んで答える。
/// 止めたものは <see cref="DisposeAsync"/> で本物に戻す。
/// </summary>
public sealed class HrsTabsTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private IPage Page => app.Page;

    private IFrameLocator Panel => Page.FrameLocator("iframe.view[data-id='hrs']");

    /// <summary>パネルの中で JS を走らせる口（Locator の evaluate は 1 つ目の引数に要素が来る）。</summary>
    private ILocator Inside => Panel.Locator("body");

    private ILocator Tabs => Panel.Locator("nav.spoitable-tabs > .spoitable-tab");

    private ILocator Rows => Panel.Locator("#devices .found");

    public async Task InitializeAsync()
    {
        if (await Page.Locator("#modal").IsVisibleAsync()) await Page.Locator("#modalNo").ClickAsync();

        await Page.Locator("#sidebar .item[data-id='hrs']").ClickAsync();
        await Assertions.Expect(Panel.Locator("#tabBroadcast")).Not.ToHaveTextAsync("", new() { Timeout = 20_000 });
    }

    public async Task DisposeAsync()
    {
        await Inside.EvaluateAsync(
            """
            async () => {
              if (window.__realRender) { render = window.__realRender; delete window.__realRender; }
              if (window.__realCall) { spoitable.call = window.__realCall; delete window.__realCall; }
              render(await spoitable.call('getStatus'));
            }
            """);
        await Page.EvaluateAsync("() => { const frame = document.querySelector(\"iframe.view[data-id='hrs']\"); if (frame) frame.style.width = frame.style.maxWidth = ''; }");
        await Panel.Locator("#tabNow").ClickAsync();
    }

    /// <summary>C# からの push で描き換わらないよう、受け口だけ止める。</summary>
    private Task FreezeRender() => Inside.EvaluateAsync("() => { window.__realRender = render; render = () => {}; }");

    /// <summary>
    /// 心拍計の一覧を読み直す口だけを包み、渡した JS の式（呼ばれるたびに評価する）で答える。呼ばれた口の名前は <c>__calls</c> に積む。
    /// 「〜前」は手元の時計で数えるので、時刻は呼ばれたときの <c>Date.now()</c> から作る。
    /// </summary>
    private Task AnswerMonitors(string rows) => Inside.EvaluateAsync(
        $$"""
        () => {
          window.__calls = [];
          window.__answers = 0;
          window.__realCall = spoitable.call;
          spoitable.call = (method, payload) => {
            window.__calls.push(method);
            if (method !== 'getMonitors') return window.__realCall(method, payload);
            const n = window.__answers++;
            const ago = seconds => new Date(Date.now() - seconds * 1000).toISOString();
            return Promise.resolve({ discovered: {{rows}} });
          };
        }
        """);

    [Fact]
    public async Task TheFourTabsComeInOrderAndEachShowsOnlyItsOwnPane()
    {
        await Assertions.Expect(Tabs).ToHaveTextAsync(["現在", "統計", "心拍計", "配信"]);

        string[] panes = ["#paneNow", "#paneStats", "#paneMonitors", "#paneBroadcast"];
        for (var open = 0; open < panes.Length; open++)
        {
            await Tabs.Nth(open).ClickAsync();

            for (var pane = 0; pane < panes.Length; pane++)
            {
                if (pane == open) await Assertions.Expect(Panel.Locator(panes[pane])).ToBeVisibleAsync();
                else await Assertions.Expect(Panel.Locator(panes[pane])).ToBeHiddenAsync();

                await Assertions.Expect(Tabs.Nth(pane)).ToHaveAttributeAsync("aria-selected", pane == open ? "true" : "false");
                await Assertions.Expect(Tabs.Nth(pane)).ToHaveAttributeAsync("aria-controls", panes[pane][1..]);
            }
        }
    }

    /// <summary>現在 = 数字とグラフ（ファイアウォールの知らせはその上）。心拍計の一覧と配信の様子は、それぞれのタブにだけある。</summary>
    [Fact]
    public async Task EachSectionLivesOnItsTab()
    {
        var homes = await Inside.EvaluateAsync<string>(
            """
            () => JSON.stringify({
              first: document.getElementById('paneNow').firstElementChild.id,
              ...Object.fromEntries(['firewall', 'bpm', 'min', 'avg', 'max', 'chart', 'devices', 'wsState', 'urls']
                .map(id => [id, document.getElementById(id)?.closest('.pane')?.id ?? null])),
            })
            """);

        Assert.Equal(
            """{"first":"firewall","firewall":"paneNow","bpm":"paneNow","min":"paneNow","avg":"paneNow","max":"paneNow","chart":"paneNow","devices":"paneMonitors","wsState":"paneBroadcast","urls":"paneBroadcast"}""",
            homes);
    }

    /// <summary>
    /// 1 行に電波の強さ・最後に見た時刻・「接続中」。「接続中」は C# がアドレスで決めて渡す（ここは描き方だけ）。
    /// 名前を名乗らない心拍計は名前がアドレスなので、アドレスを 2 回は出さない。
    /// </summary>
    [Fact]
    public async Task TheMonitorRowsShowTheSignalTheLastSeenTimeAndWhichIsConnected()
    {
        await FreezeRender();
        await AnswerMonitors(
            """
            [
              { name: 'Polar H10 1234', address: 'A0:9E:1A:00:00:01', signal: -62, lastSeen: ago(125), connected: true },
              { name: 'A0:9E:1A:00:00:02', address: 'A0:9E:1A:00:00:02', signal: -88, lastSeen: ago(2), connected: false },
            ]
            """);

        await Panel.Locator("#tabMonitors").ClickAsync();

        var connected = Rows.Nth(0);
        await Assertions.Expect(connected.Locator(".name")).ToHaveTextAsync("Polar H10 1234");
        await Assertions.Expect(connected.Locator(".address")).ToHaveTextAsync("A0:9E:1A:00:00:01");
        await Assertions.Expect(connected.Locator(".signal")).ToHaveTextAsync("-62 dBm");
        await Assertions.Expect(connected.Locator(".seen")).ToHaveTextAsync("2 分前");
        await Assertions.Expect(connected.Locator(".tag")).ToHaveTextAsync("接続中");
        await Assertions.Expect(connected).ToHaveClassAsync("found here");

        var other = Rows.Nth(1);
        await Assertions.Expect(other.Locator(".name")).ToHaveTextAsync("A0:9E:1A:00:00:02");
        await Assertions.Expect(other.Locator(".address")).ToHaveCountAsync(0);
        await Assertions.Expect(other.Locator(".signal")).ToHaveTextAsync("-88 dBm");
        await Assertions.Expect(other.Locator(".seen")).ToHaveTextAsync("たった今");
        await Assertions.Expect(other.Locator(".tag")).ToHaveCountAsync(0);
        await Assertions.Expect(other).ToHaveClassAsync("found");
    }

    /// <summary>
    /// 広告はパネルへ push しないので、心拍計タブを開いている間はパネルが一覧を読み直す（答えるたびに電波の強さを変えて、描き直したのを見る）。
    /// 読むのは一覧だけの口で、記録を数える getStatus は呼ばない。ほかのタブへ移ったら止める。
    /// </summary>
    [Fact]
    public async Task TheMonitorsTabReadsTheListAgainWhileItIsOpen()
    {
        await FreezeRender();
        await AnswerMonitors("[{ name: 'Belt', address: 'A0:9E:1A:00:00:01', signal: -50 - n, lastSeen: ago(0), connected: false }]");

        await Panel.Locator("#tabMonitors").ClickAsync();
        await Assertions.Expect(Rows.Nth(0).Locator(".signal")).ToHaveTextAsync("-50 dBm");
        await Assertions.Expect(Rows.Nth(0).Locator(".signal")).ToHaveTextAsync("-51 dBm", new() { Timeout = 10_000 });
        Assert.True(await Inside.EvaluateAsync<bool>("() => __monitors.watching"));

        var calls = await Inside.EvaluateAsync<string[]>("() => __calls");
        Assert.DoesNotContain("getStatus", calls);

        await Panel.Locator("#tabNow").ClickAsync();
        Assert.False(await Inside.EvaluateAsync<bool>("() => __monitors.watching"));
    }

    /// <summary>区切り: 10 秒未満は「たった今」、1 分未満は秒、1 時間未満は分、その先は時間。C# が書く時刻（小数 7 桁）も読める。</summary>
    [Fact]
    public async Task TheLastSeenTimeIsSaidInSecondsMinutesOrHours()
    {
        var said = await Inside.EvaluateAsync<string[]>(
            """
            () => {
              const now = Date.parse('2026-11-16T03:00:00Z');
              const at = seconds => new Date(now - seconds * 1000).toISOString();
              return [
                ...[0, 9, 10, 59, 60, 119, 3599, 3600, 7300].map(seconds => __monitors.ago(at(seconds), now)),
                __monitors.ago(null, now),
                __monitors.ago('2026-11-16T02:57:00.0000000+00:00', now),
              ];
            }
            """);

        Assert.Equal(
            ["たった今", "たった今", "10 秒前", "59 秒前", "1 分前", "1 分前", "59 分前", "1 時間前", "2 時間前", "", "3 分前"],
            said);
    }

    /// <summary>配信のタブ: 待ち受けている間は様子と OBS に貼る URL、切ってあれば OFF の案内だけ。</summary>
    [Fact]
    public async Task TheBroadcastTabShowsTheListeningStateAndTheUrls()
    {
        await FreezeRender();
        await Panel.Locator("#tabBroadcast").ClickAsync();

        await Inside.EvaluateAsync(
            """
            () => __realRender({
              connected: false, receiving: false, running: true, connection: 'none', heartRate: 0, samples: [],
              wsEnabled: true, wsStatus: 'listening', wsPort: 9100, wsClients: 2, overlays: ['pill', 'widget'],
            })
            """);

        await Assertions.Expect(Panel.Locator("#wsState"))
            .ToHaveTextAsync(await Inside.EvaluateAsync<string>("() => spoitable.t('hrs.panel.ws.listening', 9100, 2)"));
        await Assertions.Expect(Panel.Locator("#overlays")).ToBeVisibleAsync();
        await Assertions.Expect(Panel.Locator("#urls .url code"))
            .ToHaveTextAsync(["http://localhost:9100/overlay/pill", "http://localhost:9100/overlay/widget"]);

        await Inside.EvaluateAsync(
            """
            () => __realRender({
              connected: false, receiving: false, running: true, connection: 'none', heartRate: 0, samples: [],
              wsEnabled: false, wsStatus: 'disabled', wsPort: 9100, overlays: ['pill', 'widget'],
            })
            """);

        await Assertions.Expect(Panel.Locator("#wsState"))
            .ToHaveTextAsync(await Inside.EvaluateAsync<string>("() => spoitable.t('hrs.panel.ws.off')"));
        await Assertions.Expect(Panel.Locator("#overlays")).ToBeHiddenAsync();
    }

    /// <summary>狭い窓（VR の窓は字が 1.5 倍）でも、4 つのタブは 1 段に収まり、一覧の行は横にはみ出さない。</summary>
    [Fact]
    public async Task TheTabsAndTheRowsFitANarrowPanel()
    {
        await FreezeRender();
        await AnswerMonitors(
            "[{ name: 'Polar Verity Sense 0E7F1234', address: 'A0:9E:1A:00:00:01', signal: -71, lastSeen: ago(12), connected: true }]");
        // .view は flex で伸びるので、幅だけでなく上限も付ける。
        await Page.EvaluateAsync("() => { const frame = document.querySelector(\"iframe.view[data-id='hrs']\"); frame.style.width = frame.style.maxWidth = '400px'; }");
        var frame = await (await Page.Locator("iframe.view[data-id='hrs']").ElementHandleAsync()).ContentFrameAsync()
            ?? throw new InvalidOperationException("心拍のパネルに frame が無い");
        await frame.WaitForFunctionAsync("() => innerWidth <= 400", null, new FrameWaitForFunctionOptions { Timeout = 5_000 });

        await Panel.Locator("#tabMonitors").ClickAsync();
        await Assertions.Expect(Rows.Nth(0).Locator(".tag")).ToBeVisibleAsync();

        var layout = await Inside.EvaluateAsync<string>(
            """
            () => JSON.stringify({
              rows: new Set([...document.querySelectorAll('nav.spoitable-tabs > .spoitable-tab')].map(tab => tab.offsetTop)).size,
              overflow: document.documentElement.scrollWidth > innerWidth,
            })
            """);

        Assert.Equal("""{"rows":1,"overflow":false}""", layout);
    }
}
