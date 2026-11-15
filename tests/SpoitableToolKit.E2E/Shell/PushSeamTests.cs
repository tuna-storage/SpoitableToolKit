using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Shell;

/// <summary>ホストの push は、ページのスクリプトが読まれた時点から届く。bind が済む前に届いた行が二重に並ばないことを、bind を包んで bind を待つ前に push して見る。ページを読み直すが、アプリはこのクラスだけのものなので戻さない。</summary>
public sealed class PushSeamTests(AppFixture app) : IClassFixture<AppFixture>
{
    private IPage Page => app.Page;

    [Fact]
    public async Task AnAppLineThatArrivesBeforeTheBindIsShownOnce()
    {
        var row = await Page.EvaluateAsync<JsonElement>(
            "async () => { const logs = (await bridge.getStatus()).appLogs; return logs[logs.length - 1]; }");
        var seq = row.GetProperty("seq").GetInt64();

        await ReloadPushingFirst("appLogs", row);

        await Assertions.Expect(Page.Locator($"#rowsApp .row[data-seq='{seq}']")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task AVrChatEventThatArrivesBeforeTheBindIsShownOnce()
    {
        app.AppendLog(
            "[Behaviour] Entering Room: E2E Seam World",
            "[Behaviour] OnPlayerJoined e2e-seam-user");

        // 1 本の行はイベントとしても生行としても流れるので、種別まで絞る。
        var shown = Page.Locator("#rowsVrc .row[data-kind='PlayerJoined'][data-id]", new() { HasTextString = "e2e-seam-user" });
        await Assertions.Expect(shown).ToHaveCountAsync(1, new() { Timeout = 20_000 });
        var id = await shown.GetAttributeAsync("data-id");

        var row = await Page.EvaluateAsync<JsonElement>(
            "async id => (await bridge.queryLog(pageRequest({}))).rows.find(r => String(r.id) === id)", id);

        await ReloadPushingFirst("logEvents", row);

        await Assertions.Expect(Page.Locator($"#rowsVrc .row[data-kind='PlayerJoined'][data-id='{id}']")).ToHaveCountAsync(1);
    }

    /// <summary>init script はこのページのあとの読み込みにも残るので、URL の印で絞る。届けた先で投げても bind は続ける。</summary>
    private async Task ReloadPushingFirst(string channel, JsonElement row)
    {
        var mark = $"e2eSeam{channel}";
        await Page.AddInitScriptAsync($$"""
            if (new URLSearchParams(location.search).has('{{mark}}')) {
              const bind = CefSharp.BindObjectAsync.bind(CefSharp);
              CefSharp.BindObjectAsync = async (...names) => {
                try { window.stk.receive({{JsonSerializer.Serialize(channel)}}, [{{row.GetRawText()}}]); } catch (e) { console.error(e); }
                return await bind(...names);
              };
            }
            """);

        await Page.GotoAsync(new Uri(new Uri(Page.Url), $"index.html?{mark}=1").ToString());
        await Page.WaitForFunctionAsync("() => window.__stkReady === true", null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
    }
}
