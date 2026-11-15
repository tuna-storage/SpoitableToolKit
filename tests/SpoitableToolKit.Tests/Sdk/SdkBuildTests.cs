using System.Text.RegularExpressions;
using System.Xml.Linq;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Host.Modules.Loading;
using SpoitableToolKit.Host.Modules.Management;
using SpoitableToolKit.Host.Modules.Packaging;
using SpoitableToolKit.Modules.Hrs;
using SpoitableToolKit.Modules.VideoUrl;
using Xunit;

namespace SpoitableToolKit.Tests.Sdk;

/// <summary>モジュールを作る側の build の決まり（<c>src/SpoitableToolKit.Sdk/build/</c>）と、本体の決まりが食い違っていないこと。build が通した Id を本体が断る（またはその逆）と「作れたのに入らない」になる。決まりは 2 か所（MSBuild の正規表現と <see cref="ModuleManifest.IsValidId"/>）にあるので、build 側の正規表現をファイルから読んで突き合わせる。見本（<c>tests/fixtures/SampleModule</c>）は外の repo のモジュールと同じ書き方（csproj に Id と名前だけ）で、この project の build が <c>fixtures/sdk/sample</c> に配る。</summary>
public sealed class SdkBuildTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpoitableToolKit.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("repo の一番上が見つからない");
    }

    private static readonly Lazy<XDocument> Targets = new(() =>
        XDocument.Load(Path.Combine(RepoRoot(), "src", "SpoitableToolKit.Sdk", "build", "SpoitableToolKit.Sdk.targets")));

    private static string Property(string name) =>
        Targets.Value.Descendants(name).Select(e => e.Value).Single();

    [Fact]
    public void TheBuildAndTheAppAgreeOnHowLongANameMayBe() =>
        Assert.Equal(ModuleManifest.MaxNameLength.ToString(System.Globalization.CultureInfo.InvariantCulture), Property("SpoitableNameMaxLength"));

    [Fact]
    public void TheBuildAndTheAppAgreeOnWhichCharactersANameMayNotHold() =>
        Assert.Equal(ModuleManifest.NameForbiddenPattern, Property("SpoitableNameForbiddenPattern"));

    /// <summary>build が Id を通すか（targets の <c>SpoitableCheckProject</c> と同じ組み合わせ方: STK010・011・012）。</summary>
    private static bool BuildAccepts(string id) =>
        id.Length > 0
        && !Regex.IsMatch(id, Property("SpoitableIdReservedPattern"))
        && Regex.IsMatch(id, Property("SpoitableIdPattern"))
        && !Regex.IsMatch(id, Property("SpoitableIdDevicePattern"))
        && !id.Contains("..", StringComparison.Ordinal);

    [Theory]
    [InlineData("a", true)]
    [InlineData("hrs", true)]
    [InlineData("a.b-c_d", true)]
    [InlineData("console", true)]
    [InlineData("hostess", true)]
    [InlineData("my.host", true)]
    [InlineData("", false)]
    [InlineData("Hrs", false)]
    [InlineData("../x", false)]
    [InlineData("a/b", false)]
    [InlineData(".hidden", false)]
    [InlineData("a..b", false)]
    [InlineData("x.", false)]
    [InlineData("x-", false)]
    [InlineData("x\n", false)]
    [InlineData("con", false)]
    [InlineData("nul.module", false)]
    [InlineData("com1", false)]
    [InlineData("host", false)]
    [InlineData("common", false)]
    [InlineData("host.extra", false)]
    [InlineData("HOST", false)]
    public void TheBuildAndTheAppAgreeOnWhichIdsAreAccepted(string id, bool accepted)
    {
        Assert.Equal(accepted, ModuleManifest.IsValidId(id));
        Assert.Equal(accepted, BuildAccepts(id));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void TheBuildAndTheAppAgreeOnTheLength(int length, bool accepted)
    {
        var id = new string('a', length);

        Assert.Equal(accepted, ModuleManifest.IsValidId(id));
        Assert.Equal(accepted, BuildAccepts(id));
    }

    /// <summary>表に無い組み合わせでも同じ答えになること（決まりに関わる字と名前を 3 つまで並べた全部、3,616 通りで比べる）。</summary>
    [Fact]
    public void TheBuildAndTheAppAgreeOnEveryCombinationOfTheCharactersThatMatter()
    {
        string[] parts = ["a", "9", ".", "-", "_", "A", "\n", "/", " ", "con", "nul", "com1", "lpt9", "host", "common"];
        IEnumerable<string> ids = [""];
        List<string> all = [""];
        for (var length = 1; length <= 3; length++)
        {
            ids = [.. ids.SelectMany(head => parts.Select(part => head + part))];
            all.AddRange(ids);
        }

        var differing = all.Where(id => ModuleManifest.IsValidId(id) != BuildAccepts(id)).ToArray();

        Assert.Equal(3616, all.Count);
        Assert.Empty(differing);
        Assert.Contains(all, ModuleManifest.IsValidId);   // 全部が断られて「同じ」になっていないこと
    }

    private static string Sample => Path.Combine(AppContext.BaseDirectory, "fixtures", "sdk", "sample");

    /// <summary>build が作った <c>module.json</c> を本体がそのまま読める。<c>sdk</c> は作ったときの SDK の版（repo の中ではこの本体と同じ版）。</summary>
    [Fact]
    public void TheBuildWritesAManifestTheAppReads()
    {
        var file = Path.Combine(Sample, ModuleManifest.FileName);

        var manifest = ModuleManifest.Parse(File.ReadAllText(file));

        Assert.Equal(new ModuleManifest("sample", "Sample", "1.2.3", ModuleManifest.AppSdk.ToString(3)), manifest);
        Assert.Null(manifest.NewerSdkProblem(ModuleManifest.AppSdk));
        Assert.Equal((byte)'{', File.ReadAllBytes(file)[0]);   // BOM を付けない
    }

    /// <summary>入口の dll と <c>*.deps.json</c> が揃い、本体の API の dll（<c>SpoitableToolKit.Abstractions.dll</c>）は入っていない（入っていると本体のものと型が二重になる）。</summary>
    [Fact]
    public void WhatTheBuildPutsOutIsWhatTheAppInstalls()
    {
        var manifest = ModuleManifest.Parse(File.ReadAllText(Path.Combine(Sample, ModuleManifest.FileName)));

        Assert.Equal("module", ModuleInstaller.KindOf(Sample, manifest));
        Assert.Equal(Path.Combine(Sample, "SampleModule.dll"), ModuleDiscovery.FindEntryAssembly(Sample));
        Assert.Empty(Directory.GetFiles(Sample, "SpoitableToolKit.Abstractions.*", SearchOption.AllDirectories));
    }

    /// <summary>同梱のモジュールも同じ決まりで作っている。Id と名前は csproj の 1 か所に書き、build が dll に刻んだものを既定の本体が返す。Id は設定・記録の置き場と翻訳キーの頭なので、変わると利用者の設定と記録が読まれなくなる。</summary>
    [Fact]
    public void TheBundledModulesReportTheIdAndNameWrittenInTheirProjects()
    {
        ISpoitableModule hrs = new HrsModule();
        ISpoitableModule videoUrl = new VideoUrlModule();

        Assert.Equal(("hrs", "Heart Rate"), (hrs.Id, hrs.DisplayName));
        Assert.Equal(("videourl", "Video URL"), (videoUrl.Id, videoUrl.DisplayName));
    }

    /// <summary>SDK を通さずに作った dll（Id が刻まれていない）は、Id を読むと何をすればよいかの文で断る。黙ってフォルダ名や型の名前に落とすと、手で名前を変えた写しが別のモジュールとして動く。</summary>
    [Fact]
    public void AModuleBuiltWithoutTheSdkIsToldHowToGetItsId()
    {
        ISpoitableModule bare = new Unstamped();

        var error = Assert.Throws<InvalidOperationException>(() => bare.Id);
        Assert.Equal(
            $"{typeof(Unstamped).FullName} has no Id: its dll (SpoitableToolKit.Tests) was not built with the SpoitableToolKit.Sdk package, " +
            "which writes <SpoitableModuleId> from the csproj into the dll. Reference the SDK (<PackageReference Include=\"SpoitableToolKit.Sdk\" ... />) " +
            "and set <SpoitableModuleId> in the csproj; do not write Id in the module class.",
            error.Message);
        Assert.Contains("<SpoitableModuleName>", Assert.Throws<InvalidOperationException>(() => bare.DisplayName).Message);
    }

    private sealed class Unstamped : ISpoitableModule
    {
        public bool OnInit(IModuleHost host) => true;
    }
}
