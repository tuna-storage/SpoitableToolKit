using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions.Tools;
using SpoitableToolKit.Modules.VideoUrl.Config;
using SpoitableToolKit.Modules.VideoUrl.Resolving;
using SpoitableToolKit.Tests.Fakes;
using SpoitableToolKit.Tests.Hrs.Broadcast;
using Xunit;

namespace SpoitableToolKit.Tests.VideoUrl.Resolving;

/// <summary>
/// 解決を外のサーバーに任せる道。偽のサーバーは <c>HttpListener</c> で立てる。
/// 返す JSON の中の URL はその偽サーバー自身を指すので、探り（素の GET で本当に取れるかの確認）もそのまま通る。
/// </summary>
public sealed class VideoUrlServerTests
{
    private const string Video = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";

    /// <summary>空いているポートを 1 つ。<c>HttpListener</c> はポート 0 を受け付けない。</summary>
    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();

        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }

    /// <summary>外を使っている間に呼ばれてはいけない相手。呼ばれたら落ちる。</summary>
    private sealed class ExplodingTools : IExternalToolProvider
    {
        public Task<ExternalTool?> GetAsync(string toolId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException($"手元の {toolId} を取りに行った");
    }

    private sealed record Asked(string Path, string? Authorization, string Body);

    /// <summary>仕様書どおりに振る舞う偽のサーバー。返事は gzip で返す（生の JSON は 100KB を超えるので実物もそうする。既定の <c>HttpClient</c> は解いてくれない）。</summary>
    private sealed class FakeServer : IDisposable
    {
        private readonly HttpListener _listener = new();

        public FakeServer()
        {
            Port = FreePort();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();

            _ = Task.Run(LoopAsync);
        }

        public int Port { get; }

        public List<Asked> Received { get; } = [];

        /// <summary>200 以外にすると、その状態を返して JSON を返さない。</summary>
        public HttpStatusCode Answer { get; set; } = HttpStatusCode.OK;

        /// <summary>断るときに返す本文。実物は <c>{"error": ..., "message": ...}</c> を返す。</summary>
        public string? AnswerBody { get; set; }

        public void Dispose() => _listener.Close();

        private async Task LoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }

                try
                {
                    await AnswerAsync(context);
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
                {
                    // 相手が先に切った。次の要求を待つだけでよい。
                }
            }
        }

        private async Task AnswerAsync(HttpListenerContext context)
        {
            // 探りが叩く先。素の GET で取れることにしておかないと、候補が全部落とされる。
            if (context.Request.HttpMethod == "GET")
            {
                context.Response.StatusCode = (int)HttpStatusCode.PartialContent;
                await context.Response.OutputStream.WriteAsync(new byte[8]);
                context.Response.Close();
                return;
            }

            var body = await new StreamReader(context.Request.InputStream, Encoding.UTF8).ReadToEndAsync();

            lock (Received)
            {
                Received.Add(new Asked(
                    context.Request.Url!.AbsolutePath, context.Request.Headers["Authorization"], body));
            }

            if (Answer != HttpStatusCode.OK)
            {
                context.Response.StatusCode = (int)Answer;

                if (AnswerBody is { } said)
                    await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(said));

                context.Response.Close();
                return;
            }

            var client = JsonDocument.Parse(body).RootElement.GetProperty("client").GetString();

            context.Response.ContentType = "application/json";
            context.Response.AddHeader("Content-Encoding", "gzip");

            await using (var gzip = new GZipStream(context.Response.OutputStream, CompressionMode.Compress, true))
            {
                await gzip.WriteAsync(Encoding.UTF8.GetBytes(CannedFor(client, Port)));
            }

            context.Response.Close();
        }

        /// <summary>
        /// <c>yt-dlp -J</c> の出力のうち、後段が見るところだけを持った JSON。クライアントごとに違うものを返す
        /// （HLS を出すのは web_safari だけ、音声だけを出すのは default だけ）。
        /// </summary>
        private static string CannedFor(string? client, int port) => client switch
        {
            "web_safari" => $$"""
                {
                  "title": "canned",
                  "formats": [{
                    "format_id": "96", "protocol": "m3u8_native", "ext": "mp4",
                    "vcodec": "avc1.640028", "acodec": "mp4a.40.2", "height": 1080,
                    "url": "http://127.0.0.1:{{port}}/media/hls"
                  }]
                }
                """,
            "default" => $$"""
                {
                  "title": "canned",
                  "formats": [{
                    "format_id": "140", "protocol": "https", "ext": "m4a",
                    "vcodec": "none", "acodec": "mp4a.40.2", "abr": 129.502,
                    "url": "http://127.0.0.1:{{port}}/media/m4a"
                  }]
                }
                """,
            _ => $$"""
                {
                  "title": "canned",
                  "formats": [{
                    "format_id": "18", "protocol": "https", "ext": "mp4",
                    "vcodec": "avc1.42001E", "acodec": "mp4a.40.2", "height": 360,
                    "url": "http://127.0.0.1:{{port}}/media/mp4"
                  }]
                }
                """,
        };
    }

    private static (YouTubeResolver Resolver, VideoUrlConfig Config) Resolver(string url, string token = "")
    {
        var config = new VideoUrlConfig();
        config.Server.Enabled = true;
        config.Server.Url = url;
        config.Server.Token = token;

        return (new YouTubeResolver(new ExplodingTools(), new FakeLocalizer(), NullLogger.Instance, config), config);
    }

    /// <summary>
    /// クライアント 1 つにつき 1 回頼む（HLS と progressive と音声だけは、取れるクライアントがそれぞれ違う。サーバー側でまとめさせると「どれを使うか」の規則がサーバーにも住む）。
    /// 並びは映像のあるものが先。画面はこの並びのまま出す。
    /// </summary>
    [Fact]
    public async Task TheServerIsAskedOncePerPlayerClient()
    {
        using var server = new FakeServer();
        var (resolver, _) = Resolver($"http://127.0.0.1:{server.Port}/resolve");

        var result = await resolver.ResolveAsync(Video, CancellationToken.None);

        var clients = AskedClients(server);
        Assert.Equal(["android", "default", "web_safari"], clients);

        Assert.Equal("canned", result.Title);
        Assert.Equal(["hls", "muxed", "audio"], [.. result.Streams.Select(s => s.Kind)]);
    }

    /// <summary>音声だけのクライアントを空にすると、音声だけの 1 本は頼まない。</summary>
    [Fact]
    public async Task AnEmptyAudioClientAsksForNoAudio()
    {
        using var server = new FakeServer();
        var (resolver, config) = Resolver($"http://127.0.0.1:{server.Port}/resolve");

        config.Clients.Audio = " ";

        var result = await resolver.ResolveAsync(Video, CancellationToken.None);

        var clients = AskedClients(server);
        Assert.Equal(["android", "web_safari"], clients);
        Assert.Equal(["hls", "muxed"], [.. result.Streams.Select(s => s.Kind)]);
    }

    /// <summary>
    /// 音声だけのクライアントしか残っていなくても頼む（断らない）。取れたものは音声だけなので、「360p の mp4 が上限」の知らせは出さない。
    /// 題はそこから取る。
    /// </summary>
    [Fact]
    public async Task TheAudioClientAloneIsEnough()
    {
        using var server = new FakeServer();
        var (resolver, config) = Resolver($"http://127.0.0.1:{server.Port}/resolve");

        config.Clients.Hls = "";
        config.Clients.Progressive = "";

        var result = await resolver.ResolveAsync(Video, CancellationToken.None);

        var clients = AskedClients(server);
        Assert.Equal(["default"], clients);
        Assert.Equal(["audio"], [.. result.Streams.Select(s => s.Kind)]);
        Assert.Equal(("canned", (string?)null), (result.Title, result.Notice));
    }

    /// <summary>HLS が取れずに mp4 が取れたときは、動画側の事情だと知らせる（音声だけの 1 本が並んでいても）。</summary>
    [Fact]
    public async Task WithoutHlsTheNoticeSaysTheMp4IsTheCeiling()
    {
        using var server = new FakeServer();
        var (resolver, config) = Resolver($"http://127.0.0.1:{server.Port}/resolve");

        config.Clients.Hls = "";

        var result = await resolver.ResolveAsync(Video, CancellationToken.None);

        Assert.Equal(["muxed", "audio"], [.. result.Streams.Select(s => s.Kind)]);
        Assert.Equal("videourl.note.noHls", result.Notice);
    }

    private static string[] AskedClients(FakeServer server)
    {
        lock (server.Received)
        {
            return [.. server.Received
                .Select(a => JsonDocument.Parse(a.Body).RootElement.GetProperty("client").GetString() ?? "")
                .Order(StringComparer.Ordinal)];
        }
    }

    [Fact]
    public async Task TheVideoUrlIsPassedThrough()
    {
        using var server = new FakeServer();
        var (resolver, _) = Resolver($"http://127.0.0.1:{server.Port}/resolve");

        await resolver.ResolveAsync(Video, CancellationToken.None);

        Assert.All(server.Received, asked =>
            Assert.Equal(Video, JsonDocument.Parse(asked.Body).RootElement.GetProperty("url").GetString()));
    }

    /// <summary>打ち込まれた URL をそのまま叩く。<c>/resolve</c> のような口の名前をこちらで足さない（サーバーを建てるのは利用者自身で、どの道に生やすかはこちらの決めることではない）。</summary>
    [Fact]
    public async Task TheEnteredUrlIsUsedExactlyAsItIs()
    {
        using var server = new FakeServer();
        var (resolver, _) = Resolver($"http://127.0.0.1:{server.Port}/my/own/path");

        await resolver.ResolveAsync(Video, CancellationToken.None);

        Assert.NotEmpty(server.Received);
        Assert.All(server.Received, asked => Assert.Equal("/my/own/path", asked.Path));
    }

    /// <summary>token を入れてあれば <c>Authorization: Bearer</c> で運ぶ。</summary>
    [Fact]
    public async Task TheTokenRidesAlongAsABearer()
    {
        using var server = new FakeServer();
        var (resolver, _) = Resolver($"http://127.0.0.1:{server.Port}/resolve", "s3cret");

        await resolver.ResolveAsync(Video, CancellationToken.None);

        Assert.NotEmpty(server.Received);
        Assert.All(server.Received, asked => Assert.Equal("Bearer s3cret", asked.Authorization));
    }

    /// <summary>token が空ならヘッダを付けない（token を要求しないサーバー、たとえば待ち受けを localhost に寄せて SSH のトンネル越しに使う形が、同じ設定のまま通る）。</summary>
    [Fact]
    public async Task AnEmptyTokenSendsNoAuthorization()
    {
        using var server = new FakeServer();
        var (resolver, _) = Resolver($"http://127.0.0.1:{server.Port}/resolve");

        await resolver.ResolveAsync(Video, CancellationToken.None);

        Assert.NotEmpty(server.Received);
        Assert.All(server.Received, asked => Assert.Null(asked.Authorization));
    }

    /// <summary>外を使っている間、手元の道具は 1 回も触らない（手元に yt-dlp と deno を置かずに済むことがこの機能の値打ち）。</summary>
    [Fact]
    public async Task TheLocalToolsAreNeverFetchedWhileTheServerIsOn()
    {
        using var server = new FakeServer();

        // 器の IExternalToolProvider は呼ばれたら投げる。通ること自体が試験。
        var (resolver, _) = Resolver($"http://127.0.0.1:{server.Port}/resolve");

        var result = await resolver.ResolveAsync(Video, CancellationToken.None);

        Assert.NotEmpty(result.Streams);
    }

    /// <summary>断られたら token を指す（打ち直す先が分かる唯一の失敗なので分けている）。</summary>
    [Fact]
    public async Task AnUnauthorizedAnswerPointsAtTheToken()
    {
        using var server = new FakeServer { Answer = HttpStatusCode.Unauthorized };
        var (resolver, _) = Resolver($"http://127.0.0.1:{server.Port}/resolve", "wrong");

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync(Video, CancellationToken.None));

        Assert.Equal("videourl.error.serverUnauthorized(401)", failed.Message);
    }

    /// <summary>それ以外の失敗は状態を添えて出す（手元が壊れているのか外が壊れているのか、利用者が切り分けられるように）。</summary>
    [Fact]
    public async Task AFailedAnswerCarriesTheStatus()
    {
        using var server = new FakeServer { Answer = HttpStatusCode.BadGateway };
        var (resolver, _) = Resolver($"http://127.0.0.1:{server.Port}/resolve");

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync(Video, CancellationToken.None));

        Assert.Equal("videourl.error.serverFailed(502)", failed.Message);
    }

    /// <summary>
    /// サーバーが何と言ったかをそのまま出す。400 は「何かが形を満たしていない」としか言わず、url なのか client なのかはサーバーしか知らない。
    /// 実物が返す形は <c>{"error": "invalid_client", "message": "client must match …"}</c>。
    /// </summary>
    [Fact]
    public async Task TheWordsTheServerRefusedWithAreCarriedOut()
    {
        using var server = new FakeServer
        {
            Answer = HttpStatusCode.BadRequest,
            AnswerBody = """{"error":"invalid_request","message":"client is required"}""",
        };

        var (resolver, _) = Resolver($"http://127.0.0.1:{server.Port}/resolve");

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync(Video, CancellationToken.None));

        Assert.Equal("videourl.error.serverFailed(400: client is required)", failed.Message);
    }

    /// <summary>JSON で返ってこなくても出す（手前のプロキシが返した HTML かもしれず、読めたものを見せる方が切り分けに効く）。</summary>
    [Fact]
    public async Task AnAnswerThatIsNotJsonIsShownAsItIs()
    {
        using var server = new FakeServer
        {
            Answer = HttpStatusCode.BadGateway,
            AnswerBody = "<html>502 Bad Gateway</html>",
        };

        var (resolver, _) = Resolver($"http://127.0.0.1:{server.Port}/resolve");

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync(Video, CancellationToken.None));

        Assert.Equal("videourl.error.serverFailed(502: <html>502 Bad Gateway</html>)", failed.Message);
    }

    [Fact]
    public async Task AnUnauthorizedAnswerCarriesBothTheWordsAndTheHint()
    {
        using var server = new FakeServer
        {
            Answer = HttpStatusCode.Unauthorized,
            AnswerBody = """{"error":"unauthorized","message":"bearer token required"}""",
        };

        var (resolver, _) = Resolver($"http://127.0.0.1:{server.Port}/resolve", "wrong");

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync(Video, CancellationToken.None));

        Assert.Equal("videourl.error.serverUnauthorized(401: bearer token required)", failed.Message);
    }

    /// <summary>名乗るクライアントは設定から来る（yt-dlp が名前を増やしたときに、こちらを直さなくても使えるようにしてある）。</summary>
    [Fact]
    public async Task ThePlayerClientNamesComeFromTheSettings()
    {
        using var server = new FakeServer();
        var (resolver, config) = Resolver($"http://127.0.0.1:{server.Port}/resolve");

        config.Clients.Hls = "ios";
        config.Clients.Progressive = "tv";
        config.Clients.Audio = "web";

        await resolver.ResolveAsync(Video, CancellationToken.None);

        var clients = AskedClients(server);
        Assert.Equal(["ios", "tv", "web"], clients);
    }

    /// <summary>空にしたものは頼まない（要らないものを空にすれば、要求を減らせる）。</summary>
    [Fact]
    public async Task AnEmptyPlayerClientIsNotAskedFor()
    {
        using var server = new FakeServer();
        var (resolver, config) = Resolver($"http://127.0.0.1:{server.Port}/resolve");

        config.Clients.Progressive = "";
        config.Clients.Audio = "";

        var result = await resolver.ResolveAsync(Video, CancellationToken.None);

        var asked = Assert.Single(server.Received);
        Assert.Equal("web_safari", JsonDocument.Parse(asked.Body).RootElement.GetProperty("client").GetString());

        // 残した方の結果はちゃんと出る。
        Assert.Equal(["hls"], [.. result.Streams.Select(s => s.Kind)]);
    }

    /// <summary>全部空なら誰にも頼まずに断る（頼みに行ってから落ちると、設定が空なだけなのにネットワークの話に見える）。</summary>
    [Fact]
    public async Task WithNoPlayerClientAtAllNobodyIsAsked()
    {
        using var server = new FakeServer();
        var (resolver, config) = Resolver($"http://127.0.0.1:{server.Port}/resolve");

        config.Clients.Hls = "  ";
        config.Clients.Progressive = "";
        config.Clients.Audio = "";

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync(Video, CancellationToken.None));

        Assert.Equal("videourl.error.noClients", failed.Message);
        Assert.Empty(server.Received);
    }

    /// <summary>URL が空のまま入にしていたら、何も頼まずに断る（頼みに行ってから落ちると、失敗の理由がネットワークの話に見える）。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://example.com/resolve")]
    [InlineData("なにこれ")]
    public async Task AnUnusableServerUrlIsRefusedBeforeAskingAnyone(string url)
    {
        var (resolver, _) = Resolver(url);

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync(Video, CancellationToken.None));

        Assert.Equal("videourl.error.serverUrl", failed.Message);
    }
}
