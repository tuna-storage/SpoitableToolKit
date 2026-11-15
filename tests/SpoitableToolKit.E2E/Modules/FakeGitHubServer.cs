using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpoitableToolKit.E2E.Modules;

/// <summary>
/// 手元の偽の GitHub（loopback の http）。アプリは <c>SPOITABLE_GITHUB_API</c> でここへ向く — <b>試験から本物の GitHub へは出ない</b>。
/// <list type="bullet">
///   <item><c>GET /repos/&lt;owner&gt;/&lt;repo&gt;/releases/latest</c> — <see cref="Publish"/> したリリース（無ければ 404）</item>
///   <item><c>GET /download/&lt;owner&gt;/&lt;repo&gt;/&lt;asset&gt;</c> — その zip</item>
///   <item><c>GET /repos/&lt;owner&gt;/&lt;repo&gt;/commits/HEAD</c> — <see cref="PublishSource"/> した commit の id（無ければ 404）、
///         <c>GET /repos/&lt;owner&gt;/&lt;repo&gt;/zipball/&lt;commit&gt;</c> — そのソースの zip</item>
///   <item><c>GET /dotnet/release-metadata/10.0/releases.json</c>・<c>GET|HEAD /dotnet/Sdk/…zip</c> — <see cref="PublishDotnetSdk"/> した、
///         偽の .NET SDK の一覧と zip（アプリは <c>SPOITABLE_DOTNET_RELEASES</c> でここへ向く。<b>本物の配布元へは出ない</b>）</item>
///   <item>本物と同じく、名乗らない相手（User-Agent が無い）には 403</item>
/// </list>
/// 何も置かなければ、どの repo も 404（ほかの試験のクラスのアプリは、ここへ聞いても何も得ない）。
/// <para>
/// <see cref="Hold"/> で、答えを<b>足止め</b>できる（アプリが「調べている」「ダウンロードしている」「Build している」途中の画面を、待ち合わせ無しで見るため）。
/// </para>
/// </summary>
public sealed class FakeGitHubServer : IDisposable
{
    private sealed record Release(string Tag, string Asset, byte[] Zip);

    private sealed record SourceTree(string Commit, byte[] Zip);

    private sealed record DotnetSdk(string Version, byte[] Zip);

    /// <summary>足止め 1 つ（<see cref="Hold"/> が返す）。Dispose で放す。</summary>
    private sealed class Gate(FakeGitHubServer owner, string prefix, int after) : IDisposable
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Prefix { get; } = prefix;

        public int After { get; } = after;

        public Task Released => _released.Task;

        public void Dispose()
        {
            owner._holds.TryRemove(this, out _);
            _released.TrySetResult();
        }
    }

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly ConcurrentDictionary<string, Release> _releases = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SourceTree> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Gate, byte> _holds = new();
    private volatile DotnetSdk? _dotnet;
    private readonly CancellationTokenSource _stop = new();

    public FakeGitHubServer()
    {
        _listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
        _ = AcceptAsync();
    }

    /// <summary>アプリに渡す API の場所（<c>SPOITABLE_GITHUB_API</c>）。</summary>
    public string Url { get; }

    /// <summary>調子が悪い（リリースを聞かれたら 500 を返す）。</summary>
    public bool Down { get; set; }

    public ConcurrentQueue<string> Requests { get; } = new();

    /// <summary><paramref name="repository"/>（<c>owner/name</c>）の最新のリリースを置く（前のものは置き換える）。</summary>
    public void Publish(string repository, string tag, byte[] zip, string asset = "module.zip") =>
        _releases[repository] = new Release(tag, asset, zip);

    /// <summary><paramref name="repository"/> の既定の branch の先頭の commit と、そのソースの zip を置く（リリースは置かない）。</summary>
    public void PublishSource(string repository, string commit, byte[] zip) => _sources[repository] = new SourceTree(commit, zip);

    /// <summary>.NET SDK の一覧の場所（アプリに渡す <c>SPOITABLE_DOTNET_RELEASES</c>）。</summary>
    public string DotnetReleases => Url + "dotnet/release-metadata/10.0/releases.json";

    /// <summary>偽の .NET SDK（一覧の latest-sdk と、その win-x64 の zip）を置く。</summary>
    public void PublishDotnetSdk(string version, byte[] zip) => _dotnet = new DotnetSdk(version, zip);

    /// <summary>
    /// 足止め: 道が <paramref name="pathPrefix"/> で始まる <b>GET</b> に、放すまで答えない（返したものを Dispose するか、<see cref="ReleaseAll"/> で放す）。
    /// <paramref name="after"/> を渡すと、中身をそこまで送ってから止まる（ダウンロードの途中）。
    /// <list type="bullet">
    ///   <item>止めるのは GET だけ。大きさを聞く HEAD は通す（止めると、アプリは「ダウンロードしますか」と聞く所まで来ない）</item>
    ///   <item>届いたことは、止める前に <see cref="Requests"/> に積む（試験は、それを待ってから画面を見る）</item>
    /// </list>
    /// </summary>
    public IDisposable Hold(string pathPrefix, int after = 0)
    {
        var gate = new Gate(this, pathPrefix, after);
        _holds[gate] = 0;
        return gate;
    }

    public void ReleaseAll()
    {
        foreach (var gate in _holds.Keys) gate.Dispose();
    }

    public static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var target = archive.CreateEntry(name).Open();
                target.Write(content);
            }
        }

        return stream.ToArray();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    /// <summary>要求を 1 つ読んで、答えて、閉じる（<c>Connection: close</c>。HttpClient は次の要求で繋ぎ直す）。</summary>
    private async Task ServeAsync(TcpClient client)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                var buffer = new byte[16384];
                var length = 0;
                while (length < buffer.Length)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(length), _stop.Token);
                    if (read == 0) break;
                    length += read;
                    if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n\r\n", StringComparison.Ordinal)) break;
                }

                var head = Encoding.ASCII.GetString(buffer, 0, length);
                var parts = head.Split(' ', 3);
                var method = parts[0];
                var path = parts.Length > 1 ? parts[1] : "/";
                Requests.Enqueue(method == "GET" ? path : $"{method} {path}");

                // 足止め（GET だけ）。答える前に止めるものは、ここで待つ。
                var gate = method == "GET" ? _holds.Keys.FirstOrDefault(hold => path.StartsWith(hold.Prefix, StringComparison.Ordinal)) : null;
                if (gate is { After: 0 }) await gate.Released.WaitAsync(_stop.Token);

                // .NET の配布元は、名乗らなくても答える（GitHub だけが User-Agent を要る）。
                var named = head.Contains("\r\nUser-Agent:", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/dotnet/", StringComparison.Ordinal);
                var (status, type, body) = named ? Answer(path) : (403, "text/plain", "a User-Agent is required"u8.ToArray());

                var header = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header), _stop.Token);
                if (method == "HEAD") return;

                // 中身の途中で止めるもの。
                if (gate is { After: > 0 } && gate.After < body.Length)
                {
                    await stream.WriteAsync(body.AsMemory(0, gate.After), _stop.Token);
                    await stream.FlushAsync(_stop.Token);
                    await gate.Released.WaitAsync(_stop.Token);
                    await stream.WriteAsync(body.AsMemory(gate.After), _stop.Token);
                }
                else
                {
                    await stream.WriteAsync(body, _stop.Token);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // 相手が先に切った・止めている途中。
        }
    }

    private (int Status, string Type, byte[] Body) Answer(string path)
    {
        var segments = path.Split('?')[0].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (Down) return (500, "text/plain", "down"u8.ToArray());

        if (segments is ["repos", var owner, var name, "releases", "latest"] && _releases.TryGetValue($"{owner}/{name}", out var release))
        {
            var json = JsonSerializer.Serialize(new
            {
                tag_name = release.Tag,
                assets = new[]
                {
                    new
                    {
                        name = release.Asset,
                        browser_download_url = $"{Url}download/{owner}/{name}/{release.Asset}",
                        size = release.Zip.Length,
                        digest = "sha256:" + Convert.ToHexString(SHA256.HashData(release.Zip)).ToLowerInvariant(),
                    },
                },
            });
            return (200, "application/json", Encoding.UTF8.GetBytes(json));
        }

        if (segments is ["repos", var sourceOwner, var sourceName, "commits", "HEAD"]
            && _sources.TryGetValue($"{sourceOwner}/{sourceName}", out var head))
            return (200, "text/plain", Encoding.ASCII.GetBytes(head.Commit));

        if (segments is ["repos", var treeOwner, var treeName, "zipball", var commit]
            && _sources.TryGetValue($"{treeOwner}/{treeName}", out var tree) && tree.Commit == commit)
            return (200, "application/zip", tree.Zip);

        if (_dotnet is { } dotnet)
        {
            var zipPath = $"dotnet/Sdk/{dotnet.Version}/dotnet-sdk-{dotnet.Version}-win-x64.zip";
            if (segments is ["dotnet", "release-metadata", "10.0", "releases.json"])
            {
                var list = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["latest-sdk"] = dotnet.Version,
                    ["releases"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            ["sdk"] = new
                            {
                                version = dotnet.Version,
                                files = new object[]
                                {
                                    new
                                    {
                                        name = "dotnet-sdk-win-x64.zip", rid = "win-x64", url = Url + zipPath,
                                        hash = Convert.ToHexString(SHA512.HashData(dotnet.Zip)).ToLowerInvariant(),
                                    },
                                },
                            },
                        },
                    },
                });
                return (200, "application/json", Encoding.UTF8.GetBytes(list));
            }

            if (string.Join('/', segments) == zipPath) return (200, "application/zip", dotnet.Zip);
        }

        if (segments is ["download", var fromOwner, var fromName, var asset]
            && _releases.TryGetValue($"{fromOwner}/{fromName}", out var wanted) && wanted.Asset == asset)
            return (200, "application/zip", wanted.Zip);

        return (404, "application/json", """{ "message": "Not Found" }"""u8.ToArray());
    }
}
