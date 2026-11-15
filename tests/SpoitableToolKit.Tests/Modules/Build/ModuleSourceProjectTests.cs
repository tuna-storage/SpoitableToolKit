using SpoitableToolKit.Host.Modules.Build;
using Xunit;

namespace SpoitableToolKit.Tests.Modules.Build;

public sealed class ModuleSourceProjectTests : IDisposable
{
    private const string Module = "<Project><PropertyGroup><SpoitableModuleId>sample</SpoitableModuleId></PropertyGroup></Project>";
    private const string Other = "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "stk-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Put(string relative, string content)
    {
        var file = Path.GetFullPath(Path.Combine(_root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
        return file;
    }

    [Fact]
    public void TheProjectThatNamesAModuleIdIsTheOneToBuild()
    {
        var module = Put("repo/src/Sample/Sample.csproj", Module);
        Put("repo/src/Sample.Contract/Sample.Contract.csproj", Other);
        Put("repo/tests/Sample.Tests/Sample.Tests.csproj", Other);

        Assert.Equal(module, ModuleSourceBuild.FindProject(_root));
    }

    [Fact]
    public void BuildOutputAndDotFoldersAreNotSearched()
    {
        var module = Put("repo/Sample.csproj", Module);
        Put("repo/bin/Copy/Sample.csproj", Module);
        Put("repo/obj/Sample.csproj", Module);
        Put("repo/.github/Sample.csproj", Module);

        Assert.Equal(module, ModuleSourceBuild.FindProject(_root));
    }

    /// <summary>DTD を持つ csproj は、外の定義を読みに行かないよう数えない。</summary>
    [Fact]
    public void AProjectThatIsNotPlainXmlIsNotCounted()
    {
        var module = Put("repo/Sample.csproj", Module);
        Put("repo/Broken/Broken.csproj", "<Project><SpoitableModuleId>sample");
        Put("repo/Dtd/Dtd.csproj",
            """<!DOCTYPE Project [<!ENTITY x SYSTEM "file:///c:/windows/win.ini">]><Project><SpoitableModuleId>&x;</SpoitableModuleId></Project>""");

        Assert.Equal(module, ModuleSourceBuild.FindProject(_root));
    }

    [Fact]
    public void ARepositoryWithoutAModuleProjectIsRefused()
    {
        Put("repo/Library.csproj", Other);

        var refused = Assert.Throws<InvalidDataException>(() => ModuleSourceBuild.FindProject(_root));

        Assert.Equal(
            "no module project was found: the app builds the one .csproj that has <SpoitableModuleId> in it " +
            "(a repository that only holds a contract or other code cannot be installed)",
            refused.Message);
    }

    /// <summary>勝手に選ぶと別のものを入れうるので選ばない。名前は repo が好きに書ける字なので、絞って並べる。</summary>
    [Fact]
    public void ARepositoryWithSeveralModuleProjectsIsRefusedAndNamesThem()
    {
        Put("repo/B/B.csproj", Module);
        Put("repo/A\u202E/A.csproj", Module);

        var refused = Assert.Throws<InvalidDataException>(() => ModuleSourceBuild.FindProject(_root));

        Assert.Equal(
            "2 module projects were found (repo/A?/A.csproj, repo/B/B.csproj); " +
            "the app builds a repository that holds exactly one .csproj with <SpoitableModuleId>",
            refused.Message);
    }

    [Fact]
    public void TheNuGetConfigTiesTheSdkToTheAppsFeed()
    {
        var config = System.Xml.Linq.XDocument.Parse(
            ModuleSourceBuild.NuGetConfig(new ModuleBuildTools("dotnet", @"C:\app\sdk & more", ModuleBuildTools.NuGetOrg)));

        string[] Under(string section) =>
            [.. config.Root!.Element(section)!.Elements().Select(element => element.ToString(System.Xml.Linq.SaveOptions.DisableFormatting))];

        Assert.Equal(
            [
                "<clear />",
                """<add key="spoitable-sdk" value="C:\app\sdk &amp; more" />""",
                """<add key="packages" value="https://api.nuget.org/v3/index.json" />""",
            ],
            Under("packageSources"));
        Assert.Equal(
            [
                "<clear />",
                """<packageSource key="spoitable-sdk"><package pattern="SpoitableToolKit.*" /></packageSource>""",
                """<packageSource key="packages"><package pattern="*" /></packageSource>""",
            ],
            Under("packageSourceMapping"));
        Assert.Equal(["<clear />"], Under("fallbackPackageFolders"));
    }

    [Fact]
    public void WithoutAnotherFeedOnlyTheAppsFeedIsListed()
    {
        var config = System.Xml.Linq.XDocument.Parse(ModuleSourceBuild.NuGetConfig(new ModuleBuildTools("dotnet", "feed", null)));

        Assert.Equal(["spoitable-sdk"], config.Root!.Element("packageSources")!.Elements("add").Select(add => add.Attribute("key")!.Value));
    }

    /// <summary><c>%</c> を先に書き換える（あとだと、書き換えた <c>%3B</c> の <c>%</c> まで書き換える）。</summary>
    [Fact]
    public void APropertyValueIsEscapedForMSBuild() =>
        Assert.Equal(
            @"-p:RestoreConfigFile=C:\Users\a%3Bb%2C c%2520d\x%2Cy\nuget.config",
            ModuleSourceBuild.Property("RestoreConfigFile", @"C:\Users\a;b, c%20d\x,y\nuget.config"));

    /// <summary>1 行は 2000 字まで（改行の無い長い出力で、いくらでも覚えさせないため）。</summary>
    [Fact]
    public async Task ALineOfBuildOutputIsCutAtALength()
    {
        var text = new string('x', 5000) + "\r\nshort\r\n\nlast";
        using var reader = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)));
        List<string> lines = [];

        await ModuleSourceBuild.ReadLinesAsync(reader, lines.Add);

        Assert.Equal([new string('x', 2000), "short", "", "last"], lines);
    }

    /// <summary>字は絞る（repo が好きに書ける字。向きを変える字で文の並びを変えさせない）。</summary>
    [Fact]
    public void OnlyTheErrorsAreTakenFromWhatTheBuildSaid()
    {
        string[] said =
        [
            "  Determining projects to restore...",
            @"C:\w\s\r\MyModule.csproj : warning NU1603: MyModule depends on X [C:\w\s\r\MyModule.csproj]",
            @"C:\w\s\r\MyModule.csproj(12,5): error STK016: MyModule: SpoitableModuleName is 129 characters long. [C:\w\s\r\MyModule.csproj]",
            @"C:\w\s\r\MyModule.csproj(12,5): error STK016: MyModule: SpoitableModuleName is 129 characters long. [C:\w\s\r\MyModule.csproj]",
            "C:\\w\\s\\r\\A.cs(1,8): error CS1029: #error: 'x\u202Ey' [C:\\w\\s\\r\\MyModule.csproj]",
        ];

        Assert.Equal(
            "error STK016: MyModule: SpoitableModuleName is 129 characters long. / error CS1029: #error: 'x?y'",
            ModuleSourceBuild.Errors(said));
    }

    [Fact]
    public void WithoutAnErrorLineTheLastLinesAreTaken()
    {
        Assert.Equal("two / three / four", ModuleSourceBuild.Errors(["one", "two", "", "  three", "four  ", " "]));
        Assert.Equal("the build said nothing", ModuleSourceBuild.Errors([]));
    }
}
