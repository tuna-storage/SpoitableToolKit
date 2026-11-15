using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Settings;

/// <summary>
/// 差し替えは無い。項目は C# のクラスから reflection で作られ、値はモジュールが持っている
/// インスタンスそのものへ書かれる。ここが緑なら、その経路が実際に繋がっている。
/// </summary>
public sealed class SettingsTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private IPage Page => app.Page;

    /// <summary>本体はページが束ねごとに分かれているので、<c>host</c> は本体のページ全部に当たる。1 枚を指すときは <see cref="PageOf"/>。</summary>
    private ILocator Group(string module) => Page.Locator($"#viewSettings .cfg-group[data-module='{module}']");

    private ILocator Item(string module, string key) => Group(module).Locator($".cfg-item[data-key='{key}']");

    private ILocator PageOf(string page) =>
        Page.Locator($"#viewSettings .cfg-group[data-page='host:{page}'], #viewSettings .cfg-group[data-page='{page}']");

    private ILocator NavOf(string page) => app.SettingsPage(page);

    /// <summary>開いていないページは隠れているので、項目を触る前にそのページを開く。</summary>
    private Task Open(string page) => NavOf(page).ClickAsync();

    /// <summary>
    /// <c>.cfg-item</c> は畳みの高さを動かすため <c>overflow: hidden</c> を持っている。
    /// 一覧を箱の中に置くと行に切り取られるので、<c>body</c> へ出して <c>fixed</c> で置いている。
    /// 面が動かないことも見る（はみ出した一覧を <c>scrollIntoView</c> で見せると設定画面ごと巻いてしまう）。
    /// </summary>
    [Fact]
    public async Task TheDropdownOpensClearOfTheRow()
    {
        var box = Page.Locator("#viewSettings .cfg-item .spoitable-pick-box").First;
        await Assertions.Expect(box).ToBeVisibleAsync();

        // 先に画面内へ送ってから測る。Playwright は押す前に対象を見える位置へ送るので、
        // 前の試験が面を巻いていると、その動きまでこちらの落ち度として数えてしまう。
        await box.ScrollIntoViewIfNeededAsync();

        var before = await Page.EvaluateAsync<int>("() => Math.round(document.querySelector('#viewSettings').scrollTop)");

        await box.Locator(".spoitable-pick-value").ClickAsync();

        var open = await Page.EvaluateAsync<JsonElement>(
            """
            () => {
              const list = document.querySelector('.spoitable-pick-list:not([hidden])');
              const at = list.getBoundingClientRect();

              // **下の端が本当に見えているか。** 行に切られていれば、そこには別のものが居る。
              const under = document.elementFromPoint(at.left + at.width / 2, at.bottom - 4);

              return {
                onBody: list.parentElement === document.body,
                lowestIsMine: list.contains(under),
                scroll: Math.round(document.querySelector('#viewSettings').scrollTop),
              };
            }
            """);

        Assert.True(open.GetProperty("onBody").GetBoolean(), "一覧が囲いの中に置かれている");
        Assert.True(open.GetProperty("lowestIsMine").GetBoolean(), "一覧の下の端が切り取られている");
        Assert.Equal(before, open.GetProperty("scroll").GetInt32());
    }

    /// <summary>時間では測らない。<c>behavior: 'smooth'</c> はその場では scrollTop を変えず、即時なら変える。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheTableOfContentsGlidesUnlessMotionIsReduced(bool reduce)
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = reduce ? ReducedMotion.Reduce : ReducedMotion.NoPreference });

        try
        {
            // 目次はモジュールのページだけ。心拍のページは長いので巻く余地がある。
            await Open("hrs");
            await Assertions.Expect(Page.Locator("#cfgToc button:visible").Last).ToBeVisibleAsync();

            var moved = await Page.EvaluateAsync<JsonElement>(
                """
                () => {
                  el.cfgScroll.scrollTo({ top: 0, behavior: 'instant' });
                  const last = [...el.cfgToc.querySelectorAll('button')].at(-1);
                  last.click();
                  return { at: el.cfgScroll.scrollTop, room: el.cfgScroll.scrollHeight - el.cfgScroll.clientHeight };
                }
                """);
            Assert.True(moved.GetProperty("room").GetDouble() > 50, "設定が溢れていない（目次で巻く余地が無い）");
            Assert.Equal(reduce, moved.GetProperty("at").GetDouble() > 0);
        }
        finally
        {
            await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Null });
            await Page.EvaluateAsync("() => el.cfgScroll.scrollTo({ top: 0, behavior: 'instant' })");
        }
    }

    public async Task InitializeAsync()
    {
        // 前のテストが残したモーダルは、設定画面のクリックを遮る。
        if (await Page.Locator("#modal").IsVisibleAsync()) await Page.Locator("#modalNo").ClickAsync();

        // 本体の「画面」から始める（ページは画面を離れても覚えているので、前の試験のページが残っている）。
        await app.OpenSettings();
    }

    /// <summary>
    /// アプリはこのクラスで 1 つなので、変えた設定は後続のテストにも効く。
    /// 戻すのは各テストの最後ではなくここでやる（テストの途中で落ちても戻る）。
    /// </summary>
    public async Task DisposeAsync()
    {
        // 差し込んだタグの欄を外す（次に読み直したときから出ない）。
        await Page.EvaluateAsync("() => { window.__e2eTagsOn = false; }");

        // 触りかけの変更も落とす。残すと次のテストの「適用」に混ざる。
        if (await Page.Locator("#cfgApply").IsVisibleAsync()) await Page.Locator("#cfgCancel").ClickAsync();

        // 探しかけも消す（探している間はページをまたいで出すので、次の試験の「開いていないページは隠れる」が崩れる）。
        await Page.EvaluateAsync("() => { el.cfgSearch.value = ''; applyCfgSearch(); }");

        // 心拍計は空ではなく居ないアドレスへ戻す（空だと近くのベルトを掴みに行く）。
        await Page.EvaluateAsync(
            """
            device => bridge.applyConfigChanges(JSON.stringify({
              videourl: { AskOnFailure: true, 'Server.Enabled': false },
              host: { ConfirmSeconds: 15, Language: 'Ja', HiddenWristNotes: ['VideoPlaybackStarted', 'WRN'] },
              hrs: { Device: device },
            }))
            """, AppFixture.NoHeartRateMonitor);

        if (await Page.Locator("#modal").IsVisibleAsync()) await Page.Locator("#modalNo").ClickAsync();

        // 畳みはホストの設定に残るので、消さないと次のテストへ持ち越す。
        // 画面の中の控えも一緒に戻す（ホストへ投げるだけでは、開いている画面は畳まれたまま）。
        await Page.EvaluateAsync(
            "() => { applyCollapsedCategories([]); refreshCollapsed(); "
            + "return bridge.setCollapsedCategories(JSON.stringify([])); }");

        // 保存のたびに右下へトーストが出る。残すと右下の操作を覆うので、ここで畳む。
        await Page.EvaluateAsync("() => document.querySelectorAll('#toasts .toast .x').forEach(b => b.click())");
    }

    /// <summary>入力のたびに伝えると、関係する設定を 2 つ変えただけで作り直しが 2 回走るので、「適用」を押すまでモジュールには伝えない。</summary>
    [Fact]
    public async Task NothingIsCommittedUntilApplyIsPressed()
    {
        await Open("notify");
        var seconds = Item("host", "ConfirmSeconds").Locator("input[type=number]");
        await seconds.FillAsync("30");

        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeVisibleAsync();

        // 取り消すと元に戻り、C# 側は触られていない。
        await Page.Locator("#cfgCancel").ClickAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).Not.ToBeVisibleAsync();
        await Assertions.Expect(seconds).ToHaveValueAsync("15");
    }

    [Fact]
    public async Task PuttingTheOriginalValueBackClearsTheChange()
    {
        await Open("notify");
        var seconds = Item("host", "ConfirmSeconds").Locator("input[type=number]");

        await seconds.FillAsync("30");
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeVisibleAsync();

        await seconds.FillAsync("15");
        await Assertions.Expect(Page.Locator("#cfgApply")).Not.ToBeVisibleAsync();
    }

    /// <summary>
    /// 範囲は画面の入力制限だけに頼らない（<c>&lt;input type=number&gt;</c> は範囲外でも打てる）。
    /// 丸めると打った値ではないものが保存されることになるので、ホストが弾き、値は元のままにする。
    /// </summary>
    [Fact]
    public async Task TheHostRejectsAnOutOfRangeValue()
    {
        await Open("notify");
        var seconds = Item("host", "ConfirmSeconds").Locator("input[type=number]");

        await seconds.FillAsync("600");
        await Page.Locator("#cfgCommit").ClickAsync();

        // 黙って戻さない。理由はその項目の下に出る
        // （まとめて 1 行だと、項目が増えたときにどれのことか分からない）。
        var note = Item("host", "ConfirmSeconds").Locator("xpath=following-sibling::*[1]");
        await Assertions.Expect(note).ToHaveClassAsync("cfg-note bad");
        await Assertions.Expect(note).ToContainTextAsync("範囲で指定してください");
        // dirty（未確定）も付いたまま。通らなかったが、直して押し直せる状態。
        await Assertions.Expect(Item("host", "ConfirmSeconds")).ToHaveClassAsync("cfg-item dirty bad");

        // 打った値は画面に残す。直してもう一度押せるようにするため（適用のバーも出たまま）。
        await Assertions.Expect(seconds).ToHaveValueAsync("600");
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeVisibleAsync();

        // ホスト側は元の値のまま。取り消して取り直すと 15 が返る（丸めた 30 ではない）。
        await Page.Locator("#cfgCancel").ClickAsync();
        await Assertions.Expect(seconds).ToHaveValueAsync("15");
    }

    [Fact]
    public async Task EachHostCategoryAndEachModuleHasItsOwnPage()
    {
        // 「画面」のページから。名前はページの見出しと同じ。
        await Assertions.Expect(NavOf("display")).ToHaveAttributeAsync("aria-current", "page");
        await Assertions.Expect(PageOf("display").Locator(".cfg-head h2")).ToHaveTextAsync("画面");
        await Assertions.Expect(Item("host", "Language")).ToBeVisibleAsync();
        await Assertions.Expect(PageOf("notify")).ToBeHiddenAsync();

        // 本体のカテゴリはその束ねの項目だけ。型に応じたコントロールが選ばれ、値と範囲が入っている。
        await Open("notify");
        await Assertions.Expect(NavOf("notify")).ToHaveAttributeAsync("aria-current", "page");
        await Assertions.Expect(NavOf("display")).Not.ToHaveAttributeAsync("aria-current", "page");
        await Assertions.Expect(Item("host", "ConfirmSeconds")).ToContainTextAsync("確認を表示する秒数");
        await Assertions.Expect(Item("host", "Language")).ToBeHiddenAsync();
        await Assertions.Expect(Item("host", "StoreAllLines")).ToBeHiddenAsync();
        await Assertions.Expect(Group("videourl")).ToBeHiddenAsync();
        var seconds = Item("host", "ConfirmSeconds").Locator("input[type=number]");
        await Assertions.Expect(seconds).ToHaveValueAsync("15");
        await Assertions.Expect(seconds).ToHaveAttributeAsync("min", "5");
        await Assertions.Expect(seconds).ToHaveAttributeAsync("max", "30");

        // モジュールのページ。属性で付けた説明も出る。本体のページは隠れる。
        await Open("videourl");
        await Assertions.Expect(NavOf("videourl")).ToHaveAttributeAsync("aria-current", "page");
        await Assertions.Expect(PageOf("videourl").Locator(".cfg-head h2")).ToHaveTextAsync("動画 URL");
        await Assertions.Expect(Item("videourl", "AskOnFailure")).ToContainTextAsync("再生に失敗したときに確認する");
        await Assertions.Expect(Item("videourl", "AskOnFailure").Locator("input[type=checkbox]")).ToBeCheckedAsync();
        await Assertions.Expect(Item("host", "ConfirmSeconds")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task SearchingFindsSettingsOnOtherPages()
    {
        await Page.Locator("#cfgSearch").FillAsync("再生に失敗したときに確認する");

        await Assertions.Expect(Item("videourl", "AskOnFailure")).ToBeVisibleAsync();
        await Assertions.Expect(PageOf("display")).ToBeHiddenAsync();
        await Assertions.Expect(NavOf("videourl").Locator(".hits")).ToHaveTextAsync("1");
        await Assertions.Expect(NavOf("display")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("(^| )nohit( |$)"));

        // 探している間は、どのページも「開いている」ではない（全部のページから出している）。
        await Assertions.Expect(NavOf("display")).Not.ToHaveAttributeAsync("aria-current", "page");
        await Assertions.Expect(Page.Locator("#cfgNav .cfg-nav-item[aria-current]")).ToHaveCountAsync(0);

        // 一覧を押す。探していた字は消え、そのページだけに戻る。
        await Open("display");
        await Assertions.Expect(Page.Locator("#cfgSearch")).ToHaveValueAsync("");
        await Assertions.Expect(PageOf("display")).ToBeVisibleAsync();
        await Assertions.Expect(Group("videourl")).ToBeHiddenAsync();
        await Assertions.Expect(NavOf("videourl").Locator(".hits")).ToHaveTextAsync("");
        await Assertions.Expect(NavOf("display")).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("(^| )nohit( |$)"));
        await Assertions.Expect(NavOf("display")).ToHaveAttributeAsync("aria-current", "page");
    }

    /// <summary>印はページの単位。本体はカテゴリごとに付く（本体の 1 か所を変えても、本体の全部のページに付かない）。</summary>
    [Fact]
    public async Task APageWithChangesIsMarkedInTheList()
    {
        var dirty = new System.Text.RegularExpressions.Regex("(^| )dirty( |$)");

        await Open("notify");
        await Item("host", "ConfirmSeconds").Locator("input[type=number]").FillAsync("20");
        await Assertions.Expect(NavOf("notify")).ToHaveClassAsync(dirty);
        await Assertions.Expect(NavOf("display")).Not.ToHaveClassAsync(dirty);
        await Assertions.Expect(NavOf("videourl")).Not.ToHaveClassAsync(dirty);

        await Open("videourl");
        await Assertions.Expect(NavOf("notify")).ToHaveClassAsync(dirty);
        await Assertions.Expect(NavOf("notify").Locator(".mark")).ToBeVisibleAsync();
        await Assertions.Expect(NavOf("display").Locator(".mark")).ToBeHiddenAsync();

        await Page.Locator("#cfgCancel").ClickAsync();
        await Assertions.Expect(NavOf("notify")).Not.ToHaveClassAsync(dirty);
        await Assertions.Expect(NavOf("notify").Locator(".mark")).ToBeHiddenAsync();

        // 取り消しても開いていたページのまま。
        await Assertions.Expect(NavOf("videourl")).ToHaveAttributeAsync("aria-current", "page");
    }

    /// <summary>移らないと、適用を押しても帯が残るだけで、何が悪いのかが見えない。</summary>
    [Fact]
    public async Task ARejectionOnAnotherPageOpensThatPage()
    {
        var bad = new System.Text.RegularExpressions.Regex("(^| )bad( |$)");

        await Open("hrs");
        await Item("hrs", "Device").Locator("input[type=text]").FillAsync("Polar [ZZ:ZZ]");

        await Open("display");
        await Page.Locator("#cfgCommit").ClickAsync();

        await Assertions.Expect(Group("hrs").Locator(".cfg-note.bad")).ToBeVisibleAsync();
        await Assertions.Expect(NavOf("hrs")).ToHaveAttributeAsync("aria-current", "page");
        await Assertions.Expect(NavOf("hrs")).ToHaveClassAsync(bad);
        await Assertions.Expect(NavOf("display")).Not.ToHaveClassAsync(bad);

        await Page.Locator("#cfgCancel").ClickAsync();
        await Assertions.Expect(NavOf("hrs")).Not.ToHaveClassAsync(bad);
    }

    /// <summary>本体のページの中で弾かれたら、印はそのカテゴリにだけ付き、そのページへ移る。</summary>
    [Fact]
    public async Task AHostRejectionMarksOnlyItsCategory()
    {
        var bad = new System.Text.RegularExpressions.Regex("(^| )bad( |$)");

        await Open("notify");
        await Item("host", "ConfirmSeconds").Locator("input[type=number]").FillAsync("600");
        await Open("hrs");
        await Page.Locator("#cfgCommit").ClickAsync();

        await Assertions.Expect(NavOf("notify")).ToHaveClassAsync(bad);
        await Assertions.Expect(NavOf("notify")).ToHaveAttributeAsync("aria-current", "page");
        await Assertions.Expect(NavOf("display")).Not.ToHaveClassAsync(bad);
        await Assertions.Expect(NavOf("log")).Not.ToHaveClassAsync(bad);
        await Assertions.Expect(Item("host", "ConfirmSeconds")).ToBeVisibleAsync();

        await Page.Locator("#cfgCancel").ClickAsync();
        await Assertions.Expect(NavOf("notify")).Not.ToHaveClassAsync(bad);
    }

    /// <summary>
    /// 見るのはシェルと設定画面、そしてモジュールが自分で同梱した辞書。
    /// 同じ 1 つの辞書から引いていることを通しで確かめる（片方だけ切り替わる作りだと、言語の混ざった画面になる）。
    /// </summary>
    [Fact]
    public async Task ChangingTheLanguageChangesTheWording()
    {
        await Open("notify");
        await Assertions.Expect(Page.Locator("#cfgNav .heading").First).ToHaveTextAsync("本体");

        await Page.EvaluateAsync(
            """
            () => bridge.applyConfigChanges(JSON.stringify({ host: { Language: 'En' } }))
            """);

        // シェル（サイドバー）と、設定画面の一覧・見出し・ラベル。言語を変えても開いていたページのまま。
        await Assertions.Expect(Page.Locator("#sidebar .item[data-id='settings']")).ToContainTextAsync("Settings");
        await Assertions.Expect(Page.Locator("#cfgNav .heading").First).ToHaveTextAsync("App");
        await Assertions.Expect(NavOf("notify")).ToHaveTextAsync("Notifications");
        await Assertions.Expect(NavOf("notify")).ToHaveAttributeAsync("aria-current", "page");
        await Assertions.Expect(PageOf("notify").Locator(".cfg-head h2")).ToHaveTextAsync("Notifications");
        await Assertions.Expect(Item("host", "ConfirmSeconds"))
            .ToContainTextAsync("Seconds to show a prompt");

        // モジュールの分も出る（modules/videourl/locales/en.json から）。
        await Open("videourl");
        await Assertions.Expect(Item("videourl", "AskOnFailure"))
            .ToContainTextAsync("Ask when a video fails to play");

        // 戻せば日本語に戻る。DisposeAsync でも戻すが、往復まで見ておく。
        await Page.EvaluateAsync(
            """
            () => bridge.applyConfigChanges(JSON.stringify({ host: { Language: 'Ja' } }))
            """);

        await Assertions.Expect(Item("videourl", "AskOnFailure")).ToContainTextAsync("再生に失敗したときに確認する");
        await Assertions.Expect(NavOf("videourl")).ToHaveAttributeAsync("aria-current", "page");
        await Open("notify");
        await Assertions.Expect(Item("host", "ConfirmSeconds")).ToContainTextAsync("確認を表示する秒数");
    }

    /// <summary>利用者が触るものは画面に出し、ホストが自分で書くものは出さない。</summary>
    [Fact]
    public async Task TheHostsOwnSettingsAppearOnTheScreen()
    {
        await Open("log");
        await Assertions.Expect(Item("host", "StoreAllLines")).ToContainTextAsync("生ログも保存する");
        await Assertions.Expect(Item("host", "StoreApiRequests")).ToContainTextAsync("API の呼び出しも保存する");

        // 隠した項目は出ない。ログ画面のチップと外部ツールの確認時刻は利用者が触るものではない。
        await Assertions.Expect(Item("host", "HiddenKinds")).ToHaveCountAsync(0);
        await Assertions.Expect(Item("host", "ToolVerifiedAt")).ToHaveCountAsync(0);

        // 「読み込まないモジュール」はモジュールの管理画面が書くので、設定画面には出ない。
        await Assertions.Expect(Item("host", "DisabledModules")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AStringListIsEditedAsTags()
    {
        var item = await TagListAsync();

        await Assertions.Expect(item.Locator(".cfg-tags input")).ToBeVisibleAsync();
        await Assertions.Expect(item.Locator(".cfg-tags .tag")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// 文字列の一覧（タグ）の欄を、本体の設定に 1 つ差し込む。部品そのもの（候補・行に切られない・下端では上へ開く・入っているものは出さない）をこの欄で見る。
    /// 候補は動いているモジュールの Id。値は C# の知らない名前なので適用はしない（保存の道は本物の一覧の欄で見ている）。
    /// 差し込むのはこの試験の間だけ（<see cref="DisposeAsync"/> で外す。外さないと、同じアプリの後の試験の数がずれる）。
    /// 置くのは「VR / 手首」のページの最後（本体でいちばん長いページなので、下端に寄せて開ける）。
    /// </summary>
    private async Task<ILocator> TagListAsync()
    {
        await Page.EvaluateAsync(
            """
            async () => {
              if (!window.__e2eTagsWrapped) {
                window.__e2eTagsWrapped = true;
                const original = bridge.getConfigs;
                bridge.getConfigs = async (...args) => {
                  const groups = await original(...args);
                  if (!window.__e2eTagsOn) return groups;
                  const host = groups.find(g => g.module === 'host');
                  const like = host.items.find(i => i.key === 'VrWindowSpot');
                  host.items.push({ ...like, key: 'E2ETags', name: 'E2ETags', label: 'E2E の文字列の一覧', description: '',
                    kind: 'list', itemKind: 'string', choices: null, suggestions: ['hrs', 'videourl'], secret: false,
                    restart: false, switches: false, editor: null, min: null, max: null, value: '[]' });
                  return groups;
                };
              }
              window.__e2eTagsOn = true;
              await loadSettings();
            }
            """);
        await Open("vr");
        var item = Item("host", "E2ETags");
        await Assertions.Expect(item).ToHaveCountAsync(1);
        return item;
    }

    /// <summary>一覧は「切ってあるもの」として保存される。片道だけだと「常に足す」実装でも緑になるので、戻すところまで見る。</summary>
    [Fact]
    public async Task ASwitchListSavesWhatIsTurnedOff()
    {
        var left = Page.Locator("#swList .cfg-switch[data-name='PlayerLeft']");

        // 落ちてもモーダルを開いたまま残さない（このクラスの試験は同じアプリで続けて走るので、残すと後の試験のクリックを塞ぐ）。
        try
        {
            await Open("vr");
            await Item("host", "HiddenWristNotes").Locator(".cfg-switch-field button").ClickAsync();
            await Assertions.Expect(left).ToBeVisibleAsync();
            await Assertions.Expect(left.Locator("input.spoitable-switch")).ToBeCheckedAsync();

            // 既定で切ってあるものは、切った形で出る。
            await Assertions.Expect(Page.Locator("#swList .cfg-switch[data-name='VideoPlaybackStarted'] input.spoitable-switch")).Not.ToBeCheckedAsync();

            await left.Locator("input.spoitable-switch").ClickAsync();
            await Page.Locator("#swDone").ClickAsync();
            await Page.Locator("#cfgCommit").ClickAsync();
            await ExpectHost(node => node?["HiddenWristNotes"]?.AsArray().Any(v => (string?)v == "PlayerLeft") == true);

            await Item("host", "HiddenWristNotes").Locator(".cfg-switch-field button").ClickAsync();
            await left.Locator("input.spoitable-switch").ClickAsync();
            await Page.Locator("#swDone").ClickAsync();
            await Page.Locator("#cfgCommit").ClickAsync();
            await ExpectHost(node => node?["HiddenWristNotes"]?.AsArray().All(v => (string?)v != "PlayerLeft") == true);
        }
        finally
        {
            await Page.EvaluateAsync("() => { if (!el.switchModal.hidden) closeSwitchModal(); }");
        }
    }

    /// <summary>保存は非同期なので、本体の設定（<c>config.json</c>）が期待どおりになるまで待つ。</summary>
    private async Task ExpectHost(Func<JsonNode?, bool> expected)
    {
        var file = Path.Combine(app.DataDirectory, "config.json");
        var waited = System.Diagnostics.Stopwatch.StartNew();

        while (waited.Elapsed < TimeSpan.FromSeconds(15))
        {
            try
            {
                if (expected(JsonNode.Parse(File.ReadAllText(file)))) return;
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
                // 書き込みの最中。
            }

            await Task.Delay(100);
        }

        Assert.Fail("config.json が 15 秒待っても期待どおりにならなかった: " + File.ReadAllText(file));
    }

    [Fact]
    public async Task ASuggestionCanBePickedAndAddedToTheList()
    {
        var tags = (await TagListAsync()).Locator(".cfg-tags");
        await tags.Locator("input").FillAsync("hr");

        var option = tags.Locator(".cfg-suggest button", new() { HasTextString = "hrs" });
        await Assertions.Expect(option).ToBeVisibleAsync();

        // blur が click より先に来るので、シェルは mousedown で拾っている。
        await option.ClickAsync();
        await Assertions.Expect(tags.Locator(".tag")).ToHaveTextAsync(["hrs×"]);
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeVisibleAsync();

        // 外すところまで見る。片道だけだと「常に足す」実装でも緑になる。
        await tags.Locator(".tag button").ClickAsync();
        await Assertions.Expect(tags.Locator(".tag")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeHiddenAsync();
    }

    /// <summary>
    /// 行は畳むときのために <c>overflow: hidden</c> を持っていて、行の外へ出る候補を切る。
    /// 切られていても要素は在るので、DOM にあるかどうかでは足りない。その場所に本当に候補があるかを、画面の側から聞く。
    /// </summary>
    [Fact]
    public async Task TheSuggestionsAreNotClippedByTheirRow()
    {
        var tags = (await TagListAsync()).Locator(".cfg-tags");

        await tags.Locator("input").FillAsync("video");

        var choice = tags.Locator(".cfg-suggest button", new() { HasTextString = "videourl" });
        await Assertions.Expect(choice).ToHaveCountAsync(1);

        // 候補の真ん中にあるものを画面に聞く。切られていれば下の行が返る。
        var onTop = await choice.EvaluateAsync<bool>(
            "button => { const box = button.getBoundingClientRect(); "
            + "const hit = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2); "
            + "return button === hit || button.contains(hit); }");

        Assert.True(onTop, "候補が行に切られている");
    }

    /// <summary>
    /// 下へ開くと下の帯の裏に潜るので、画面の下端の行では候補を上へ開く。
    /// 上の試験は Playwright の自動スクロールに任せていて行の位置が設定の行数次第なので、ここでは下端に寄せてから開く。
    /// </summary>
    [Fact]
    public async Task AtTheBottomEdgeTheSuggestionsOpenUpwards()
    {
        var row = await TagListAsync();
        var tags = row.Locator(".cfg-tags");

        await row.EvaluateAsync("row => row.scrollIntoView({ block: 'end' })");
        await tags.Locator("input").FillAsync("video");

        var choice = tags.Locator(".cfg-suggest button", new() { HasTextString = "videourl" });
        await Assertions.Expect(choice).ToHaveCountAsync(1);

        var onTop = await choice.EvaluateAsync<bool>(
            "button => { const box = button.getBoundingClientRect(); "
            + "const hit = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2); "
            + "return button === hit || button.contains(hit); }");

        Assert.True(onTop, "候補が下の帯の裏に潜っている");
        await Assertions.Expect(tags.Locator(".cfg-suggest")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bup\b"));
    }

    [Fact]
    public async Task WhatIsAlreadyInTheListIsNotSuggested()
    {
        var tags = (await TagListAsync()).Locator(".cfg-tags");

        await tags.Locator("input").FillAsync("videourl");
        await tags.Locator("input").PressAsync("Enter");
        await Assertions.Expect(tags.Locator(".tag")).ToHaveTextAsync(["videourl×"]);

        // 打ち込んだ分は候補から消える。候補に無いものも Enter で入る（=選択肢ではない）。
        await tags.Locator("input").FillAsync("videourl");
        await Assertions.Expect(tags.Locator(".cfg-suggest button", new() { HasTextString = "videourl" }))
            .ToHaveCountAsync(0);
    }

    /// <summary>
    /// hrs は別 ALC に居るので、ここが緑なら「画面 → ホスト → 別 ALC のモジュール → 却下の理由 → 画面」が全部繋がっている。
    /// 実機の BLE は要らない。読めないアドレスかどうかは手元で分かることなので、繋ぎに行く前に答えられる
    /// （<c>IConfigEventListener&lt;TConfig&gt;.OnConfigChanging</c> が「待たずに答える」と約束している形）。
    /// </summary>
    [Fact]
    public async Task WhenAModuleRejectsTheReasonAppearsOnScreen()
    {
        await Open("hrs");
        var device = Item("hrs", "Device").Locator("input[type=text]");
        await device.FillAsync("Polar [ZZ:ZZ]");

        await Page.Locator("#cfgCommit").ClickAsync();

        // モジュールはその値だけを断る（e.Reject(c => c.Device, …)）。理由はその欄の下に出て、欄が赤くなる。
        // key は欄の名前（保存名の道）で返るので、欄に当たる。
        var note = Item("hrs", "Device").Locator("xpath=following-sibling::*[1]");
        await Assertions.Expect(note).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bcfg-note\b"));
        await Assertions.Expect(note).ToContainTextAsync("アドレスとして読み込めません");
        await Assertions.Expect(Item("hrs", "Device")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bbad\b"));

        // 値は書かれていない。取り消すと元の値（e2e では居ないアドレス）に戻る。
        await Page.Locator("#cfgCancel").ClickAsync();
        await Assertions.Expect(device).ToHaveValueAsync(AppFixture.NoHeartRateMonitor);
    }

    /// <summary>
    /// 入った欄は保存済みとして描き直され（変更の印が消え、溜めの件数から外れる）、
    /// 断られた欄だけが打った値のまま溜めに残る（赤枠と理由。直してもう一度押せる）。
    /// 取り消すと、断られた欄だけ元に戻る（入った欄は保存済みのまま）。
    /// </summary>
    [Fact]
    public async Task WhenOneValueIsRefusedTheOthersAreSavedAndOnlyTheRefusedOneStaysPending()
    {
        var bad = new System.Text.RegularExpressions.Regex(@"\bbad\b");
        var dirty = new System.Text.RegularExpressions.Regex(@"\bdirty\b");

        await Open("hrs");
        var device = Item("hrs", "Device").Locator("input[type=text]");
        var reconnect = Item("hrs", "ReconnectSeconds").Locator("input[type=number]");
        await device.FillAsync("Polar [ZZ:ZZ]");
        await reconnect.FillAsync("9");
        await Assertions.Expect(Page.Locator("#cfgCount")).ToContainTextAsync("2 件");

        await Page.Locator("#cfgCommit").ClickAsync();

        await Assertions.Expect(Item("hrs", "Device")).ToHaveClassAsync(bad);
        await Assertions.Expect(Item("hrs", "Device")).ToHaveClassAsync(dirty);
        await Assertions.Expect(device).ToHaveValueAsync("Polar [ZZ:ZZ]");
        await Assertions.Expect(Item("hrs", "ReconnectSeconds")).Not.ToHaveClassAsync(dirty);
        await Assertions.Expect(Item("hrs", "ReconnectSeconds")).Not.ToHaveClassAsync(bad);
        await Assertions.Expect(reconnect).ToHaveValueAsync("9");
        await Assertions.Expect(Page.Locator("#cfgCount")).ToContainTextAsync("1 件");

        await Page.Locator("#cfgCancel").ClickAsync();

        await Assertions.Expect(device).ToHaveValueAsync(AppFixture.NoHeartRateMonitor);
        await Assertions.Expect(reconnect).ToHaveValueAsync("9");
        await Assertions.Expect(Page.Locator("#cfgApply")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task AReadableSelectorIsAccepted()
    {
        await Open("hrs");
        var device = Item("hrs", "Device").Locator("input[type=text]");
        await device.FillAsync("Polar H10 [A1:B2:C3:D4:E5:F6]");

        await Page.Locator("#cfgCommit").ClickAsync();

        await Assertions.Expect(Page.Locator("#cfgApply")).Not.ToBeVisibleAsync();
        await Assertions.Expect(Group("hrs").Locator(".cfg-note")).ToHaveCountAsync(0);
        await Assertions.Expect(device).ToHaveValueAsync("Polar H10 [A1:B2:C3:D4:E5:F6]");
    }

    /// <summary>設定インスタンスを共有しているので、モジュールは読み直すだけでよい。画面で切った値が別 ALC のモジュールの動きを実際に変えるところまで見る。</summary>
    [Fact]
    public async Task TurningASettingOffChangesWhatTheModuleDoes()
    {
        const string url = "https://www.youtube.com/watch?v=e2eSettingsOff";

        // 触っただけでは何も起きない。「適用」を押すまでモジュールには伝わらない。
        await Open("videourl");
        await Item("videourl", "AskOnFailure").Locator("input[type=checkbox]").UncheckAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#cfgCount")).ToContainTextAsync("1 件");

        await Page.Locator("#cfgCommit").ClickAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).Not.ToBeVisibleAsync();

        await Page.Locator("#sidebar .item[data-id='videourl']").ClickAsync();

        app.AppendLog(
            $"[Video Playback] Attempting to resolve URL '{url}'",
            $"[Video Playback] URL '{url}' resolved to 'https://e2e.invalid/settings'",
            "[AVProVideo] Opening https://e2e.invalid/settings (offset 0) with API MediaFoundation",
            "[AVProVideo] Error: Loading failed.  e2e");

        // 先に「届いていること」を確かめる。これが無いと、イベントごと壊れていても
        // 「モーダルが出ない」だけで緑になってしまう。
        await Assertions.Expect(Page.FrameLocator("iframe.view[data-id='videourl']").Locator($".failure[data-url='{url}']"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // 届いたうえで聞かれない。
        await Assertions.Expect(Page.Locator("#modal")).Not.ToBeVisibleAsync();
        await Task.Delay(1_500);
        await Assertions.Expect(Page.Locator("#modal")).Not.ToBeVisibleAsync();
    }

    /// <summary>.cfg-item は flex なので、隠す指定を書き忘れると hidden 属性は付いているのに画面には出たままになる。属性ではなく、実際に見えるかで見る。</summary>
    [Fact]
    public async Task ANestedSectionHidesItsContentsWhileOff()
    {
        await Open("videourl");
        await Assertions.Expect(Item("videourl", "Server")).ToBeVisibleAsync();
        await Assertions.Expect(Item("videourl", "Server.Url")).ToBeHiddenAsync();
        await Assertions.Expect(Item("videourl", "Server.TimeoutSeconds")).ToBeHiddenAsync();
    }

    /// <summary>
    /// 出す条件はモジュールが書いたふつうの式なので画面では評価できず、押すたびにホストへ「当てたつもりの姿」を聞き直している。
    /// ここが繋がっていないと、入にした直後に打ち込めない設定になる。
    /// </summary>
    [Fact]
    public async Task TurningTheHeadingOnShowsTheContentsBeforeApplying()
    {
        await Open("videourl");
        await Item("videourl", "Server").Locator("input[type=checkbox]").CheckAsync();

        await Assertions.Expect(Item("videourl", "Server.Url")).ToBeVisibleAsync();
        await Assertions.Expect(Item("videourl", "Server.TimeoutSeconds")).ToBeVisibleAsync();

        // まだ適用していない。押す前に出ることを見る。
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeVisibleAsync();
    }

    /// <summary>スイッチではなく、選ぶ欄の値を見て出し入れする。選ぶ欄にはメンバー名（VRCHR・VRCOSC）がそのまま出る。既定は VRCHR。</summary>
    [Fact]
    public async Task ChoosingAPresetSwapsTheNameFieldsBeforeApplying()
    {
        await Open("hrs");
        var preset = Item("hrs", "OscPreset").Locator("select");

        await Assertions.Expect(preset).ToHaveValueAsync("VRCHR");
        await Assertions.Expect(Item("hrs", "Parameters")).ToBeVisibleAsync();
        await Assertions.Expect(Item("hrs", "VrcoscParameters")).ToBeHiddenAsync();

        // 元の <select> は隠してあるので、被せた部品を押す（InPage と同じ）。
        await Item("hrs", "OscPreset").Locator(".spoitable-pick-value").ClickAsync();
        await Page.Locator(".spoitable-pick-list:not([hidden]) button[data-value='VRCOSC']").ClickAsync();

        await Assertions.Expect(Item("hrs", "VrcoscParameters.Value").Locator("input")).ToHaveValueAsync("VRCOSC/Heartrate/Value");
        await Assertions.Expect(Item("hrs", "Parameters")).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeVisibleAsync();
    }

    /// <summary>
    /// 欄の種別は JS が付けるので、HTML を読んでも付いているか分からない。画面の側から見るしかない。
    /// 隠しているのは画面だけで、値は config.json に生のまま入る。守っているのは「配信に映り込む」「隣から見える」までの範囲。
    /// </summary>
    [Fact]
    public async Task TheTokenFieldIsMaskedWhileTheUrlIsNot()
    {
        await Open("videourl");
        await Item("videourl", "Server").Locator("input[type=checkbox]").CheckAsync();

        var token = Item("videourl", "Server.Token").Locator("input");
        await Assertions.Expect(token).ToBeVisibleAsync();
        await Assertions.Expect(token).ToHaveAttributeAsync("type", "password");

        // 入れ物の記憶にも残さない。CEF に覚えさせると、消す手が画面に無い。
        await Assertions.Expect(token).ToHaveAttributeAsync("autocomplete", "off");

        // 伏せるのは印を付けた欄だけ。隣の URL まで伏せたら、繋ぎ先が読めなくなる。
        await Assertions.Expect(Item("videourl", "Server.Url").Locator("input"))
            .ToHaveAttributeAsync("type", "text");
    }

    /// <summary>入/切のスイッチが入れ物の中にあると、切にした瞬間にスイッチごと消えて config.json を手で書く以外に入へ戻す道が無くなる。見出しに載せてあるのはこのため。</summary>
    [Fact]
    public async Task TurningItOffAndApplyingStillLetsYouTurnItBackOn()
    {
        await Open("videourl");
        var box = Item("videourl", "Server").Locator("input[type=checkbox]");

        await box.CheckAsync();
        await Page.Locator("#cfgCommit").ClickAsync();
        await Assertions.Expect(Item("videourl", "Server.Url")).ToBeVisibleAsync();

        await box.UncheckAsync();
        await Page.Locator("#cfgCommit").ClickAsync();
        await Assertions.Expect(Item("videourl", "Server.Url")).ToBeHiddenAsync();

        // 押す相手が残っていること。
        await Assertions.Expect(Item("videourl", "Server")).ToBeVisibleAsync();
        await Assertions.Expect(box).ToBeVisibleAsync();

        await box.CheckAsync();
        await Assertions.Expect(Item("videourl", "Server.Url")).ToBeVisibleAsync();
    }

    /// <summary>平らなキーで書くと、次の起動で読み戻せない（読む側は入れ子を辿る）。</summary>
    [Fact]
    public async Task ANestedSectionIsSavedInNestedShape()
    {
        await Open("videourl");
        await Item("videourl", "Server").Locator("input[type=checkbox]").CheckAsync();
        await Item("videourl", "Server.Url").Locator("input[type=text]").FillAsync("https://e2e.test");
        await Page.Locator("#cfgCommit").ClickAsync();

        await ExpectSaved(saved =>
            saved?["Server"]?["Url"]?.GetValue<string>() == "https://e2e.test"
            && saved["Server"]!["Enabled"]!.GetValue<bool>()
            // 平らなキーで書いていないこと。書けても読み戻せない形になる。
            && saved["Server.Url"] is null);
    }

    /// <summary>保存はボタンの先で非同期に走るので待つ。モジュールの設定はモジュールごとのファイル（<c>module-data/&lt;Id&gt;/config.json</c>）。</summary>
    private async Task ExpectSaved(Func<JsonNode?, bool> expected)
    {
        var file = Path.Combine(app.DataDirectory, "module-data", "videourl", "config.json");
        var waited = System.Diagnostics.Stopwatch.StartNew();

        while (waited.Elapsed < TimeSpan.FromSeconds(15))
        {
            try
            {
                if (expected(JsonNode.Parse(File.ReadAllText(file)))) return;
            }
            catch (Exception e) when (e is IOException or JsonException or NullReferenceException)
            {
                // まだ書かれていない / 書き込みの最中。待ち足りないという結論は同じ。
            }

            await Task.Delay(100);
        }

        Assert.Fail($"config.json が {waited.Elapsed.TotalSeconds:F1} 秒待っても期待どおりにならなかった");
    }

    /// <summary>畳みは値ではないので「適用」も要らず、押した時点で効く。見出しまで消えると開き直す手が無くなるので、入/切のときと同じく見出しは残す。</summary>
    [Fact]
    public async Task FoldingHidesTheContentsAndKeepsTheHeading()
    {
        await Open("hrs");
        var fold = Item("hrs", "Parameters").Locator(".fold");

        await Assertions.Expect(Item("hrs", "Parameters.Ones")).ToBeVisibleAsync();

        await fold.ClickAsync();

        // 見え方で見る。hidden 属性だけを見ると、CSS が打ち消していても素通りする
        // （.cfg-item の display: flex が [hidden] に勝つ）。
        await Assertions.Expect(Item("hrs", "Parameters.Ones")).ToBeHiddenAsync();
        await Assertions.Expect(Item("hrs", "Parameters.HeartRate")).ToBeHiddenAsync();
        await Assertions.Expect(Item("hrs", "Parameters")).ToBeVisibleAsync();

        // 押しても「適用」は出ない。畳みは値ではないので適用に混ざらない。
        await Assertions.Expect(Page.Locator("#cfgApply")).ToBeHiddenAsync();

        await fold.ClickAsync();
        await Assertions.Expect(Item("hrs", "Parameters.Ones")).ToBeVisibleAsync();
    }

    /// <summary>
    /// 画面の中の控えだけだと、次の起動で開き直ってしまう。
    /// 画面の中の Set が残っているだけでも「開き直しても畳まれたまま」は通ってしまうので、ファイルに出ていることまで見る。
    /// </summary>
    [Fact]
    public async Task AFoldSurvivesReopeningTheScreen()
    {
        await Open("hrs");
        await Item("hrs", "Parameters").Locator(".fold").ClickAsync();

        await ExpectCollapsed("hrs:Parameters");

        // 画面を出入りする。設定画面は開くたびに作り直されるが、ページは覚えている（心拍のまま）。
        await Page.Locator("#sidebar .item[data-id='log']").ClickAsync();
        await Page.Locator("#sidebar .item[data-id='settings']").ClickAsync();

        await Assertions.Expect(NavOf("hrs")).ToHaveAttributeAsync("aria-current", "page");
        await Assertions.Expect(Item("hrs", "Parameters.Ones")).ToBeHiddenAsync();
        await Assertions.Expect(Item("hrs", "Parameters")).ToBeVisibleAsync();
    }

    /// <summary>入/切のカテゴリに畳みを出すと、切っているのか畳んでいるのかが見た目で区別できなくなる。</summary>
    [Fact]
    public async Task OnlyCategoriesThatAskedForItCanBeFolded()
    {
        await Assertions.Expect(Item("hrs", "Parameters").Locator(".fold")).ToHaveCountAsync(1);
        await Assertions.Expect(Item("hrs", "Zones").Locator(".fold")).ToHaveCountAsync(1);

        await Assertions.Expect(Item("hrs", "Hds").Locator(".fold")).ToHaveCountAsync(0);
        await Assertions.Expect(Item("hrs", "Broadcast").Locator(".fold")).ToHaveCountAsync(0);
        await Assertions.Expect(Item("videourl", "Server").Locator(".fold")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task HdsHidesItsContentsWhileOff()
    {
        await Open("hrs");
        var box = Item("hrs", "Hds").Locator("input[type=checkbox]");

        await Assertions.Expect(Item("hrs", "Hds.Port")).ToBeHiddenAsync();

        await box.CheckAsync();
        await Assertions.Expect(Item("hrs", "Hds.Port")).ToBeVisibleAsync();
        await Assertions.Expect(Item("hrs", "Hds.NoSignalSeconds")).ToBeVisibleAsync();

        await box.UncheckAsync();
        await Assertions.Expect(Item("hrs", "Hds.Port")).ToBeHiddenAsync();
        await Assertions.Expect(Item("hrs", "Hds")).ToBeVisibleAsync();
    }

    /// <summary>畳みはボタンの先で保存されるので、ファイルに出るまで待つ。</summary>
    private async Task ExpectCollapsed(string category)
    {
        var file = Path.Combine(app.DataDirectory, "config.json");

        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (Read(file) is { } saved && saved.Contains(category)) return;
            await Task.Delay(100);
        }

        Assert.Fail($"CollapsedCategories に {category} が出なかった: "
            + (Read(file) is { } last ? string.Join(", ", last) : "(読めない)"));

        static string[]? Read(string file)
        {
            try
            {
                return JsonNode.Parse(File.ReadAllText(file))?["CollapsedCategories"].Deserialize<string[]>();
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// シェルは読み込みのときに getStatus から畳みを拾う。載せ忘れてもその場では何も壊れず、
    /// 次の起動で畳みが全部開いている、という形でだけ出るので、ここで見る。
    /// </summary>
    [Fact]
    public async Task TheFoldsRideAlongWithTheStartupState()
    {
        await Open("hrs");
        await Item("hrs", "Parameters").Locator(".fold").ClickAsync();
        await ExpectCollapsed("hrs:Parameters");

        var carried = await Page.EvaluateAsync<string[]>(
            "async () => (await bridge.getStatus()).collapsedCategories");

        Assert.Contains("hrs:Parameters", carried);
    }

    /// <summary>
    /// 入/切を押すたびに、画面はホストへ「当てたつもりの姿」を聞き直して行の出し入れを合わせる。
    /// その戻りには畳みのことは入っていない（畳みは画面の持ち物）ので、掛け合わせを忘れると関係ない設定を触った拍子に畳みが解ける。
    /// </summary>
    [Fact]
    public async Task TouchingSomethingElseDoesNotUnfoldIt()
    {
        await Open("hrs");
        await Item("hrs", "Parameters").Locator(".fold").ClickAsync();
        await Assertions.Expect(Item("hrs", "Parameters.Ones")).ToBeHiddenAsync();

        // 聞き直しが走る操作。別のモジュールの設定を触っても同じこと。
        await Open("videourl");
        await Item("videourl", "Server").Locator("input[type=checkbox]").CheckAsync();
        await Assertions.Expect(Item("videourl", "Server.Url")).ToBeVisibleAsync();

        // 畳んだままであること。心拍のページへ戻ってから見る（隠れたページの中では「見えない」が素通りする）。
        await Open("hrs");
        await Assertions.Expect(Item("hrs", "Parameters")).ToBeVisibleAsync();
        await Assertions.Expect(Item("hrs", "Parameters.Ones")).ToBeHiddenAsync();
        await Assertions.Expect(Item("hrs", "Parameters.HeartRate")).ToBeHiddenAsync();
    }

    /// <summary>矢印は左端、入/切のチェックは右端にあるので、行ぜんぶで受ける。矢印は自前の listener を持たず行が拾うので、二重に切り替わらないことも見る。</summary>
    [Fact]
    public async Task AnywhereOnTheHeadingFoldsIt()
    {
        await Open("hrs");
        var heading = Item("hrs", "Parameters");

        // 矢印ではなく名前のところを押す。
        await heading.Locator(".name").ClickAsync();
        await Assertions.Expect(Item("hrs", "Parameters.Ones")).ToBeHiddenAsync();

        // 補足のところでも同じ。
        await heading.Locator(".desc").ClickAsync();
        await Assertions.Expect(Item("hrs", "Parameters.Ones")).ToBeVisibleAsync();

        // 矢印を押しても 1 回だけ切り替わる（行にも listener があるので二重になりうる）。
        await heading.Locator(".fold").ClickAsync();
        await Assertions.Expect(Item("hrs", "Parameters.Ones")).ToBeHiddenAsync();
    }
}
