using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using SpoitableToolKit.Tests.Helpers;

namespace SpoitableToolKit.Tests.Modules.Management;

internal sealed class FakeDotnetSite : HttpMessageHandler
{
    public static readonly Uri Metadata = new("http://127.0.0.1:1/dotnet/release-metadata/10.0/releases.json");

    public const string ZipPath = "/dotnet/Sdk/10.0.404/dotnet-sdk-10.0.404-win-x64.zip";

    private readonly List<string> _asked = [];

    public byte[] Zip { get; } = FakeGitHub.ZipOf(("dotnet.exe", "not a real dotnet"), ("sdk/10.0.404/dotnet.dll", "x")).ToArray();

    /// <summary>zip を返す前に待つ。要求が止められたら解ける。</summary>
    public Func<Task>? BeforeZip { get; set; }

    public string[] Asked
    {
        get
        {
            lock (_asked) return [.. _asked];
        }
    }

    /// <summary>HEAD（大きさを聞くだけ）は数えない。</summary>
    public int Downloads => Asked.Count(asked => asked == "GET " + ZipPath);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_asked) _asked.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");

        var isList = request.RequestUri.AbsolutePath.EndsWith("releases.json", StringComparison.Ordinal);
        if (!isList && request.Method == HttpMethod.Get && BeforeZip is { } wait) await wait().WaitAsync(cancellationToken).ConfigureAwait(false);

        var body = isList
            ? JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
            {
                ["latest-sdk"] = "10.0.404",
                ["releases"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["sdk"] = new
                        {
                            version = "10.0.404",
                            files = new object[]
                            {
                                new
                                {
                                    name = "dotnet-sdk-win-x64.zip", rid = "win-x64", url = "http://127.0.0.1:1" + ZipPath,
                                    hash = Convert.ToHexString(SHA512.HashData(Zip)).ToLowerInvariant(),
                                },
                            },
                        },
                    },
                },
            })
            : Zip;
        var content = new ByteArrayContent(request.Method == HttpMethod.Head ? [] : body);
        content.Headers.ContentLength = body.Length;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request };
    }
}
