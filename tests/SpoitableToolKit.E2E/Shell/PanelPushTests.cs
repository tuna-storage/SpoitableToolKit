using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Shell;

/// <summary>ネットワークには出ないので yt-dlp は要らない。アプリはこのクラスで 1 つなので、件数や n 番目ではなく自分の URL を含む行で見る。</summary>
public sealed class PanelPushTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private IPage Page => app.Page;

    private IFrameLocator Panel => Page.FrameLocator("iframe.view[data-id='videourl']");

    /// <summary>押すと本当にネットワークへ出るので、分岐だけを呼んで確かめる。</summary>
    [Fact]
    public async Task ThePanelSaysWhichWayItIsWaitingOn()
    {
        // フレームの中で評価する（Panel.Owner は親側の iframe 要素で、そちらで呼ぶとパネルの関数が見えない）。
        var inside = Panel.Locator("body");

        var local = await inside.EvaluateAsync<string>("() => runningKey({ server: false })");
        var server = await inside.EvaluateAsync<string>("() => runningKey({ server: true })");

        Assert.Equal("videourl.panel.running", local);
        Assert.Equal("videourl.panel.running.server", server);

        // 聞けなかったときは既定のまま（文言だけの話なので、そこで止めない）。
        Assert.Equal("videourl.panel.running",
            await inside.EvaluateAsync<string>("() => runningKey(null)"));
    }

    /// <summary>ネットワークへ出る呼び出しだけを凍らせる（解決は押した瞬間に外へ出るため）。getMode は答えるのが C# 側なので本物のまま通す。</summary>
    [Fact]
    public async Task TheWaitingMessageOnScreenFollowsTheWay()
    {
        var inside = Panel.Locator("body");

        await inside.EvaluateAsync("() => { window.__realCall = spoitable.call.bind(spoitable); spoitable.call = (method, payload) => method === 'resolve' ? new Promise(() => {}) : window.__realCall(method, payload); }");

        try
        {
            await Panel.Locator("#url").FillAsync("https://www.youtube.com/watch?v=e2e-waiting");

            await Panel.Locator("#go").ClickAsync();
            await Assertions.Expect(Panel.Locator("#msg")).ToHaveTextAsync(
                await inside.EvaluateAsync<string>("() => spoitable.t('videourl.panel.running')"));

            await Page.EvaluateAsync(
                "() => bridge.applyConfigChanges(JSON.stringify({ videourl: { 'Server.Enabled': true } }))");

            // 解決を凍らせてあるので busy のままになり、戻さないと 2 回目が届かない。
            await inside.EvaluateAsync("() => busy(false)");

            await Panel.Locator("#go").ClickAsync();
            await Assertions.Expect(Panel.Locator("#msg")).ToHaveTextAsync(
                await inside.EvaluateAsync<string>("() => spoitable.t('videourl.panel.running.server')"));
        }
        finally
        {
            await Page.EvaluateAsync(
                "() => bridge.applyConfigChanges(JSON.stringify({ videourl: { 'Server.Enabled': false } }))");

            await inside.EvaluateAsync("() => { spoitable.call = window.__realCall; }");
        }
    }

    /// <summary>VRChat が実際に書く 4 行の形に合わせる。</summary>
    private void Fail(string url, string direct)
    {
        _failed = true;
        app.AppendLog(
            $"[Video Playback] Attempting to resolve URL '{url}'",
            $"[Video Playback] URL '{url}' resolved to '{direct}'",
            $"[AVProVideo] Opening {direct} (offset 0) with API MediaFoundation",
            "[AVProVideo] Error: Loading failed.  e2e");
    }

    private bool _failed;

    /// <summary>確認のモーダルは既定で 2 分出てシェル全体を覆うので、残すと次のテストのクリックが届かなくなる。片付けは DisposeAsync でやる（本体の最後だと、失敗したときに残る）。</summary>
    /// <param name="waitMs">0 は渡さない（Playwright では無制限に待つ）。待たずに見るなら IsVisibleAsync。</param>
    private async Task DismissModalAsync(int waitMs)
    {
        var modal = Page.Locator("#modal");
        try
        {
            await modal.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = waitMs });
        }
        catch (TimeoutException)
        {
            return;
        }

        await Page.Locator("#modalNo").ClickAsync();
    }

    public async Task InitializeAsync()
    {
        if (await Page.Locator("#modal").IsVisibleAsync()) await Page.Locator("#modalNo").ClickAsync();

        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();
    }

    /// <summary>検出から確認までは非同期なので、失敗を流した試験だけ少し待ってから畳む。</summary>
    public async Task DisposeAsync()
    {
        if (_failed) await DismissModalAsync(waitMs: 5_000);
        else if (await Page.Locator("#modal").IsVisibleAsync()) await Page.Locator("#modalNo").ClickAsync();
    }

    [Fact]
    public async Task FailuresArePushedIntoAnOpenPanel()
    {
        const string url = "https://www.youtube.com/watch?v=e2ePush";

        await Assertions.Expect(Panel.Locator("#failures")).ToBeVisibleAsync();

        Fail(url, "https://e2e.invalid/push");

        var row = Panel.Locator($".failure[data-url='{url}']");
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await Assertions.Expect(row).ToContainTextAsync("×1");
        await Assertions.Expect(row).ToContainTextAsync("Loading failed.");
    }

    [Fact]
    public async Task ARetryRaisesTheCountWithoutAddingARow()
    {
        const string url = "https://www.youtube.com/watch?v=e2ePushRetry";

        // VRChat は失敗すると同じ URL でやり直す。
        for (var attempt = 0; attempt < 3; attempt++) Fail(url, $"https://e2e.invalid/retry{attempt}");

        var row = Panel.Locator($".failure[data-url='{url}']");
        await Assertions.Expect(row).ToContainTextAsync("×3", new() { Timeout = 20_000 });
        await Assertions.Expect(row).ToHaveCountAsync(1);
    }

    /// <summary>push は届かなかった分を取り返さない。パネルは読み込み時に getFailures で追いつく。</summary>
    [Fact]
    public async Task TheListIsCompleteAgainAfterAReload()
    {
        const string url = "https://www.youtube.com/watch?v=e2ePushReload";

        Fail(url, "https://e2e.invalid/reload");

        await Assertions.Expect(Panel.Locator($".failure[data-url='{url}']"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        var frame = Page.Frames.First(f => f.Url.Contains("videourl", StringComparison.Ordinal));

        // 目印を置いてから読み込み直す（待たずに見ると reload 前の行を見て緑になる）。
        try
        {
            await frame.EvaluateAsync("() => { window.__beforeReload = true; location.reload(); }");
        }
        catch (PlaywrightException)
        {
            // reload で実行コンテキストが消えることがある。
        }

        await frame.WaitForFunctionAsync(
            "() => window.__panelReady === true && window.__beforeReload === undefined",
            null, new FrameWaitForFunctionOptions { Timeout = 20_000 });

        await Assertions.Expect(Panel.Locator($".failure[data-url='{url}']")).ToBeVisibleAsync();
    }

    /// <summary>最初の一覧（getFailures）は C# が答えた時点の写しで、その返事が後から来た push（一覧まるごと）より遅れて着くと、push で出た行を消してしまう。シェルで最初の返事を止めてその形を作る（止めるのは返事を渡すところだけで、中身は本物）。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFailurePushedWhileThePanelLoadsSurvivesTheLateFirstList(bool firstListFails)
    {
        var url = $"https://www.youtube.com/watch?v=e2ePushLate{(firstListFails ? "Error" : "")}";

        await Assertions.Expect(Panel.Locator("#failures")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        var frame = Page.Frames.First(f => f.Url.Contains("videourl", StringComparison.Ordinal));

        await Page.EvaluateAsync("""
            fails => {
              window.__realCallModule = bridge.callModule;
              bridge.callModule = (module, method, payload) => {
                const answer = window.__realCallModule(module, method, payload);
                if (module !== 'videourl' || method !== 'getFailures') return answer;
                bridge.callModule = window.__realCallModule;   // 止めるのは読み込みの最初の 1 回だけ
                return answer
                  .then(response => fails ? { ok: false, error: 'e2e: the first list failed' } : response)
                  .then(response => new Promise(resolve => { window.__releaseFirstList = () => resolve(response); }));
              };
            }
            """, firstListFails);

        try
        {
            try
            {
                await frame.EvaluateAsync("() => { window.__beforeReload = true; location.reload(); }");
            }
            catch (PlaywrightException)
            {
                // reload で実行コンテキストが消えることがある。
            }

            await frame.WaitForFunctionAsync(
                "() => window.__panelReady === true && window.__beforeReload === undefined",
                null, new FrameWaitForFunctionOptions { Timeout = 20_000 });

            await Page.WaitForFunctionAsync("() => typeof window.__releaseFirstList === 'function'",
                null, new PageWaitForFunctionOptions { Timeout = 20_000 });

            // bridge.js の受け口より後に足すので、これが立てば描くのは済んでいる。
            await frame.EvaluateAsync("""
                () => addEventListener('message', event => {
                  if (Array.isArray(event.data?.result) || event.data?.error === 'e2e: the first list failed') window.__firstListArrived = true;
                })
                """);

            Fail(url, "https://e2e.invalid/late");

            var row = Panel.Locator($".failure[data-url='{url}']");
            await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 20_000 });

            await Page.EvaluateAsync("() => window.__releaseFirstList()");
            await frame.WaitForFunctionAsync("() => window.__firstListArrived === true",
                null, new FrameWaitForFunctionOptions { Timeout = 10_000 });

            await Assertions.Expect(row).ToHaveCountAsync(1);
        }
        finally
        {
            await Page.EvaluateAsync("() => { bridge.callModule = window.__realCallModule; window.__releaseFirstList?.(); }");
        }
    }

    /// <summary>モジュールのパネルは空の &lt;option&gt; を並べ、字は data-i18n で訳が届いてから入る。作ったときに 1 度読むだけだとボタンが空のまま固まる。シェル側（#logSize）は HTML に字があるので、ここでしか捕まらない。</summary>
    [Fact]
    public async Task ThePanelDropdownGetsItsLabelOnceTheStringsArrive()
    {
        await Page.Locator("#sidebar .item[data-id='hrs']").ClickAsync();

        var hrs = Page.FrameLocator("iframe.view[data-id='hrs']");
        var shown = hrs.Locator(".spoitable-pick-box:has(#range) .spoitable-pick-value");

        await hrs.Locator("#tabStats").ClickAsync(new() { Timeout = 20_000 });

        try
        {
            await Assertions.Expect(shown).ToBeVisibleAsync(new() { Timeout = 20_000 });
            await Assertions.Expect(shown).Not.ToHaveTextAsync("", new() { Timeout = 20_000 });

            await Assertions.Expect(shown).Not.ToContainTextAsync("hrs.panel.records.range");
        }
        finally
        {
            await hrs.Locator("#tabNow").ClickAsync();
        }
    }

    /// <summary>e2e のアプリは居ない心拍計を指しているので本物の心拍は来ない。状態を渡して、パネルの描き方だけを確かめる。</summary>
    [Fact]
    public async Task TheHeartRateTilesShowTheConnectionNotTheChart()
    {
        await Page.Locator("#sidebar .item[data-id='hrs']").ClickAsync();

        var hrs = Page.FrameLocator("iframe.view[data-id='hrs']");
        var inside = hrs.Locator("body");
        await Assertions.Expect(hrs.Locator("#export")).Not.ToHaveTextAsync("", new() { Timeout = 20_000 });

        // C# からの push で描き換わらないよう、受け口だけ止める。
        await inside.EvaluateAsync("() => { window.__realRender = render; render = () => {}; }");

        try
        {
            // グラフの 2 拍（80・90）とは違う数字を渡す（グラフから数えていれば 85 になる）。
            await inside.EvaluateAsync("""
                () => __realRender({
                  connected: true, connection: 'bleGatt', heartRate: 90, running: true,
                  samples: [{ bpm: 80, at: '2026-11-10T12:00:00Z' }, { bpm: 90, at: '2026-11-10T12:00:01Z' }],
                  stats: { min: 61, avg: 70, max: 99 },
                })
                """);

            Assert.Equal(["61", "70", "99"], await Tiles(hrs));

            await inside.EvaluateAsync("""
                () => __realRender({
                  connected: true, connection: 'bleGatt', heartRate: 0, running: true,
                  samples: [{ bpm: 80, at: '2026-11-10T12:00:00Z' }, { bpm: 90, at: '2026-11-10T12:00:01Z' }],
                })
                """);

            Assert.Equal(["—", "—", "—"], await Tiles(hrs));

            // グラフは 2 拍ないと線を引かないが、数字は 1 拍目から出す。
            await inside.EvaluateAsync("""
                () => __realRender({
                  connected: true, connection: 'bleGatt', heartRate: 75, running: true,
                  samples: [{ bpm: 75, at: '2026-11-10T12:00:02Z' }],
                  stats: { min: 75, avg: 75, max: 75 },
                })
                """);

            Assert.Equal(["75", "75", "75"], await Tiles(hrs));
        }
        finally
        {
            // 本物に戻す（後の試験が描き換えた画面を見ないように）。
            await inside.EvaluateAsync("async () => { render = __realRender; render(await spoitable.call('getStatus')); }");
        }
    }

    /// <summary>状態を渡して描き方だけを見る（いつ届いていないとするかは HrsSourceTests）。</summary>
    [Fact]
    public async Task TheHeartRateStateSaysReceivingOnlyWhileBeatsArrive()
    {
        await Page.Locator("#sidebar .item[data-id='hrs']").ClickAsync();

        var hrs = Page.FrameLocator("iframe.view[data-id='hrs']");
        var inside = hrs.Locator("body");
        await Assertions.Expect(hrs.Locator("#export")).Not.ToHaveTextAsync("", new() { Timeout = 20_000 });

        await inside.EvaluateAsync("() => { window.__realRender = render; render = () => {}; }");

        try
        {
            async Task Shows(string connected, string receiving, string key, string className)
            {
                await inside.EvaluateAsync($$"""
                    () => __realRender({ connected: {{connected}}, receiving: {{receiving}}, connection: 'bleGatt', heartRate: 0, running: true, samples: [] })
                    """);

                var expected = await inside.EvaluateAsync<string>($"() => spoitable.t('{key}', 'BLE (GATT)')");
                await Assertions.Expect(hrs.Locator("#stateText")).ToHaveTextAsync(expected);
                Assert.Equal(className, await hrs.Locator("#state").GetAttributeAsync("class"));
            }

            await Shows("true", "true", "hrs.panel.state.receiving", "state on");
            await Shows("true", "false", "hrs.panel.state.noHeartRate", "state quiet");
            await Shows("false", "false", "hrs.panel.state.disconnected", "state off");
        }
        finally
        {
            await inside.EvaluateAsync("async () => { render = __realRender; render(await spoitable.call('getStatus')); }");
        }
    }

    /// <summary>0 は C# が置く。ここでは描き方だけを見る（0 の点が枠の底に来て、「直近 N 件」は拍だけを数える）。</summary>
    [Fact]
    public async Task TheChartDropsToZeroWhereTheHeartRateStopped()
    {
        await Page.Locator("#sidebar .item[data-id='hrs']").ClickAsync();

        var hrs = Page.FrameLocator("iframe.view[data-id='hrs']");
        var inside = hrs.Locator("body");
        await Assertions.Expect(hrs.Locator("#export")).Not.ToHaveTextAsync("", new() { Timeout = 20_000 });

        await inside.EvaluateAsync("() => { window.__realRender = render; render = () => {}; }");

        try
        {
            await inside.EvaluateAsync("""
                () => __realRender({ connected: true, receiving: true, connection: 'bleGatt', heartRate: 85, running: true,
                  samples: [{ bpm: 80, at: '2026-11-14T03:00:00Z' }, { bpm: 90, at: '2026-11-14T03:00:01Z' }, { bpm: 0, at: '2026-11-14T03:00:02Z' },
                            { bpm: 0, at: '2026-11-14T03:00:30Z' }, { bpm: 85, at: '2026-11-14T03:00:30Z' }] })
                """);

            var d = await hrs.Locator("#line").GetAttributeAsync("d");
            Assert.Equal("M0.0,13.9 L75.0,5.0 L150.0,85.0 L225.0,85.0 L300.0,9.4", d);

            var expected = await inside.EvaluateAsync<string>("() => spoitable.t('hrs.panel.chart.recent', 3)");
            await Assertions.Expect(hrs.Locator("#span")).ToHaveTextAsync(expected);
        }
        finally
        {
            await inside.EvaluateAsync("async () => { render = __realRender; render(await spoitable.call('getStatus')); }");
        }
    }

    /// <summary>状態を渡して描き方だけを見る。手で切った 1 台は「接続」を押すまで繋ぎ直さない。</summary>
    [Fact]
    public async Task TheToggleOffersConnectForAMonitorDisconnectedByHand()
    {
        await Page.Locator("#sidebar .item[data-id='hrs']").ClickAsync();

        var hrs = Page.FrameLocator("iframe.view[data-id='hrs']");
        var inside = hrs.Locator("body");
        await Assertions.Expect(hrs.Locator("#export")).Not.ToHaveTextAsync("", new() { Timeout = 20_000 });

        await inside.EvaluateAsync("() => { window.__realRender = render; render = () => {}; }");

        try
        {
            await inside.EvaluateAsync("""
                () => __realRender({
                  connected: false, receiving: false, running: true, configured: 'Belt [00:00:00:00:00:01]',
                  heldOff: 'Belt [00:00:00:00:00:01]', connection: 'none', heartRate: 0, samples: [],
                })
                """);

            await Assertions.Expect(hrs.Locator("#toggle")).ToHaveTextAsync(await inside.EvaluateAsync<string>("() => spoitable.t('hrs.panel.connect')"));
            Assert.Equal("connect", await hrs.Locator("#toggle").GetAttributeAsync("data-method"));
            await Assertions.Expect(hrs.Locator("#device"))
                .ToHaveTextAsync(await inside.EvaluateAsync<string>("() => spoitable.t('hrs.panel.device.heldOff', 'Belt [00:00:00:00:00:01]')"));
            await Assertions.Expect(hrs.Locator("#toggleHint")).ToHaveTextAsync(await inside.EvaluateAsync<string>("() => spoitable.t('hrs.panel.heldOff')"));

            await inside.EvaluateAsync("""
                () => __realRender({
                  connected: true, receiving: true, running: true, device: 'Watch', connection: 'hds',
                  heldOff: 'Belt [00:00:00:00:00:01]', heartRate: 70, samples: [],
                })
                """);

            await Assertions.Expect(hrs.Locator("#toggle")).ToHaveTextAsync(await inside.EvaluateAsync<string>("() => spoitable.t('hrs.panel.disconnect')"));
            Assert.Equal("disconnect", await hrs.Locator("#toggle").GetAttributeAsync("data-method"));

            await inside.EvaluateAsync("""
                () => __realRender({
                  connected: false, receiving: false, running: false, connection: 'none',
                  heldOff: 'Belt [00:00:00:00:00:01]', heartRate: 0, samples: [],
                })
                """);

            await Assertions.Expect(hrs.Locator("#toggle")).ToHaveTextAsync(await inside.EvaluateAsync<string>("() => spoitable.t('hrs.panel.connect')"));
            await Assertions.Expect(hrs.Locator("#toggleHint")).ToHaveTextAsync(await inside.EvaluateAsync<string>("() => spoitable.t('hrs.panel.stopped')"));
        }
        finally
        {
            await inside.EvaluateAsync("async () => { render = __realRender; render(await spoitable.call('getStatus')); }");
        }
    }

    private static async Task<string[]> Tiles(IFrameLocator hrs) =>
    [
        await hrs.Locator("#min").InnerTextAsync(),
        await hrs.Locator("#avg").InnerTextAsync(),
        await hrs.Locator("#max").InnerTextAsync(),
    ];
}
