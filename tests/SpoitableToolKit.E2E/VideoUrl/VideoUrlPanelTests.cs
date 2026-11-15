using Microsoft.Playwright;
using SpoitableToolKit.E2E.Shell;
using Xunit;

namespace SpoitableToolKit.E2E.VideoUrl;

/// <summary>
/// <see cref="PanelPushTests"/> とは分けてある。あちらは毎回、確認のモーダルを 5 秒待ってから畳む
/// （失敗を流すので）。ここは失敗を流さないので、その待ちが要らない。
/// </summary>
public sealed class VideoUrlPanelTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private IPage Page => app.Page;

    private IFrameLocator Panel => Page.FrameLocator("iframe.view[data-id='videourl']");

    public async Task InitializeAsync()
    {
        // 前のテストが残したモーダルがあると、サイドバーを押せない。
        if (await Page.Locator("#modal").IsVisibleAsync()) await Page.Locator("#modalNo").ClickAsync();

        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();
        await Assertions.Expect(Panel.Locator("#url")).ToBeVisibleAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>失敗の行の「解決」は欄の value を直に書く（input の出来事が起きない）。そのときにも出ないと、VR で消す手段が無い。</summary>
    [Fact]
    public async Task TheClearButtonShowsOnlyWhileTheFieldHasText()
    {
        var clear = Panel.Locator("#clear");

        await Panel.Locator("#url").FillAsync("");
        await Assertions.Expect(clear).ToBeHiddenAsync();

        await Panel.Locator("#url").FillAsync("https://www.youtube.com/watch?v=e2e-clear");
        await Assertions.Expect(clear).ToBeVisibleAsync();

        await Panel.Locator("#url").FillAsync("");
        await Assertions.Expect(clear).ToBeHiddenAsync();

        await Panel.Locator("body").EvaluateAsync("() => { el.url.value = 'https://www.youtube.com/watch?v=e2e-direct'; }");
        await Assertions.Expect(clear).ToBeVisibleAsync();
    }

    /// <summary>動画 URL の辞書は「コピー」「コピーした」を持っていない（本体が <c>CommonPhrases</c> として出している）。</summary>
    [Fact]
    public async Task ThePanelTakesTheSharedPhrasesFromTheApp()
    {
        await Panel.Locator("body").EvaluateAsync(
            "() => render({ streams: [{ kind: 'muxed', label: '720p', note: null, url: 'https://example.invalid/e2e-common.mp4' }] })");

        try
        {
            var copy = Panel.Locator("#streams .stream button");
            await Assertions.Expect(copy).ToHaveTextAsync("コピー");

            await copy.ClickAsync();
            await Assertions.Expect(copy).ToHaveTextAsync("コピーしました");

            await Assertions.Expect(copy).ToHaveTextAsync("コピー");
        }
        finally
        {
            await Panel.Locator("body").EvaluateAsync("() => el.streams.replaceChildren()");
        }
    }

    /// <summary>
    /// <c>spoitable.phrases.ui.close</c> は C# の <c>CommonPhrases.Ui.Close</c> と同じ並び。
    /// 無い名前はキーのまま画面に出さず、何があるかを添えて投げる（打ち間違いをその場で知らせる）。
    /// </summary>
    [Fact]
    public async Task ASharedPhraseIsReadByNameAndAWrongNameIsAnError()
    {
        var body = Panel.Locator("body");
        const string read = "(body, name) => { try { return name.split('.').reduce((at, part) => at[part], spoitable.phrases); } catch (e) { return 'threw: ' + e.message; } }";

        Assert.Equal("閉じる", await body.EvaluateAsync<string>(read, "ui.close"));
        Assert.Equal("読み込み中…", await body.EvaluateAsync<string>(read, "ui.loading"));

        Assert.Equal(
            "threw: spoitable.phrases.ui.clsoe is not a phrase the app provides. spoitable.phrases.ui has: " +
            "add, apply, back, cancel, close, copied, copy, cut, delete, done, edit, failed, loading, next, no, ok, open, paste, " +
            "reload, remove, retry, revert, save, search, yes. " +
            "Wording of your own goes in your module's locales under '<your module Id>.…' and is read with spoitable.t(key).",
            await body.EvaluateAsync<string>(read, "ui.clsoe"));

        // 名前の途中が違っても同じ。モジュール自身のキーや本体のキー（host.…）は、この口からは引けない。
        Assert.StartsWith("threw: spoitable.phrases.buttons is not a phrase the app provides. spoitable.phrases has: ui. ",
            await body.EvaluateAsync<string>(read, "buttons.close"));
        Assert.StartsWith("threw: spoitable.phrases.videourl is not a phrase the app provides.",
            await body.EvaluateAsync<string>(read, "videourl.name"));

        // await や JSON にしても投げない（名前ではない問い合わせ）。
        Assert.Equal("{}", await body.EvaluateAsync<string>("async () => JSON.stringify(await spoitable.phrases.ui)"));
    }

    /// <summary>
    /// push はページを読み込んだ直後にも来る。文言が届く前に購読者へ渡すと、<c>t()</c> はキーを返し、<c>phrases</c> は投げる。
    /// シェルが文言を返すのを止めておいて、パネルを読み込み直し、その間に push を入れる。
    /// </summary>
    [Fact]
    public async Task APushThatComesBeforeTheWordingIsHandedOverAfterIt()
    {
        await Page.EvaluateAsync("""
            () => {
              window.__e2eStringsGate = new Promise(release => { window.__e2eReleaseStrings = release; });
              window.__e2eGetStrings = bridge.getStrings;
              bridge.getStrings = async () => { await window.__e2eStringsGate; return window.__e2eGetStrings(); };
            }
            """);

        try
        {
            var frame = Page.Frames.First(f => f.Url.Contains("videourl", StringComparison.Ordinal));
            try
            {
                await frame.EvaluateAsync("() => { window.__beforeReload = true; location.reload(); }");
            }
            catch (PlaywrightException)
            {
                // reload で実行コンテキストが消えることがある。目的は果たしている。
            }

            await frame.WaitForFunctionAsync(
                "() => window.__panelReady === true && window.__beforeReload === undefined",
                null, new FrameWaitForFunctionOptions { Timeout = 20_000 });

            // 購読して、シェルから push を入れる（本物の push と同じ道: シェルが iframe へ postMessage する）。
            await frame.EvaluateAsync("""
                () => spoitable.onPush((channel, payload) => {
                  if (channel === 'e2e') (window.__e2eSeen ??= []).push(payload + ':' + spoitable.phrases.ui.close);
                })
                """);
            await Page.EvaluateAsync("""
                () => document.querySelector("iframe.view[data-id='videourl']").contentWindow
                  .postMessage({ stk: 1, kind: 'push', channel: 'e2e', payload: 'first' }, '*')
                """);
            await Page.EvaluateAsync("""
                () => document.querySelector("iframe.view[data-id='videourl']").contentWindow
                  .postMessage({ stk: 1, kind: 'push', channel: 'e2e', payload: 'second' }, '*')
                """);

            // 文言はまだ届いていない。購読者も呼ばれていない。
            var held = await frame.EvaluateAsync<string>("""
                async () => {
                  await new Promise(done => setTimeout(done, 300));
                  let early;
                  try { early = String(spoitable.phrases.ui); } catch (e) { early = 'threw: ' + e.message; }
                  return JSON.stringify([spoitable.t('common.ui.close'), window.__e2eSeen ?? null, early]);
                }
                """);
            Assert.Equal(
                """["common.ui.close",null,"threw: spoitable.phrases.ui was read before the wording arrived. Wait first: await spoitable.whenReady"]""",
                held);

            await Page.EvaluateAsync("() => window.__e2eReleaseStrings()");

            // 届いたら、来た順に渡る。購読者の中で文言が引ける。
            await frame.WaitForFunctionAsync("() => (window.__e2eSeen ?? []).length === 2");
            Assert.Equal("""["first:閉じる","second:閉じる"]""", await frame.EvaluateAsync<string>("() => JSON.stringify(window.__e2eSeen)"));
        }
        finally
        {
            await Page.EvaluateAsync("() => { window.__e2eReleaseStrings(); bridge.getStrings = window.__e2eGetStrings; }");
        }
    }

    /// <summary>空にしたらそのまま次の URL を貼るので、欄へ戻す（VR では長押しの右クリックで貼る）。</summary>
    [Fact]
    public async Task TheClearButtonEmptiesTheFieldAndKeepsTheCaretThere()
    {
        var inside = Panel.Locator("body");
        var url = Panel.Locator("#url");

        await url.FillAsync("https://www.youtube.com/watch?v=e2e-clear");
        var message = await Panel.Locator("#msg").TextContentAsync();

        // 解決へ出たら分かるように、resolve を数える口に差し替える（押しても外へは出ない）。
        await inside.EvaluateAsync("() => { window.__resolves = 0; window.__realCall = spoitable.call.bind(spoitable); spoitable.call = (method, payload) => { if (method === 'resolve') { window.__resolves++; return new Promise(() => {}); } return window.__realCall(method, payload); }; }");

        try
        {
            await Panel.Locator("#clear").ClickAsync();

            Assert.Equal("", await url.InputValueAsync());
            Assert.True(await inside.EvaluateAsync<bool>("() => document.activeElement === el.url"), "欄へ戻っていない");
            await Assertions.Expect(Panel.Locator("#clear")).ToBeHiddenAsync();

            Assert.Equal(0, await inside.EvaluateAsync<int>("() => window.__resolves"));
            Assert.Equal(message, await Panel.Locator("#msg").TextContentAsync());
        }
        finally
        {
            await inside.EvaluateAsync("() => { spoitable.call = window.__realCall; }");
        }
    }

    /// <summary>重ねる基準を欄と「解決」が並ぶ行に取ると、✕ が「解決」の上に乗る。小さいと VR のレーザーで当てにくい。</summary>
    [Fact]
    public async Task TheClearButtonSitsInsideTheFieldAndIsAsTallAsIt()
    {
        await Panel.Locator("#url").FillAsync("https://www.youtube.com/watch?v=e2e-clear");

        var field = await Panel.Locator("#url").BoundingBoxAsync();
        var clear = await Panel.Locator("#clear").BoundingBoxAsync();
        var go = await Panel.Locator("#go").BoundingBoxAsync();
        Assert.NotNull(field);
        Assert.NotNull(clear);
        Assert.NotNull(go);

        Assert.True(clear.X >= field.X && clear.X + clear.Width <= field.X + field.Width + 0.5,
            $"✕ が欄の外にある（欄 {field.X}〜{field.X + field.Width}、✕ {clear.X}〜{clear.X + clear.Width}）");
        Assert.True(field.X + field.Width - (clear.X + clear.Width) < 2, "✕ が欄の右端に寄っていない");
        Assert.True(clear.X + clear.Width <= go.X, "✕ が「解決」に重なっている");
        Assert.True(clear.Height >= field.Height - 1, $"✕ が欄より低い（欄 {field.Height}、✕ {clear.Height}）");
        Assert.True(clear.Width >= clear.Height - 1, $"✕ の幅が足りない（{clear.Width} × {clear.Height}）");

        // 長い URL の文字が ✕ の下に潜らない（文字の入る幅が ✕ の手前で終わる）。
        // 測るのはフレームの中で（BoundingBoxAsync は親のページの座標、getBoundingClientRect は iframe の中の座標）。
        var room = await Panel.Locator("#url").EvaluateAsync<double>(
            "u => { const s = getComputedStyle(u); const textEnds = u.getBoundingClientRect().right - parseFloat(s.paddingRight) - parseFloat(s.borderRightWidth); return el.clear.getBoundingClientRect().left - textEnds; }");
        Assert.True(room >= -0.5, $"文字が ✕ の下まで入る（✕ の左と文字の端の差 {room}）");

        await Panel.Locator("#url").FillAsync("");
    }
}
