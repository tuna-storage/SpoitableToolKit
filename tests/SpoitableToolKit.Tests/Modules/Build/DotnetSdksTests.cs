using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Modules.Build;
using SpoitableToolKit.Host.Modules.Build.Sdk;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Modules.Build;

/// <summary>本物の SDK は落とさない（290 MB）: 一覧も zip も <c>dotnet --list-sdks</c> の答えも、手元の偽物が返す。</summary>
public sealed class DotnetSdksTests : IDisposable
{
    private static readonly Uri Metadata = new("http://127.0.0.1:1/dotnet/release-metadata/10.0/releases.json");
    private const string ZipUrl = "http://127.0.0.1:1/dotnet/Sdk/10.0.404/dotnet-sdk-10.0.404-win-x64.zip";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "stk-tests", Guid.NewGuid().ToString("N"));
    private readonly Site _site = new();
    private readonly Dictionary<string, string?> _listings = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _asked = [];

    private string Tools => Path.Combine(_root, "tools");
    private string Own => Path.Combine(Tools, "dotnet", "dotnet.exe");

    /// <summary>展開した先（入れ替える前）の <c>dotnet.exe</c>。入れる前に、ここへ <c>--list-sdks</c> を聞く。</summary>
    private string Unpacked => Path.Combine(Tools, "dotnet.new", "dotnet.exe");

    public DotnetSdksTests()
    {
        _site.Zip = FakeGitHub.ZipOf(("dotnet.exe", "not a real dotnet"), ("sdk/10.0.404/dotnet.dll", "x")).ToArray();
        _site.Metadata = MetadataFor(ZipUrl, Sha512(_site.Zip));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private DotnetSdks Sdks(params string[] candidates) =>
        new(new HttpClient(_site), Metadata, Tools, NullLogger.Instance, candidates, (dotnet, _) =>
        {
            _asked.Add(dotnet);
            return Task.FromResult(_listings.GetValueOrDefault(dotnet));
        });

    /// <summary><c>dotnet.exe</c> の代わりのファイルを置き、<c>--list-sdks</c> の答えを決める（null なら「答えない」）。</summary>
    private string Dotnet(string name, string? listing)
    {
        var dotnet = Path.Combine(_root, name, "dotnet.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(dotnet)!);
        File.WriteAllText(dotnet, "x");
        _listings[dotnet] = listing;
        return dotnet;
    }

    private static string Sha512(byte[] bytes) => Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant();

    /// <summary>公式の一覧（<c>releases.json</c>）と同じ形。要るところだけ。</summary>
    private static byte[] MetadataFor(string url, string hash, string latest = "10.0.404", string rid = "win-x64", string name = "dotnet-sdk-win-x64.zip") =>
        JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["channel-version"] = "10.0",
            ["latest-sdk"] = latest,
            ["releases"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["release-version"] = "10.0.15",
                    ["sdk"] = new
                    {
                        version = "10.0.404",
                        files = new object[]
                        {
                            new { name = "dotnet-sdk-win-x64.exe", rid = "win-x64", url = "http://127.0.0.1:1/installer.exe", hash = new string('0', 128) },
                            new { name = "dotnet-sdk-linux-x64.tar.gz", rid = "linux-x64", url = "http://127.0.0.1:1/linux.tar.gz", hash = new string('1', 128) },
                            new { name, rid, url, hash },
                        },
                    },
                },
            },
        });

    /// <summary>一覧の場所の差し替えは、自分の機械（loopback）を指すときだけ受ける（よその場所を指せると、Microsoft 以外から dotnet.exe を落とさせる口になる）。</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("not a url", null)]
    [InlineData("https://example.com/dotnet/release-metadata/10.0/releases.json", null)]
    [InlineData("http://192.168.0.10:8080/releases.json", null)]
    [InlineData("http://127.0.0.1:8080/dotnet/release-metadata/10.0/releases.json", "http://127.0.0.1:8080/dotnet/release-metadata/10.0/releases.json")]
    [InlineData("http://localhost:8080/releases.json", "http://localhost:8080/releases.json")]
    public void TheListOfReleasesCanOnlyBePointedAtThisMachine(string? custom, string? expected) =>
        Assert.Equal(expected is null ? DotnetSdks.DefaultMetadata : new Uri(expected), DotnetSdks.MetadataFrom(custom));

    [Fact]
    public void TheDotnetToLookAtCanBeNamed()
    {
        Assert.Null(DotnetSdks.CandidatesOverride(null));
        Assert.Null(DotnetSdks.CandidatesOverride(string.Empty));
        Assert.Equal(["C:\\nowhere\\dotnet.exe"], DotnetSdks.CandidatesOverride("C:\\nowhere\\dotnet.exe"));
    }

    [Theory]
    [InlineData("9.0.306 [C:\\Program Files\\dotnet\\sdk]\r\n10.0.100 [C:\\Program Files\\dotnet\\sdk]\r\n10.0.301 [C:\\Program Files\\dotnet\\sdk]\r\n", "10.0.301")]
    [InlineData("10.0.301 [C:\\x]\n10.0.100 [C:\\x]\n", "10.0.301")]
    [InlineData("11.0.100 [C:\\x]\n", "11.0.100")]
    [InlineData("9.0.306 [C:\\x]\n8.0.404 [C:\\x]\n", null)]
    [InlineData("10.0.100-rc.1.25451.107 [C:\\x]\n", null)]
    [InlineData("No .NET SDKs were found.\n", null)]
    [InlineData("99999999999.0.0 [C:\\x]\n10.0.100 [C:\\x]\n", "10.0.100")]
    [InlineData("\uFF11\uFF10.0.100 [C:\\x]\n", null)]
    [InlineData("\u0661\u0660.0.100 [C:\\x]\n", null)]
    [InlineData("", null)]
    public void TheNewestSdkThatCanBuildModulesIsPicked(string listing, string? expected) =>
        Assert.Equal(expected, DotnetSdkLocator.NewestSdk(listing)?.ToString());

    [Fact]
    public async Task TheFirstDotnetWithAUsableSdkIsUsed()
    {
        var old = Dotnet("old", "9.0.306 [C:\\x]\n");
        var silent = Dotnet("silent", null);
        var good = Dotnet("good", "10.0.100 [C:\\x]\n10.0.301 [C:\\x]\n");
        var later = Dotnet("later", "10.0.404 [C:\\x]\n");
        var missing = Path.Combine(_root, "missing", "dotnet.exe");

        var found = await Sdks(missing, old, silent, good, later).FindAsync(default);

        Assert.Equal((good, "10.0.301"), (found!.Dotnet, found.Version.ToString()));
        Assert.Equal([old, silent, good], _asked);
    }

    [Fact]
    public async Task WithoutAUsableSdkNothingIsFoundAndTheAppsOwnCopyIsAskedLast()
    {
        var old = Dotnet("old", "9.0.306 [C:\\x]\n");
        Directory.CreateDirectory(Path.GetDirectoryName(Own)!);
        File.WriteAllText(Own, "x");

        Assert.Null(await Sdks(old).FindAsync(default));
        Assert.Equal([old, Own], _asked);

        _listings[Own] = "10.0.404 [C:\\x]\n";
        Assert.Equal(Own, (await Sdks(old).FindAsync(default))!.Dotnet);
    }

    /// <summary>本物の <c>dotnet</c> に聞く（この repo を build できる機械には .NET SDK 10 以上がある）。</summary>
    [Fact]
    [Trait("Category", "SdkBuild")]
    public async Task TheRealDotnetOnThisMachineIsFound()
    {
        var sdks = new DotnetSdks(new HttpClient(_site), Metadata, Tools, NullLogger.Instance);

        var found = await sdks.FindAsync(default);

        Assert.NotNull(found);
        Assert.True(File.Exists(found.Dotnet));
        Assert.True(found.Version.Major >= DotnetSdks.Major);
        Assert.Empty(_site.Asked);
    }

    /// <summary><c>PATH</c> の絶対パスでない項目は見ない（<c>.</c> や相対の項目は、いまの作業フォルダにある <c>dotnet.exe</c> を走らせることになる）。引用符と空の項目は読み飛ばす。</summary>
    [Fact]
    public void OnlyRootedPathEntriesAreSearchedForDotnet()
    {
        var relative = "stk-relative-" + Guid.NewGuid().ToString("N");
        var inCurrent = Path.Combine(Environment.CurrentDirectory, relative);
        try
        {
            Directory.CreateDirectory(inCurrent);
            File.WriteAllText(Path.Combine(inCurrent, "dotnet.exe"), "x");
            var empty = Path.Combine(_root, "no-dotnet-here");
            Directory.CreateDirectory(empty);
            var first = Dotnet("first", null);
            var second = Dotnet("second", null);
            var path = string.Join(';', relative, ".", "", empty, $"\"{Path.GetDirectoryName(first)}\"", Path.GetDirectoryName(second));

            Assert.Equal([first, @"C:\Program Files\dotnet\dotnet.exe"], DotnetSdkLocator.CandidatesFrom(path, @"C:\Program Files"));
            Assert.Equal([first], DotnetSdkLocator.CandidatesFrom(path, ""));
        }
        finally
        {
            Directory.Delete(inCurrent, recursive: true);
        }
    }

    [Fact]
    public async Task TheLatestSdkZipIsOfferedWithItsSize()
    {
        var download = await Sdks().LatestAsync(default);

        Assert.Equal(("10.0.404", ZipUrl, Sha512(_site.Zip).ToUpperInvariant(), (long)_site.Zip.Length),
            (download.Version, download.Url.ToString(), download.Sha512, download.Bytes));
        Assert.Equal(["GET /dotnet/release-metadata/10.0/releases.json", "HEAD /dotnet/Sdk/10.0.404/dotnet-sdk-10.0.404-win-x64.zip"], _site.Asked);
    }

    /// <summary>一覧は 8 MB までしか読まない（実物は 1.2 MB）。</summary>
    [Fact]
    public async Task AListLargerThanTheLimitIsNotRead()
    {
        _site.Metadata = new byte[DotnetReleaseList.MaxMetadataBytes + 1];

        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => Sdks().LatestAsync(default));

        Assert.Equal("The list of .NET releases is larger than 8 MB", refused.Message);
    }

    [Fact]
    public async Task ADownloadThatSaysItIsTooLargeIsRefused()
    {
        var sdks = Sdks();
        var download = await sdks.LatestAsync(default);
        _site.ClaimedLength = DotnetSdkInstaller.MaxDownloadBytes + 1;

        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => sdks.InstallAsync(download, default));

        Assert.Equal("The .NET SDK download is larger than 1024 MB", refused.Message);
        Assert.False(Directory.Exists(Path.Combine(Tools, "dotnet")));
    }

    /// <summary>
    /// 一覧は、決めた場所そのものから読む。転送でよそのホストへ回された答えは読まない
    /// （zip の「一覧と同じ場所だけ」の決まりは、一覧を返した相手が決めた場所であることに頼っている）。
    /// </summary>
    [Fact]
    public async Task AListThatWasRedirectedToAnotherHostIsNotRead()
    {
        _site.MetadataLandsOn = new Uri("http://127.0.0.2:1/dotnet/release-metadata/10.0/releases.json");

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Sdks().LatestAsync(default));

        Assert.Equal("Refusing to read the list of .NET releases from 127.0.0.2: it was redirected away from 127.0.0.1", refused.Message);
    }

    /// <summary>大きさを聞けなくても落とす物は分かる。大きさは 0（分からない）として返す（見せるためだけの数字なので）。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WithoutAnAnswerAboutTheSizeTheDownloadIsStillOffered(bool timesOut)
    {
        _site.HeadThrows = timesOut ? new TaskCanceledException("the request timed out") : new HttpRequestException("the connection was reset");

        var download = await Sdks().LatestAsync(default);

        Assert.Equal(("10.0.404", 0L), (download.Version, download.Bytes));
    }

    [Fact]
    public async Task ACancelledLookUpStops()
    {
        using var stop = new CancellationTokenSource();
        _site.BeforeHead = stop.Cancel;
        _site.HeadThrows = new TaskCanceledException("cancelled");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sdks().LatestAsync(stop.Token));
    }

    [Fact]
    public void TheLatestSdkIsFoundAmongTheOtherBandsToo()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["latest-sdk"] = "10.0.404",
            ["releases"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["sdk"] = new { version = "10.0.110", files = new object[] { new { name = "a.zip", rid = "win-x64", url = "http://127.0.0.1:1/a.zip", hash = new string('a', 128) } } },
                    ["sdks"] = new object[]
                    {
                        new { version = "10.0.404", files = new object[] { new { name = "b.zip", rid = "win-x64", url = "http://127.0.0.1:1/b.zip", hash = new string('b', 128) } } },
                    },
                },
            },
        });

        Assert.Equal("http://127.0.0.1:1/b.zip", DotnetReleaseList.Choose(json, Metadata).Url.ToString());
    }

    [Theory]
    [InlineData("http://127.0.0.1:1/sdk.zip", "abc", "The list of .NET releases has no SHA-512 for the SDK 10.0.404")]
    [InlineData("http://127.0.0.2:1/sdk.zip", null, "The list of .NET releases points the SDK 10.0.404 at a place the app does not download from")]
    [InlineData("ftp://127.0.0.1/sdk.zip", null, "The list of .NET releases points the SDK 10.0.404 at a place the app does not download from")]
    [InlineData("http://127.0.0.1:2/sdk.zip", null, "The list of .NET releases points the SDK 10.0.404 at a place the app does not download from")]
    [InlineData("http://someone@127.0.0.1:1/sdk.zip", null, "The list of .NET releases points the SDK 10.0.404 at a place the app does not download from")]
    [InlineData("sdk.zip", null, "The list of .NET releases points the SDK 10.0.404 at a place the app does not download from")]
    public void AnEntryTheAppDoesNotAcceptIsRefused(string url, string? hash, string message)
    {
        var refused = Assert.Throws<InvalidDataException>(() => DotnetReleaseList.Choose(MetadataFor(url, hash ?? new string('a', 128)), Metadata));

        Assert.Equal(message, refused.Message);
    }

    [Fact]
    public void AnHttpUrlInTheRealListIsRefused()
    {
        var real = new Uri("https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json");

        Assert.Throws<InvalidDataException>(() =>
            DotnetReleaseList.Choose(MetadataFor("http://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.404/sdk.zip", new string('a', 128)), real));
        Assert.Equal("https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.404/sdk.zip",
            DotnetReleaseList.Choose(MetadataFor("https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.404/sdk.zip", new string('a', 128)), real).Url.ToString());
    }

    [Theory]
    [InlineData("{}", "The list of .NET releases does not name a latest SDK")]
    [InlineData("""{ "latest-sdk": "10.0.404<script>" }""", "The list of .NET releases does not name a latest SDK")]
    [InlineData("""{ "latest-sdk": "10.0.404", "releases": [] }""", "The list of .NET releases has no win-x64 zip for the SDK 10.0.404")]
    [InlineData("""{ "latest-sdk": "10.0.404", "releases": [ 1, { "sdk": 2, "sdks": [ 3 ] } ] }""", "The list of .NET releases has no win-x64 zip for the SDK 10.0.404")]
    public void AListWithoutTheSdkIsRefused(string json, string message)
    {
        var refused = Assert.Throws<InvalidDataException>(() => DotnetReleaseList.Choose(Encoding.UTF8.GetBytes(json), Metadata));

        Assert.Equal(message, refused.Message);
    }

    [Fact]
    public void AListThatIsNotJsonIsRefused()
    {
        var refused = Assert.Throws<InvalidDataException>(() => DotnetReleaseList.Choose("<html>sign in</html>"u8.ToArray(), Metadata));

        Assert.StartsWith("The list of .NET releases could not be read (", refused.Message);
    }

    [Theory]
    [InlineData("10.0.404", "linux-x64", "dotnet-sdk-linux-x64.zip")]
    [InlineData("10.0.404", "win-x64", "dotnet-sdk-win-x64.msi")]
    [InlineData("10.0.500", "win-x64", "dotnet-sdk-win-x64.zip")]
    public void WithoutAWindowsZipOfTheLatestSdkNothingIsOffered(string latest, string rid, string name)
    {
        var refused = Assert.Throws<InvalidDataException>(() =>
            DotnetReleaseList.Choose(MetadataFor(ZipUrl, new string('a', 128), latest, rid, name), Metadata));

        Assert.Equal($"The list of .NET releases has no win-x64 zip for the SDK {latest}", refused.Message);
    }

    [Fact]
    public async Task TheSdkIsDownloadedCheckedAndUnpacked()
    {
        _listings[Unpacked] = "10.0.404 [C:\\x]\n";
        var sdks = Sdks();

        var installed = await sdks.InstallAsync(await sdks.LatestAsync(default), default);

        Assert.Equal((Own, "10.0.404"), (installed.Dotnet, installed.Version.ToString()));
        Assert.Equal("not a real dotnet", File.ReadAllText(Own));
        Assert.True(File.Exists(Path.Combine(Tools, "dotnet", "sdk", "10.0.404", "dotnet.dll")));
        Assert.Equal(["dotnet"], Directory.GetFileSystemEntries(Tools).Select(Path.GetFileName));
    }

    [Fact]
    public async Task TheDownloadReportsHowFarItIs()
    {
        _listings[Unpacked] = "10.0.404 [C:\\x]\n";
        var filler = new byte[3 * 1024 * 1024];
        new Random(1).NextBytes(filler);
        using (var stream = new MemoryStream())
        {
            using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var exe = archive.CreateEntry("dotnet.exe").Open()) exe.Write("x"u8);
                using (var big = archive.CreateEntry("sdk/10.0.404/big.bin").Open()) big.Write(filler);
            }

            _site.Zip = stream.ToArray();
        }

        _site.Metadata = MetadataFor(ZipUrl, Sha512(_site.Zip));
        var reports = new Reports();
        var sdks = Sdks();

        await sdks.InstallAsync(await sdks.LatestAsync(default), default, reports);

        Assert.Equal(_site.Zip.Length, reports.Seen[^1]);
        Assert.Equal(reports.Seen.Order(), reports.Seen);
        Assert.InRange(reports.Seen.Count, 3, 4);
        Assert.All(reports.Seen.Take(reports.Seen.Count - 1), seen => Assert.InRange(seen, 1024 * 1024, _site.Zip.Length));
    }

    private sealed class Reports : IProgress<long>
    {
        public List<long> Seen { get; } = [];

        public void Report(long value) => Seen.Add(value);
    }

    [Fact]
    public async Task AnEarlierCopyIsReplaced()
    {
        _listings[Unpacked] = "10.0.404 [C:\\x]\n";
        var stale = Path.Combine(Tools, "dotnet", "sdk", "10.0.100", "old.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, "x");
        var sdks = Sdks();

        await sdks.InstallAsync(await sdks.LatestAsync(default), default);

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(Own));
        Assert.Equal(["dotnet"], Directory.GetFileSystemEntries(Tools).Select(Path.GetFileName));
    }

    [Fact]
    public async Task ADownloadThatDoesNotMatchTheListIsNotInstalled()
    {
        var earlier = Path.Combine(Tools, "dotnet", "kept.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(earlier)!);
        File.WriteAllText(earlier, "x");
        var sdks = Sdks();
        var download = await sdks.LatestAsync(default);
        _site.Zip = FakeGitHub.ZipOf(("dotnet.exe", "tampered")).ToArray();

        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => sdks.InstallAsync(download, default));

        Assert.Equal(
            "The .NET SDK 10.0.404 that was downloaded does not match the SHA-512 in the list of .NET releases; nothing was installed",
            refused.Message);
        Assert.True(File.Exists(earlier));
        Assert.Equal(["dotnet"], Directory.GetFileSystemEntries(Tools).Select(Path.GetFileName));
        Assert.False(File.Exists(Own));
    }

    /// <summary>展開したものが SDK を持っていなければ（<c>--list-sdks</c> が答えない）入れない。確かめるのは入れ替える前（前に入れた動いている SDK を残すため）。</summary>
    [Fact]
    public async Task AnUnpackedCopyThatListsNoSdkIsNotInstalledAndTheEarlierCopyStays()
    {
        var earlier = Path.Combine(Tools, "dotnet", "kept.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(earlier)!);
        File.WriteAllText(earlier, "x");
        var sdks = Sdks();

        var refused = await Assert.ThrowsAsync<InvalidDataException>(async () => await sdks.InstallAsync(await sdks.LatestAsync(default), default));

        Assert.Equal(
            "The .NET SDK 10.0.404 was downloaded and unpacked, but it does not list an SDK that can build modules; nothing was installed",
            refused.Message);
        Assert.True(File.Exists(earlier));
        Assert.False(File.Exists(Own));
        Assert.Equal(["dotnet"], Directory.GetFileSystemEntries(Tools).Select(Path.GetFileName));
    }

    /// <summary>前の回の残りは、入れる前に消す（1 つで 775 MB になりうる）。</summary>
    [Fact]
    public async Task LeftoversOfEarlierAttemptsAreSweptBeforeInstalling()
    {
        _listings[Unpacked] = "10.0.404 [C:\\x]\n";
        foreach (var name in (string[])["dotnet.old-12345678", "dotnet.new"])
        {
            Directory.CreateDirectory(Path.Combine(Tools, name));
            File.WriteAllText(Path.Combine(Tools, name, "left.txt"), "x");
        }

        File.WriteAllText(Path.Combine(Tools, "dotnet-sdk.zip.part"), "half");
        var sdks = Sdks();

        await sdks.InstallAsync(await sdks.LatestAsync(default), default);

        Assert.Equal(["dotnet"], Directory.GetFileSystemEntries(Tools).Select(Path.GetFileName));
        Assert.False(File.Exists(Path.Combine(Tools, "dotnet", "left.txt")));
    }

    [Fact]
    public async Task LeftoversOfAnEarlierRunAreClearedWhenTheAppStarts()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Own)!);
        File.WriteAllText(Own, "installed");
        foreach (var name in (string[])["dotnet.old-12345678", "dotnet.new"])
        {
            Directory.CreateDirectory(Path.Combine(Tools, name));
            File.WriteAllText(Path.Combine(Tools, name, "left.txt"), "x");
        }

        File.WriteAllText(Path.Combine(Tools, "dotnet-sdk.zip.part"), "half");

        await Sdks().ClearLeftoversAsync();

        Assert.Equal(["dotnet"], Directory.GetFileSystemEntries(Tools).Select(Path.GetFileName));
        Assert.Equal("installed", File.ReadAllText(Own));
    }

    /// <summary>落としている最中には片付けない（その回の zip を消してしまう）。片付けを呼んでも、その回はそのまま入る。</summary>
    [Fact]
    public async Task ClearingLeftoversLeavesAnInstallInProgressAlone()
    {
        _listings[Unpacked] = "10.0.404 [C:\\x]\n";
        var sdks = Sdks();
        var download = await sdks.LatestAsync(default);
        using var unpacking = new ManualResetEventSlim();
        using var go = new ManualResetEventSlim();
        var install = Task.Run(() => sdks.InstallAsync(download, default, unpacking: () =>
        {
            unpacking.Set();
            go.Wait(TimeSpan.FromSeconds(30));
        }));
        Assert.True(unpacking.Wait(TimeSpan.FromSeconds(30)), "展開する所まで来なかった");

        await sdks.ClearLeftoversAsync();
        Assert.True(File.Exists(Path.Combine(Tools, "dotnet-sdk.zip.part")), "落としている最中の zip を消した");

        go.Set();
        var installed = await install;
        Assert.Equal("10.0.404", installed.Version.ToString());
        Assert.Equal(["dotnet"], Directory.GetFileSystemEntries(Tools).Select(Path.GetFileName));
    }

    /// <summary>
    /// ほかのアプリ（同じ置き場で 2 つ目に起動したもの）が入れている最中なら、起動の片付けは何も消さない。
    /// その間は、こちらから入れることも断る（その回の物を消してしまう）。終われば、片付けも入れることもできる。
    /// </summary>
    [Fact]
    public async Task WhileAnotherCopyOfTheAppInstallsNothingOfItsIsTouched()
    {
        _listings[Unpacked] = "10.0.404 [C:\\x]\n";
        Directory.CreateDirectory(Path.Combine(Tools, "dotnet.new", "sdk"));
        File.WriteAllText(Path.Combine(Tools, "dotnet.new", "sdk", "half.dll"), "x");
        File.WriteAllText(Path.Combine(Tools, "dotnet-sdk.zip.part"), "half");
        var sdks = Sdks();
        var download = await sdks.LatestAsync(default);

        using (new FileStream(Path.Combine(Tools, ".installing"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose))
        {
            await sdks.ClearLeftoversAsync();
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => sdks.InstallAsync(download, default));

            Assert.Contains("Another copy of the app", refused.Message);
            Assert.True(File.Exists(Path.Combine(Tools, "dotnet.new", "sdk", "half.dll")), "ほかのアプリが展開している途中の物を消した");
            Assert.True(File.Exists(Path.Combine(Tools, "dotnet-sdk.zip.part")));
        }

        await sdks.ClearLeftoversAsync();
        Assert.Empty(Directory.GetFileSystemEntries(Tools));

        await sdks.InstallAsync(download, default);
        Assert.Equal(["dotnet"], Directory.GetFileSystemEntries(Tools).Select(Path.GetFileName));
    }

    [Fact]
    public async Task OnlyADownloadOfTheRightShapeIsAccepted()
    {
        var sdks = Sdks();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sdks.InstallAsync(new DotnetSdkDownload("10.0.404", new Uri(ZipUrl), "abc", 0), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sdks.InstallAsync(new DotnetSdkDownload("10.0.404", new Uri("http://example.invalid/sdk.zip"), new string('a', 128), 0), default));

        // 受けない URL へは、要求そのものを出さない（答えを見てから断るのでは遅い）。
        Assert.Empty(_site.Asked);
    }

    /// <summary>受け取った「落とす物」も、一覧から選ぶときと同じ決まりで見直す（呼ぶ側が別の URL とそれに合うハッシュを渡せると、ハッシュの突き合わせは通ってしまう）。</summary>
    [Theory]
    [InlineData("http://127.0.0.2:1/dotnet/Sdk/sdk.zip")]
    [InlineData("http://127.0.0.1:2/dotnet/Sdk/sdk.zip")]
    [InlineData("http://someone@127.0.0.1:1/dotnet/Sdk/sdk.zip")]
    public async Task ADownloadFromAnotherPlaceThanTheListIsRefusedBeforeAnythingIsAsked(string url)
    {
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Sdks().InstallAsync(new DotnetSdkDownload("10.0.404", new Uri(url), Sha512(_site.Zip), 0), default));

        Assert.Equal($"Refusing to fetch {new Uri(url)}: the .NET SDK is only downloaded from 127.0.0.1", refused.Message);
        Assert.Empty(_site.Asked);
        Assert.False(Directory.Exists(Tools));
    }

    [Fact]
    public async Task AZipThatCannotBeUnpackedLeavesNothingBehind()
    {
        _site.Zip = FakeGitHub.ZipOf(("dotnet.exe", "x"), ("sdk/a.txt", "x"), ("sdk/A.TXT", "x")).ToArray();
        _site.Metadata = MetadataFor(ZipUrl, Sha512(_site.Zip));
        var sdks = Sdks();

        var refused = await Assert.ThrowsAsync<InvalidDataException>(async () => await sdks.InstallAsync(await sdks.LatestAsync(default), default));

        Assert.StartsWith("The .NET SDK download has an entry that cannot be written on this PC: sdk/A.TXT (", refused.Message);
        Assert.Empty(Directory.GetFileSystemEntries(Tools));
    }

    /// <summary>偽の配布元: 一覧と zip を返す。</summary>
    private sealed class Site : HttpMessageHandler
    {
        public byte[] Metadata { get; set; } = [];

        public byte[] Zip { get; set; } = [];

        public List<string> Asked { get; } = [];

        /// <summary>zip の大きさとして名乗る値（null なら本当の大きさ）。</summary>
        public long? ClaimedLength { get; set; }

        /// <summary>一覧の答えが、転送でここへ回されたことにする。</summary>
        public Uri? MetadataLandsOn { get; set; }

        /// <summary>大きさを聞かれたら（HEAD）、これを投げる。</summary>
        public Exception? HeadThrows { get; set; }

        /// <summary>大きさを聞かれたときに、投げる前に呼ぶ。</summary>
        public Action? BeforeHead { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            var isList = request.RequestUri.AbsolutePath.EndsWith("releases.json", StringComparison.Ordinal);
            if (request.Method == HttpMethod.Head)
            {
                BeforeHead?.Invoke();
                if (HeadThrows is not null) throw HeadThrows;
            }

            if (isList && MetadataLandsOn is not null) request = new HttpRequestMessage(HttpMethod.Get, MetadataLandsOn);
            var body = isList ? Metadata : Zip;
            var content = new ByteArrayContent(request.Method == HttpMethod.Head ? [] : body);
            content.Headers.ContentLength = isList ? body.Length : ClaimedLength ?? body.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }
}
