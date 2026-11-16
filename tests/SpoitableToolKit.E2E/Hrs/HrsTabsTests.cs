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
              delete window.__hds;
              render(await spoitable.call('getStatus'));
            }
            """);
        await Page.EvaluateAsync("() => { const frame = document.querySelector(\"iframe.view[data-id='hrs']\"); if (frame) frame.style.width = frame.style.maxWidth = ''; }");

        // 別の画面へ移した試験が途中で落ちても、隠れたパネルのタブは押せないので、先にパネルを出す。
        await Page.Locator("#sidebar .item[data-id='hrs']").ClickAsync();
        await Panel.Locator("#tabNow").ClickAsync();
    }

    /// <summary>C# からの push で描き換わらないよう、受け口だけ止める。</summary>
    private Task FreezeRender() => Inside.EvaluateAsync("() => { window.__realRender = render; render = () => {}; }");

    private async Task<IFrame> FrameAsync() =>
        await (await Page.Locator("iframe.view[data-id='hrs']").ElementHandleAsync()).ContentFrameAsync()
        ?? throw new InvalidOperationException("心拍のパネルに frame が無い");

    /// <summary>
    /// 心拍計の一覧を読み直す口だけを包み、渡した JS の式（呼ばれるたびに評価する）で答える。呼ばれた口の名前は <c>__calls</c> に積む。
    /// 「〜前」は手元の時計で数えるので、時刻は呼ばれたときの <c>Date.now()</c> から作る。
    /// Apple Watch (HDS) の欄は、そのとき <c>window.__hds</c> に置いてあるもの（置いていなければ HDS が OFF と同じ）。
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
            return Promise.resolve({ discovered: {{rows}}, hds: window.__hds });
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
    /// 接続中の 1 台が広告をやめていると、C# は電波の強さを渡さず、時刻を「いま」にする（HrsMonitorListTests）。
    /// その行は「接続中・たった今」で、dBm の欄を出さない（古い数字も「undefined dBm」も出さない）。
    /// </summary>
    [Fact]
    public async Task AConnectedRowWithoutASignalShowsNoDbm()
    {
        await FreezeRender();
        await AnswerMonitors("[{ name: 'Polar H10 1234', address: 'A0:9E:1A:00:00:01', lastSeen: ago(0), connected: true }]");

        await Panel.Locator("#tabMonitors").ClickAsync();

        var row = Rows.Nth(0);
        await Assertions.Expect(row.Locator(".tag")).ToHaveTextAsync("接続中");
        await Assertions.Expect(row.Locator(".seen")).ToHaveTextAsync("たった今");
        await Assertions.Expect(row.Locator(".signal")).ToHaveCountAsync(0);
        await Assertions.Expect(row).Not.ToContainTextAsync("dBm");
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

    /// <summary>
    /// シェルがほかの画面へ移るとパネルの iframe は隠れるだけで、visibilitychange は来ない。心拍計タブを開いたままでも、
    /// 隠れている間は読み直しを止め、戻ったらまた読み直す。
    /// </summary>
    [Fact]
    public async Task TheMonitorsTabStopsReadingWhileThePanelIsHidden()
    {
        await FreezeRender();
        await AnswerMonitors("[{ name: 'Belt', address: 'A0:9E:1A:00:00:01', signal: -50 - n, lastSeen: ago(0), connected: false }]");

        await Panel.Locator("#tabMonitors").ClickAsync();
        await Assertions.Expect(Rows.Nth(0).Locator(".signal")).ToHaveTextAsync("-50 dBm");
        var frame = await FrameAsync();
        await frame.WaitForFunctionAsync("() => __monitors.watching", null, new FrameWaitForFunctionOptions { Timeout = 5_000 });

        await Page.Locator("#sidebar .item[data-id='log']").ClickAsync();
        await frame.WaitForFunctionAsync("() => !__monitors.watching", null, new FrameWaitForFunctionOptions { Timeout = 5_000 });
        var hidden = await Rows.Nth(0).Locator(".signal").TextContentAsync();

        // 戻ったら、次の周期を待たずに 1 回読む（答えるたびに電波の強さが変わる）。
        await Page.Locator("#sidebar .item[data-id='hrs']").ClickAsync();
        await frame.WaitForFunctionAsync("() => __monitors.watching", null, new FrameWaitForFunctionOptions { Timeout = 5_000 });
        await Assertions.Expect(Rows.Nth(0).Locator(".signal")).Not.ToHaveTextAsync(hidden!, new() { Timeout = 2_000 });
    }

    /// <summary>
    /// Apple Watch (HDS) の欄は、HDS の受信が ON の間だけ（C# が getMonitors に hds を載せる間だけ。載せるかは HrsMonitorListTests）。
    /// 宛先ごとに口の名前を添え、ON のままアドレスが無ければ 1 行の案内。OFF にしたら、次の読み直しで隠れる。
    /// e2e のアプリでは本物の HDS を ON にしない（0.0.0.0 で待ち受けて、ファイアウォールの確認が出ることがある）。
    /// </summary>
    [Fact]
    public async Task TheWatchAddressesShowOnlyWhileHdsIsOn()
    {
        await FreezeRender();
        await AnswerMonitors("[]");
        await Inside.EvaluateAsync(
            "() => { window.__hds = { addresses: [{ name: 'Wi-Fi', endpoint: '192.168.1.20:3476' }, { name: 'Ethernet 2', endpoint: '10.0.0.5:3476' }] }; }");

        await Panel.Locator("#tabMonitors").ClickAsync();
        await Assertions.Expect(Panel.Locator("#hds")).ToBeVisibleAsync();
        await Assertions.Expect(Panel.Locator("#hdsAddresses .url code")).ToHaveTextAsync(["192.168.1.20:3476", "10.0.0.5:3476"]);
        await Assertions.Expect(Panel.Locator("#hdsAddresses .url .note")).ToHaveTextAsync(["Wi-Fi", "Ethernet 2"]);

        await Inside.EvaluateAsync("() => { window.__hds = undefined; }");
        await Assertions.Expect(Panel.Locator("#hds")).ToBeHiddenAsync(new() { Timeout = 10_000 });

        await Inside.EvaluateAsync("() => { window.__hds = { addresses: [] }; }");
        await Assertions.Expect(Panel.Locator("#hds")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(Panel.Locator("#hdsAddresses"))
            .ToHaveTextAsync(await Inside.EvaluateAsync<string>("() => spoitable.t('hrs.panel.hds.none')"));
    }

    /// <summary>
    /// 「コピー」で <c>IP:ポート</c> がクリップボードに入る。e2e のアプリのクリップボードはアプリの中だけにあり、試験から直に読む口は無いので、
    /// 別のパネル（動画 URL）の欄へ右クリックのメニューから貼って見る。
    /// </summary>
    [Fact]
    public async Task CopyingAWatchAddressPutsItOnTheClipboard()
    {
        await FreezeRender();
        await AnswerMonitors("[]");
        await Inside.EvaluateAsync("() => { window.__hds = { addresses: [{ name: 'Wi-Fi', endpoint: '192.168.1.20:3476' }] }; }");

        await Panel.Locator("#tabMonitors").ClickAsync();
        var copy = Panel.Locator("#hdsAddresses .url button");
        await copy.ClickAsync();
        await Assertions.Expect(copy).ToHaveTextAsync("コピーしました");

        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();
        var url = Page.FrameLocator("iframe.view[data-id='videourl']").Locator("#url");
        await Assertions.Expect(url).ToBeVisibleAsync(new() { Timeout = 20_000 });

        try
        {
            await url.FillAsync("");

            var turns = await Page.EvaluateAsync<int>("() => window.__stkCtx?.turns ?? 0");
            await url.ClickAsync(new() { Button = MouseButton.Right });
            await Page.WaitForFunctionAsync($"() => (window.__stkCtx?.turns ?? 0) > {turns}");
            await Page.Locator("#ctxMenu button[data-id='host paste']").ClickAsync();

            await Assertions.Expect(url).ToHaveValueAsync("192.168.1.20:3476");
        }
        finally
        {
            await url.FillAsync("");
        }
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

    /// <summary>
    /// 配信の状態を渡す（ポートは何も待っていない番号。URL に出るだけ）。<paramref name="feed"/> は配信が最後に流した 1 通（JS の式。無ければ null）。
    /// </summary>
    private Task RenderBroadcast(bool listening, string feed = "null") => Inside.EvaluateAsync(
        $$"""
        (_, listening) => __realRender({
          connected: false, receiving: false, running: true, connection: 'none', heartRate: 0,
          samples: [{ bpm: 70, at: '2026-11-16T03:00:00Z' }, { bpm: 0, at: '2026-11-16T03:00:01Z' }, { bpm: 75, at: '2026-11-16T03:00:02Z' }],
          wsEnabled: listening, wsStatus: listening ? 'listening' : 'disabled', wsPort: 1, wsClients: 2,
          overlays: ['pill', 'glass', 'neon', 'widget'],
          overlayFeed: {{feed}},
        })
        """, listening);

    private ILocator Cards => Panel.Locator("#urls .overlay");

    private ILocator Previews => Panel.Locator("#urls .overlay .preview iframe");

    /// <summary>配信のタブ: 待ち受けの様子と、オーバーレイ 1 つにつき 1 枚のカード（名前・説明と大きさ・OBS に貼る URL）。切ってあっても、カードは出す。</summary>
    [Fact]
    public async Task TheBroadcastTabShowsACardPerOverlayWithItsUrl()
    {
        await FreezeRender();
        await Panel.Locator("#tabBroadcast").ClickAsync();
        await RenderBroadcast(listening: true);

        await Assertions.Expect(Panel.Locator("#wsState"))
            .ToHaveTextAsync(await Inside.EvaluateAsync<string>("() => spoitable.t('hrs.panel.ws.listening', 1, 2)"));
        await Assertions.Expect(Cards.Locator(".overlay-name")).ToHaveTextAsync(["Pill Badge", "Glass Card", "Neon Ring", "Full Widget"]);
        await Assertions.Expect(Cards.Locator(".overlay-desc")).ToHaveTextAsync(
        [
            await Inside.EvaluateAsync<string>("() => spoitable.t('hrs.panel.overlays.pill') + ' · 400×112'"),
            await Inside.EvaluateAsync<string>("() => spoitable.t('hrs.panel.overlays.glass') + ' · 600×260'"),
            await Inside.EvaluateAsync<string>("() => spoitable.t('hrs.panel.overlays.neon') + ' · 384×384'"),
            await Inside.EvaluateAsync<string>("() => spoitable.t('hrs.panel.overlays.widget') + ' · 680×320'"),
        ]);
        await Assertions.Expect(Cards.Locator(".url code")).ToHaveTextAsync(
            ["http://localhost:1/overlay/pill", "http://localhost:1/overlay/glass", "http://localhost:1/overlay/neon", "http://localhost:1/overlay/widget"]);

        await RenderBroadcast(listening: false);

        await Assertions.Expect(Panel.Locator("#wsState"))
            .ToHaveTextAsync(await Inside.EvaluateAsync<string>("() => spoitable.t('hrs.panel.ws.off')"));
        await Assertions.Expect(Cards).ToHaveCountAsync(4);
    }

    /// <summary>
    /// プレビュー（オーバーレイそのものを縮めた iframe）は、OBS が開くのと同じ頁を<b>パネルと同じ origin</b> で、印 <c>preview</c> を付けて開く
    /// （パネルからは http://localhost を開けない）。配信を切っていても出し、配信タブを開いていてパネルが見えている間だけ置く。
    /// 外れたら iframe ごと外す（4 枚が動き続けると CPU と GPU を食う）。
    /// </summary>
    [Fact]
    public async Task TheLivePreviewsRunOnlyWhileTheTabIsShown()
    {
        await FreezeRender();
        await Panel.Locator("#tabBroadcast").ClickAsync();

        await RenderBroadcast(listening: false);
        await Assertions.Expect(Cards).ToHaveCountAsync(4);
        await Assertions.Expect(Previews).ToHaveCountAsync(4);
        Assert.Equal(
        [
            "spoitable://hrs/overlays/pill.html?preview", "spoitable://hrs/overlays/glass.html?preview",
            "spoitable://hrs/overlays/neon.html?preview", "spoitable://hrs/overlays/widget.html?preview",
        ], await Previews.EvaluateAllAsync<string[]>("frames => frames.map(frame => frame.src)"));

        // 毎拍の push で作り直さない（作り直すと、プレビューが毎秒読み直しになる）。
        await Previews.First.EvaluateAsync("frame => { frame.dataset.kept = 'yes'; }");
        await RenderBroadcast(listening: true);
        await Assertions.Expect(Previews.First).ToHaveAttributeAsync("data-kept", "yes");

        await Panel.Locator("#tabNow").ClickAsync();
        await Assertions.Expect(Previews).ToHaveCountAsync(0);

        await Panel.Locator("#tabBroadcast").ClickAsync();
        await Assertions.Expect(Previews).ToHaveCountAsync(4);

        await Page.Locator("#sidebar .item[data-id='log']").ClickAsync();
        await Assertions.Expect(Previews).ToHaveCountAsync(0, new() { Timeout = 5_000 });

        await Page.Locator("#sidebar .item[data-id='hrs']").ClickAsync();
        await Assertions.Expect(Previews).ToHaveCountAsync(4, new() { Timeout = 5_000 });
    }

    /// <summary>
    /// プレビューは、配信が最後に流した 1 通をパネル経由で受けて、OBS と同じものを出す。開いた直後は welcome（いまの様子と直近の心拍）、
    /// そのあとは新しい 1 通（何通目かが変わったとき）だけ。切れたら「繋がっていない」の出し方になる。頁はパネルと同じ origin なので、中の DOM を読んで見る。
    /// </summary>
    [Fact]
    public async Task APreviewShowsTheBroadcastMessageItIsGiven()
    {
        await FreezeRender();
        await Panel.Locator("#tabBroadcast").ClickAsync();

        const string beating =
            "{ sent: 1, latest: { type: 'hr_update', bpm: 88, zone: 'moderate', connected: true, avg: 80, min: 70, max: 90, device: 'Belt' } }";
        await RenderBroadcast(listening: false, beating);

        var pill = Panel.FrameLocator(".overlay[data-overlay='pill'] iframe");
        var widget = Panel.FrameLocator(".overlay[data-overlay='widget'] iframe");
        await Assertions.Expect(pill.Locator("#bpm")).ToHaveTextAsync("88", new() { Timeout = 10_000 });
        await Assertions.Expect(widget.Locator("#avg")).ToHaveTextAsync("80");
        await Assertions.Expect(widget.Locator("#zone")).ToHaveTextAsync("Moderate Zone");

        // welcome の直近の心拍（グラフの 0 を除いた 70・75）と、その 1 通の 88 で 3 点。同じ 1 通（同じ番号）は渡し直さない（渡すとグラフに点が積もる）。
        await Assertions.Expect(widget.Locator("#bpm")).ToHaveTextAsync("88");
        await RenderBroadcast(listening: false, beating);
        await Task.Delay(500);
        Assert.Equal(3, await widget.Locator("body").EvaluateAsync<int>("() => hist.length"));

        // 頁が受けるのは親（このパネル）からだけ。シェル（いちばん上の窓）から送っても出ない。
        await Page.EvaluateAsync(
            """
            () => document.querySelector("iframe.view[data-id='hrs']").contentWindow.frames[0]
              .postMessage({ type: 'hr_update', bpm: 111, zone: 'hard', connected: true, avg: 111, min: 111, max: 111 }, '*')
            """);
        await Task.Delay(500);
        await Assertions.Expect(pill.Locator("#bpm")).ToHaveTextAsync("88");

        const string lost =
            "{ sent: 2, latest: { type: 'hr_update', bpm: 0, zone: 'rest', connected: false, avg: 80, min: 70, max: 90 } }";
        await RenderBroadcast(listening: false, lost);

        await Assertions.Expect(pill.Locator("#bpm")).ToHaveTextAsync("--");
        await Assertions.Expect(pill.Locator("body")).ToHaveClassAsync("off");
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
