using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Shell;

/// <summary>ui.css はシェルもモジュールのページも同じ 1 枚を読み、spoitable- で始まる class にだけ当たる。</summary>
public sealed class HostUiTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    private IPage Page => app.Page;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (await Page.Locator("#filterModal").IsVisibleAsync()) await Page.Locator("#filterClose").ClickAsync();
        await Page.EvaluateAsync("""
            () => {
              if (!el.switchModal.hidden) closeSwitchModal();
              if (!el.shortcutModal.hidden) closeShortcutModal();
              if (!el.maModal.hidden) closeAddModule();
            }
            """);
        if (await Page.Locator("#cfgCancel").IsVisibleAsync()) await Page.Locator("#cfgCancel").ClickAsync();
        await Page.Locator("#sidebar .item[data-id='log']").ClickAsync();
    }

    /// <summary>疑似要素は matches が見ないので、疑似要素を取った形で当たりを見る。ui.css を読んでいなければ null。</summary>
    private const string UiRules = """
        (element) => {
          const sheet = [...document.styleSheets].find(s => s.href?.endsWith('/_host/ui.css'));
          const flat = rules => [...rules].flatMap(r => r.cssRules && !r.selectorText ? flat(r.cssRules) : [r]);
          const rules = sheet ? flat(sheet.cssRules).filter(r => r.selectorText) : [];
          const hits = rules
            .flatMap(r => r.selectorText.split(','))
            .map(s => s.trim())
            .filter(s => { try { return element.matches(s.replace(/::?(before|after)/g, '')); } catch { return false; } });
          return sheet ? hits : null;
        }
        """;

    private const string Token = """
        (name) => {
          const probe = document.createElement('span');
          probe.style.color = `var(${name})`;
          document.body.append(probe);
          const value = getComputedStyle(probe).color;
          probe.remove();
          return value;
        }
        """;

    /// <summary>flex の箱・正方形（2.15em）は ui.css だけが決める（読まなければ inline-block で幅は中身なり）。</summary>
    [Fact]
    public async Task TheShellDrawsItsPartsFromUiCss()
    {
        await Page.Locator("#sidebar .item[data-id='log']").ClickAsync();
        var reload = Page.Locator("#logReload");
        await Assertions.Expect(reload).ToBeVisibleAsync();

        var hits = await reload.EvaluateAsync<string[]?>(UiRules);
        Assert.True(hits is not null, "シェルが /_host/ui.css を読んでいない");
        Assert.Contains(".spoitable-btn.spoitable-btn-icon", hits);

        Assert.Equal("flex center", await reload.EvaluateAsync<string>("e => getComputedStyle(e).display + ' ' + getComputedStyle(e).justifyContent"));
        var box = await reload.BoundingBoxAsync() ?? throw new InvalidOperationException("再読み込みのボタンが見えない");
        var em = await reload.EvaluateAsync<double>("e => parseFloat(getComputedStyle(e).fontSize)");
        Assert.Equal(2.15 * em, box.Height, 1);
        Assert.Equal(box.Height, box.Width, 1);
    }

    /// <summary>タブの高さ 2.4em と下線（::after）は ui.css だけが決める（読まなければパネルの button の 2.15em で下線が無い）。</summary>
    [Fact]
    public async Task TheHrsPanelDrawsItsTabsFromUiCss()
    {
        await Page.Locator("#sidebar .item[data-id='hrs']").ClickAsync();
        var hrs = Page.FrameLocator("iframe.view[data-id='hrs']");
        var tab = hrs.Locator("#tabNow");
        await Assertions.Expect(tab).Not.ToHaveTextAsync("", new() { Timeout = 20_000 });

        var hits = await tab.EvaluateAsync<string[]?>(UiRules);
        Assert.True(hits is not null, "hrs のパネルが /_host/ui.css を読んでいない");
        Assert.Contains(".spoitable-tab.spoitable-tab-on", hits);

        // パネルは素の button を .88em にしているが、部品の土台（font: inherit）がタブには掛けさせない。
        var em = await tab.EvaluateAsync<double>("e => parseFloat(getComputedStyle(e).fontSize)");
        Assert.Equal(await hrs.Locator("body").EvaluateAsync<double>("b => parseFloat(getComputedStyle(b).fontSize)"), em, 1);
        var height = await tab.EvaluateAsync<double>("e => parseFloat(getComputedStyle(e).height)");
        Assert.Equal(2.4 * em, height, 1);

        var accent = await hrs.Locator("body").EvaluateAsync<string>($"(_, name) => ({Token})(name)", "--accent");
        Assert.Equal(accent, await tab.EvaluateAsync<string>("e => getComputedStyle(e, '::after').backgroundColor"));
    }

    /// <summary>当たってよいのは、部品か部品の中の要素だけ（.spoitable-card h2 の h2 など）。</summary>
    [Fact]
    public async Task UiCssTouchesNothingOutsideItsPartsOnAModulePage()
    {
        await Page.Locator("#sidebar .item[data-id='hrs']").ClickAsync();
        var hrs = Page.FrameLocator("iframe.view[data-id='hrs']");
        await Assertions.Expect(hrs.Locator("#tabStats")).Not.ToHaveTextAsync("", new() { Timeout = 20_000 });

        var result = await hrs.Locator("body").EvaluateAsync<string[]>("""
            body => {
              const sheet = [...document.styleSheets].find(s => s.href?.endsWith('/_host/ui.css'));
              if (!sheet) return ['ui.css が無い'];
              const flat = rules => [...rules].flatMap(r => r.cssRules && !r.selectorText ? flat(r.cssRules) : [r]);
              const selectors = flat(sheet.cssRules).filter(r => r.selectorText)
                .flatMap(r => r.selectorText.split(',')).map(s => s.trim().replace(/::?(before|after)/g, ''));

              const fresh = ['button', 'input', 'select', 'a', 'div', 'span', 'h2', 'label'].map(tag => body.appendChild(document.createElement(tag)));
              try {
                const offenders = [];
                let parts = 0;
                for (const element of document.querySelectorAll('*')) {
                  const hits = selectors.filter(s => { try { return element.matches(s); } catch { return false; } });
                  if (hits.length === 0) continue;
                  if (element.closest('[class*="spoitable-"]')) { parts++; continue; }
                  offenders.push(`${element.tagName.toLowerCase()}.${element.className} ← ${hits.join(' / ')}`);
                }
                return parts === 0 ? ['ui.css がどの部品にも当たっていない（見ていない）'] : offenders;
              } finally {
                for (const element of fresh) element.remove();
              }
            }
            """);

        Assert.True(result.Length == 0, "ui.css が部品の外に当たっている:" + Environment.NewLine + string.Join(Environment.NewLine, result));
    }

    /// <summary>ui.css は頭の付いた名前にしか当たらないので、頭の無い古い名前が残るとその部品だけ素のまま出る。</summary>
    private const string OldNames =
        ".btn, .ib, .link, .search, .find, .switch, .tabs, .tab, .toolbar, .spacer, .hint, .tiles, .tile, .card, .card-head, .empty, " +
        ".modal, .dialog, .dialog-head, .dialog-title, .dialog-body, .dialog-foot, " +
        ".pick-box, .pick-value, .pick-list, .when-box, .when-value, .when-pop, .when-head, .when-grid, .when-time, .when-foot, .tip-pop, " +
        ".spoitable-btn.primary, .spoitable-btn.ghost, .spoitable-btn.small, .spoitable-btn.icon, .spoitable-tab.on, " +
        ".spoitable-tile .k, .spoitable-tile .v, .spoitable-tile .note, .spoitable-tile .loud, .spoitable-search .find, " +
        ".spoitable-when-value.empty, .spoitable-when-head .label, .spoitable-when-grid .dow, .spoitable-when-grid .other, " +
        ".spoitable-when-grid .today, .spoitable-when-time .sep";

    private static readonly string[] WristOwnNames = [".tiles", ".tile", ".empty"];

    private async Task<string[]> OldNamesInTheShellAsync() =>
        await Page.EvaluateAsync<string[]>(
            "names => [...document.querySelectorAll(names)].map(e => `${e.tagName.toLowerCase()}${e.id ? '#' + e.id : ''}.${e.className}`)",
            OldNames);

    [Fact]
    public async Task NoOldPartNamesAreLeftOnTheAppsPages()
    {
        List<string> found = [];
        async Task Look(string where) => found.AddRange((await OldNamesInTheShellAsync()).Select(x => $"{where}: {x}"));

        app.AppendLog("[Behaviour] Entering Room: E2E Host UI World", "[Behaviour] OnPlayerJoined e2e-host-ui");

        await Page.Locator("#sidebar .item[data-id='dashboard']").ClickAsync();
        await Assertions.Expect(Page.Locator("#dashTiles > *").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        found.AddRange((await Page.EvaluateAsync<string[]>($$"""
            names => {
              const before = statusItems;
              try {
                applyStatusItems([...before.filter(i => i.id !== 'hrs'), {{WarnHrs}}]);
                return [...document.querySelectorAll(names)].map(e => `${e.tagName.toLowerCase()}.${e.className}`);
              } finally {
                applyStatusItems(before);
              }
            }
            """, OldNames)).Select(x => $"ダッシュボード: {x}"));

        await Page.Locator("#sidebar .item[data-id='log']").ClickAsync();
        var row = Page.Locator("#rowsVrc .row[data-kind='PlayerJoined'] .line").First;
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await row.ClickAsync();
        await Assertions.Expect(Page.Locator("#logSheet")).ToBeVisibleAsync();
        await Look("ログの詳細");
        await Page.Locator("#sheetClose").ClickAsync();

        await Page.Locator("#logFilter").ClickAsync();
        await Assertions.Expect(Page.Locator("#filterModal")).ToBeVisibleAsync();
        await Look("絞り込み");
        await Page.Locator("#filterClose").ClickAsync();

        await Page.Locator(".spoitable-pick-box:has(#logSize) .spoitable-pick-value").ClickAsync();
        await Look("選ぶ欄");
        await Page.Keyboard.PressAsync("Escape");
        await InPage.OpenWhenAsync(Page, "#logFrom");
        await Look("暦");
        await Page.Keyboard.PressAsync("Escape");
        await Page.Locator(".spoitable-pick-box:has(#logSize) .spoitable-pick-value").HoverAsync();
        await Assertions.Expect(Page.Locator("[role=tooltip]:not([hidden])")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Look("吹き出し");

        await app.OpenSettings("vr");
        await Look("設定（本体）");
        await Page.Locator("#viewSettings .cfg-item[data-key='HiddenWristNotes'] .cfg-switch-field button").ClickAsync();
        await Assertions.Expect(Page.Locator("#switchModal")).ToBeVisibleAsync();
        await Look("スイッチの一覧");
        await Page.EvaluateAsync("() => closeSwitchModal()");
        await Page.Locator("#viewSettings .cfg-item[data-key='WristShortcuts']").GetByRole(AriaRole.Button).ClickAsync();
        await Assertions.Expect(Page.Locator("#shortcutModal")).ToBeVisibleAsync();
        await Look("手首のショートカット");
        await Page.EvaluateAsync("() => closeShortcutModal()");
        await app.OpenSettings("hrs");
        await Look("設定（心拍）");

        await Page.Locator("#sidebar .item[data-id='moduleManager']").ClickAsync();
        await Assertions.Expect(Page.Locator("#plList > *").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Look("モジュール");
        await Page.Locator("#plTabs [data-tab='updates']").ClickAsync();
        await Look("更新");
        await Page.Locator("#plTabs [data-tab='modules']").ClickAsync();
        await Page.Locator("#plAddBtn").ClickAsync();
        await Assertions.Expect(Page.Locator("#maModal")).ToBeVisibleAsync();
        await Look("モジュールを追加");
        await Page.Locator("#maUrlCancel").ClickAsync();

        await Page.Locator("#bell").ClickAsync();
        await Look("通知の履歴");
        await Page.Locator("#bell").ClickAsync();

        await Page.EvaluateAsync("""
            () => new Promise(resolve => {
              const frame = document.createElement('iframe');
              frame.name = 'e2eHostUiWrist';
              frame.style.cssText = 'position:fixed;left:0;top:0;width:1120px;height:240px;border:0;opacity:.01;pointer-events:none;z-index:9999';
              frame.onload = () => resolve();
              frame.src = '/wrist.html';
              document.body.appendChild(frame);
            })
            """);
        try
        {
            var wrist = Page.Frame("e2eHostUiWrist") ?? throw new InvalidOperationException("手首のページの iframe が無い");
            await wrist.WaitForFunctionAsync("() => typeof renderNotes === 'function'");
            var names = string.Join(", ", OldNames.Split(", ").Except(WristOwnNames));
            found.AddRange((await wrist.EvaluateAsync<string[]>(
                    "names => [...document.querySelectorAll(names)].map(e => `${e.tagName.toLowerCase()}.${e.className}`)", names))
                .Select(x => $"手首: {x}"));
        }
        finally
        {
            await Page.EvaluateAsync("() => document.querySelector('iframe[name=\"e2eHostUiWrist\"]')?.remove()");
        }

        Assert.True(found.Count == 0, "古い名前が残っている:" + Environment.NewLine + string.Join(Environment.NewLine, found.Distinct()));
    }

    private const string WarnHrs = "{ id: 'hrs', state: 'warn', text: 'e2e', note: 'e2e' }";

    [Fact]
    public async Task TheStatusTakesTheStateColourInTheSidebarAndOnTheDashboard()
    {
        await Page.Locator("#sidebar .item[data-id='dashboard']").ClickAsync();
        await Assertions.Expect(Page.Locator("#dashModules > *").First).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var colours = await Page.EvaluateAsync<string[]>($$"""
            () => {
              const before = statusItems;
              try {
                applyStatusItems([...before.filter(i => i.id !== 'hrs'), {{WarnHrs}}]);
                const item = document.querySelector('#sidebar .item[data-id="hrs"]');
                const row = [...document.querySelectorAll('#dashModules .st')].find(st => st.textContent === 'e2e');
                return [({{Token}})('--warn'), getComputedStyle(item.querySelector('.st')).color, getComputedStyle(item.querySelector('.pip')).color,
                  row ? getComputedStyle(row).color : 'ダッシュボードに心拍の行が無い'];
              } finally {
                applyStatusItems(before);
              }
            }
            """);

        Assert.Equal(colours[0], colours[1]);
        Assert.Equal(colours[0], colours[2]);
        Assert.Equal(colours[0], colours[3]);
    }

    /// <summary>入っているスイッチは、焦点が当たっても弾かれても、枠は入の色（設定の欄の焦点・弾かれたの枠より先に見せる）。</summary>
    [Fact]
    public async Task SettingsSwitchesAreTheHostSwitchAndKeepTheirOnBorder()
    {
        foreach (var tab in new[] { "hrs", "log" })
        {
            await app.OpenSettings(tab);
            var boxes = Page.Locator("#viewSettings .cfg-item input[type=checkbox]");
            await boxes.First.WaitForAsync(new() { State = WaitForSelectorState.Attached });

            var plain = await boxes.EvaluateAllAsync<string[]>(
                "boxes => boxes.filter(b => !b.classList.contains('spoitable-switch') || getComputedStyle(b).appearance !== 'none').map(b => b.closest('.cfg-item')?.dataset.key ?? '?')");
            Assert.True(plain.Length == 0, $"ui.css のスイッチになっていない設定の入/切（{tab}）: " + string.Join(", ", plain));
        }

        var borders = await Page.EvaluateAsync<string[]>($$"""
            () => {
              const item = document.createElement('div');
              item.className = 'cfg-item';
              const box = document.createElement('input');
              box.type = 'checkbox';
              box.className = 'spoitable-switch';
              box.checked = true;
              // ⚠ 動きを切ってから読む。スイッチは枠の色を .15s かけて変えるので、変わった直後に読むと動き出す前の色（入の色）が返り、
              // 焦点の枠が勝っていても緑になる。
              box.style.transition = 'none';
              item.append(box);
              document.getElementById('cfgList').prepend(item);
              try {
                box.focus();
                const focused = getComputedStyle(box).borderTopColor;
                item.classList.add('bad');
                const rejected = getComputedStyle(box).borderTopColor;
                return [({{Token}})('--accent'), String(box.matches(':focus')), focused, rejected];
              } finally {
                item.remove();
              }
            }
            """);

        Assert.Equal("true", borders[1]);
        Assert.Equal(borders[0], borders[2]);
        Assert.Equal(borders[0], borders[3]);
    }

    [Fact]
    public async Task AShortcutValueTakesTheStateColour()
    {
        await app.OpenSettings("vr");
        await Page.Locator("#viewSettings .cfg-item[data-key='WristShortcuts']").GetByRole(AriaRole.Button).ClickAsync();
        await Assertions.Expect(Page.Locator("#scPlaced .sc-row[data-key='hrs']")).ToBeVisibleAsync();

        var colours = await Page.EvaluateAsync<string[]>($$"""
            () => {
              applyShortcutValues([{ key: 'hrs', text: 'e2e', state: 'warn' }]);
              const value = document.querySelector('#scPlaced .sc-row[data-key="hrs"] .val');
              return [({{Token}})('--warn'), value ? getComputedStyle(value).color : '並べた行に中身が無い'];
            }
            """);

        Assert.Equal(colours[0], colours[1]);
    }
}
