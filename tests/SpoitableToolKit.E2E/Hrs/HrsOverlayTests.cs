using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace SpoitableToolKit.E2E.Hrs;

/// <summary>
/// OBS が開くオーバーレイ（http://localhost:&lt;ポート&gt;/overlay/&lt;名前&gt;）。配信タブのプレビューのための口
/// （パネルの origin で <c>preview</c> の印が付いたときだけ WebSocket の代わりにパネルの 1 通を受ける）を足しても、
/// http で開いた頁は今までどおり自分で WebSocket を開くことを、本物の頁を開いて見る。
/// 配信はループバックだけで待ち受ける（ファイアウォールの確認は出ない）。ポートは空いている番号。シェルの頁を離れるので、終わったら戻す。
/// </summary>
public sealed class HrsOverlayTests(AppFixture app) : IClassFixture<AppFixture>
{
    private IPage Page => app.Page;

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<int> ClientsAsync(HttpClient http, int port)
    {
        using var health = JsonDocument.Parse(await http.GetStringAsync($"http://localhost:{port}/health"));
        return health.RootElement.GetProperty("clients").GetInt32();
    }

    /// <summary>印 <c>preview</c> を付けても、http で開いた頁は WebSocket で繋ぎに来る（プレビューの口に入るのはパネルの origin のときだけ）。</summary>
    [Fact]
    public async Task AnOverlayOpenedOverHttpStillConnectsItsWebSocket()
    {
        var home = Page.Url;
        var port = FreePort();
        using var http = new HttpClient();

        await Page.EvaluateAsync($"() => bridge.applyConfigChanges(JSON.stringify({{ hrs: {{ 'Broadcast.Enabled': true, 'Broadcast.Port': {port} }} }}))");

        try
        {
            var listening = false;
            for (var i = 0; i < 40 && !listening; i++)
            {
                try
                {
                    listening = await ClientsAsync(http, port) == 0;
                }
                catch (HttpRequestException)
                {
                    await Task.Delay(250);
                }
            }
            Assert.True(listening, $"配信がポート {port} で待ち受けなかった");

            await Page.GotoAsync($"http://localhost:{port}/overlay/pill?preview");

            var clients = 0;
            for (var i = 0; i < 40 && clients == 0; i++)
            {
                clients = await ClientsAsync(http, port);
                if (clients == 0) await Task.Delay(250);
            }
            Assert.Equal(1, clients);
        }
        finally
        {
            await Page.GotoAsync(home);
            await Page.WaitForFunctionAsync("() => window.__stkReady === true", null, new PageWaitForFunctionOptions { Timeout = 30_000 });
            await Page.EvaluateAsync("() => bridge.applyConfigChanges(JSON.stringify({ hrs: { 'Broadcast.Enabled': false } }))");
        }
    }
}
