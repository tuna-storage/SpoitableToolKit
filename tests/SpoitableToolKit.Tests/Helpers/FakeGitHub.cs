using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

namespace SpoitableToolKit.Tests.Helpers;

/// <summary>偽の GitHub（HTTP の差し込み）。本物と同じく、名乗らない相手（User-Agent が無い）には 403 を返す。</summary>
internal sealed class FakeGitHub : HttpMessageHandler
{
    public byte[] Zip { get; set; } = [];

    public string Tag { get; set; } = "v1.2.0";

    public string AssetName { get; set; } = "sample-module.zip";

    public string? Digest { get; set; }

    public string Commit { get; set; } = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>id でないものを返させる。</summary>
    public string? CommitAnswer { get; set; }

    public HttpStatusCode CommitStatus { get; set; } = HttpStatusCode.OK;

    public byte[] Source { get; set; } = [];

    public Func<Task>? BeforeSource { get; set; }

    // 同時に来る要求から読むので、鍵をかけて写す。
    public string[] AskedPaths()
    {
        lock (Asked) return [.. Asked.Select(asked => asked.AbsolutePath)];
    }

    public List<string> Accepts { get; } = [];

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    public bool Offline { get; set; }

    public Exception? Throws { get; set; }

    /// <summary>JSON でないもの（途中の proxy の案内のページのような）を 200 で返す。</summary>
    public bool Garbage { get; set; }

    public List<Uri> Asked { get; } = [];

    public List<string> Agents { get; } = [];

    public int Releases => Asked.Count(u => u.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var agent = request.Headers.UserAgent.ToString();
        lock (Asked)
        {
            Asked.Add(request.RequestUri!);
            if (!Offline && Throws is null)
            {
                Agents.Add(agent);
                if (agent.Length > 0) Accepts.Add(request.Headers.Accept.ToString());
            }
        }

        if (Offline) throw new HttpRequestException("the network is down");
        if (Throws is not null) throw Throws;
        if (agent.Length == 0) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { RequestMessage = request });

        if (request.RequestUri!.AbsolutePath.EndsWith("/commits/HEAD", StringComparison.Ordinal))
        {
            if (CommitStatus != HttpStatusCode.OK) return Task.FromResult(new HttpResponseMessage(CommitStatus) { RequestMessage = request });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(CommitAnswer ?? Commit, Encoding.UTF8, "text/plain"), RequestMessage = request });
        }

        if (request.RequestUri!.AbsolutePath.Contains("/zipball/", StringComparison.Ordinal))
            return SourceAsync(request, cancellationToken);

        if (request.RequestUri!.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal))
        {
            if (Status != HttpStatusCode.OK) return Task.FromResult(new HttpResponseMessage(Status) { RequestMessage = request });
            if (Garbage)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent("<html>sign in</html>", Encoding.UTF8, "text/html"), RequestMessage = request });
            var body = JsonSerializer.Serialize(new
            {
                tag_name = Tag,
                assets = new[] { new { name = AssetName, browser_download_url = "http://127.0.0.1:1/download/sample-module.zip", size = Zip.Length, digest = Digest } },
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json"), RequestMessage = request });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Zip), RequestMessage = request });
    }

    private async Task<HttpResponseMessage> SourceAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // 足止めは、要求が止められたら解ける（本物の通信と同じく、止めれば返る）。
        if (BeforeSource is { } wait) await wait().WaitAsync(cancellationToken).ConfigureAwait(false);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Source), RequestMessage = request };
    }

    public static MemoryStream ZipOf(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
