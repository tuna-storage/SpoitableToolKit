using System.IO.Compression;
using SpoitableToolKit.Host.Modules;
using SpoitableToolKit.Host.Modules.Loading.Models;
using SpoitableToolKit.Host.Modules.Management;
using SpoitableToolKit.Host.Modules.Packaging;
using Xunit;

namespace SpoitableToolKit.Tests.Sdk;

/// <summary>
/// 渡す側の zip には契約の dll が <c>shared/</c> に入り、使う側の zip には入らない（両方を本体に入れると、API が片方からもう片方へ渡る）。
/// <c>dotnet build</c> を何回も走らせるので重い（20 秒前後）。外したいときは <c>dotnet test --filter "Category!=SdkBuild"</c>。
/// </summary>
[Collection("module-loader")]
[Trait("Category", "SdkBuild")]
public sealed class SdkContractTests(SdkContractWorkspace workspace) : IClassFixture<SdkContractWorkspace>
{
    private string ProviderZip => workspace.ZipOf("GreeterModule", "greeter", "1.0.0");

    private string UserZip => workspace.ZipOf("GreeterUser", "greeter-user", "1.0.0");

    private string LibraryZip => workspace.ZipOf("Clock.Library", "clock-lib", "2.1.0");

    /// <summary>zip を、本体が入れたあとの形（<c>modules/&lt;Id&gt;/</c>）に展開する。</summary>
    private static string Install(string zip, string root, string id)
    {
        var directory = Path.Combine(root, "app", "modules", id);
        ZipFile.ExtractToDirectory(zip, directory);
        return directory;
    }

    private string NewRoot() => Path.Combine(workspace.Root, "app-" + Guid.NewGuid().ToString("N"));

    /// <summary>渡す側（<c>SpoitableContract="Provide"</c>）: 契約の dll は <c>shared/</c> に入る。直下には来ない。</summary>
    [Fact]
    public void TheProviderCarriesTheContractInItsSharedFolder()
    {
        Assert.True(workspace.Provider.ExitCode == 0, workspace.Provider.Output);

        string[] output =
        [
            "GreeterModule.deps.json", "GreeterModule.dll", "GreeterModule.pdb", "GreeterModule.runtimeconfig.json",
            "module.json", "shared/Greetings.Contract.dll", "shared/Greetings.Contract.pdb",
        ];
        Assert.Equal(output, SdkSandbox.FilesUnder(workspace.OutputOf("GreeterModule")));

        string[] zip =
        [
            "GreeterModule.deps.json", "GreeterModule.dll", "GreeterModule.runtimeconfig.json",
            "module.json", "shared/Greetings.Contract.dll",
        ];
        Assert.Equal(zip, SdkSandbox.EntriesOf(File.ReadAllBytes(ProviderZip)));
    }

    /// <summary>使う側（<c>SpoitableContract="Use"</c>）: 契約の dll は出力にも zip にも入らない。</summary>
    [Fact]
    public void TheUserCarriesNoCopyOfTheContract()
    {
        Assert.True(workspace.User.ExitCode == 0, workspace.User.Output);

        string[] output = ["GreeterUser.deps.json", "GreeterUser.dll", "GreeterUser.pdb", "GreeterUser.runtimeconfig.json", "module.json"];
        Assert.Equal(output, SdkSandbox.FilesUnder(workspace.OutputOf("GreeterUser")));

        string[] zip = ["GreeterUser.deps.json", "GreeterUser.dll", "GreeterUser.runtimeconfig.json", "module.json"];
        Assert.Equal(zip, SdkSandbox.EntriesOf(File.ReadAllBytes(UserZip)));
    }

    /// <summary>
    /// 契約をパッケージで参照しても、印は同じに効く（別の repo のモジュールが渡す API を使うときの形）。
    /// </summary>
    [Fact]
    public void TheMarksWorkTheSameOnAPackageReference()
    {
        Assert.True(workspace.ContractPackage.ExitCode == 0, workspace.ContractPackage.Output);
        Assert.True(workspace.PackagedProvider.ExitCode == 0, workspace.PackagedProvider.Output);
        Assert.True(workspace.PackagedUser.ExitCode == 0, workspace.PackagedUser.Output);

        string[] provider =
        [
            "GreeterModule.deps.json", "GreeterModule.dll", "GreeterModule.pdb", "GreeterModule.runtimeconfig.json",
            "module.json", "shared/Greetings.Contract.dll",
        ];
        Assert.Equal(provider, SdkSandbox.FilesUnder(workspace.OutputOf("PackagedProvider")));

        string[] user = ["GreeterUser.deps.json", "GreeterUser.dll", "GreeterUser.pdb", "GreeterUser.runtimeconfig.json", "module.json"];
        Assert.Equal(user, SdkSandbox.FilesUnder(workspace.OutputOf("PackagedUser")));

        string[] zip =
        [
            "GreeterModule.deps.json", "GreeterModule.dll", "GreeterModule.runtimeconfig.json",
            "module.json", "shared/Greetings.Contract.dll",
        ];
        Assert.Equal(zip, SdkSandbox.EntriesOf(File.ReadAllBytes(workspace.ZipOf("PackagedProvider", "greeter", "1.0.0"))));
    }

    /// <summary>
    /// 2 つの zip を本体に入れると、両方が動き、使う側が渡す側の API を呼べる（契約の型は、渡す側の <c>shared/</c> の 1 つを両方が見ている）。
    /// </summary>
    [Fact]
    public void TheAppLoadsBothAndTheApiCrossesFromOneToTheOther()
    {
        var root = NewRoot();
        Install(ProviderZip, root, "greeter");
        Install(UserZip, root, "greeter-user");

        var manager = SdkSandbox.NewManager(root, [Path.Combine(root, "app")]);
        manager.LoadAll();

        Assert.Empty(manager.Skipped);
        string[] running = ["greeter", "greeter-user"];
        Assert.Equal(running, manager.Modules.Where(p => p.State == ModuleState.Running).Select(p => p.Id).Order());
        Assert.Equal("greeter", manager.SharedFiles["Greetings.Contract"].Owner);
        Assert.Equal("hello, user", File.ReadAllText(Path.Combine(root, "module-data", "greeter-user", "greeting.txt")));
    }

    /// <summary>
    /// 渡す側が入っていなければ、使う側は読み込まれず、理由に契約の名前が出る（使う側の zip に契約の写しが無いことの、本体の側からの確かめ）。
    /// </summary>
    [Fact]
    public void WithoutTheProviderTheUserIsNotLoadedAndTheReasonNamesTheContract()
    {
        var root = NewRoot();
        Install(UserZip, root, "greeter-user");

        var manager = SdkSandbox.NewManager(root, [Path.Combine(root, "app")]);
        manager.LoadAll();

        Assert.Empty(manager.Modules);
        var skipped = Assert.Single(manager.Skipped);
        Assert.Equal(("greeter-user", SkipReason.MissingDependency), (skipped.Id, skipped.Reason));
        Assert.Equal((DependencyProblemKind.Missing, "Greetings.Contract"), (skipped.Problem!.Kind, skipped.Problem.Needed.Name));
    }

    /// <summary>
    /// ライブラリの zip は <c>module.json</c> と <c>shared/</c> の dll だけ（自分の dll も <c>shared/</c> に入る）。
    /// 本体はそれをライブラリとして受け、dll を共有に載せる。
    /// </summary>
    [Fact]
    public void ALibraryPacksIntoAManifestAndSharedDllsAndTheAppTakesItAsALibrary()
    {
        Assert.True(workspace.Library.ExitCode == 0, workspace.Library.Output);

        string[] zip = ["module.json", "shared/Clock.Library.dll"];
        Assert.Equal(zip, SdkSandbox.EntriesOf(File.ReadAllBytes(LibraryZip)));

        var root = NewRoot();
        var directory = Install(LibraryZip, root, "clock-lib");
        var manifest = ModuleManifest.Parse(File.ReadAllText(Path.Combine(directory, ModuleManifest.FileName)));
        Assert.Equal(new ModuleManifest("clock-lib", "Clock library", "2.1.0", workspace.SdkVersion), manifest);
        Assert.Equal("library", ModuleInstaller.KindOf(directory, manifest));

        var manager = SdkSandbox.NewManager(root, [Path.Combine(root, "app")]);
        manager.LoadAll();

        Assert.Empty(manager.Skipped);
        Assert.Empty(manager.Modules);
        Assert.Equal("clock-lib", Assert.Single(manager.Libraries).Id);
        Assert.Equal("clock-lib", manager.SharedFiles["Clock.Library"].Owner);
    }

    /// <summary>
    /// 契約の project を印なしで参照したら、build を止める。通すと、契約の dll がモジュールの dll の隣にコピーされ、
    /// 渡す側と使う側が別々の写しを読み込んで、API が渡らない。
    /// </summary>
    [Fact]
    public void AReferenceToAContractProjectWithoutAMarkStopsTheBuild()
    {
        Assert.NotEqual(0, workspace.UnmarkedUser.ExitCode);
        Assert.Contains(
            "error STK031: GreeterUser references the contract project Greetings.Contract.csproj without saying which side it is on. " +
            "Add SpoitableContract=\"Provide\" to the ProjectReference if this module brings the contract dll (it registers the API), " +
            "or SpoitableContract=\"Use\" if another module brings it (this one only calls the API). " +
            "Without the mark the dll is copied next to the module's own dll, and each module would load its own copy of the interface.",
            workspace.UnmarkedUser.Output);
        Assert.DoesNotContain("error CS", workspace.UnmarkedUser.Output);
    }

    /// <summary>印は Provide か Use。ほかの字は、どちらとも読まずに止める。</summary>
    [Fact]
    public void AMarkThatNamesNeitherSideStopsTheBuild()
    {
        Assert.NotEqual(0, workspace.BadMarkUser.ExitCode);
        Assert.Contains(
            "error STK030: GreeterUser: the reference to Greetings.Contract.csproj has SpoitableContract=\"Maybe\". " +
            "Write Provide (this module brings the contract dll: it goes into the module's shared/ folder) or Use (another module brings it: nothing is copied).",
            workspace.BadMarkUser.Output);
        Assert.DoesNotContain("error CS", workspace.BadMarkUser.Output);
    }

    /// <summary>
    /// 契約が持ってくるファイルがサブフォルダに置かれるもの（文化ごとの <c>de/…resources.dll</c>・native の <c>runtimes/…</c>）なら、build を止める。
    /// 通すと、どれも <c>shared/</c> の直下に平たく並ぶ（文化どうしで同じ名前がぶつかる）。本体が読むのは <c>shared/</c> の直下の dll だけ。
    /// </summary>
    [Fact]
    public void AContractThatBringsFilesForASubFolderStopsTheBuild()
    {
        Assert.NotEqual(0, workspace.LocalizedProvider.ExitCode);
        Assert.Contains(
            @"error STK032: GreeterModule: the contract it provides brings files that belong in a sub-folder (de\Greetings.Contract.resources.dll). " +
            "A contract is one dll of interfaces: the app loads the dlls right in shared/ and nothing below it, " +
            "so translated resources or native files of a contract would not be found. Provide a contract that is a single dll.",
            workspace.LocalizedProvider.Output);
        Assert.DoesNotContain("error CS", workspace.LocalizedProvider.Output);
        Assert.Empty(SdkSandbox.FilesUnder(Path.Combine(workspace.OutputOf("LocalizedProvider"), "shared")));
    }

    /// <summary>
    /// 契約の project が SDK のほかを参照していたら、契約の build を止める。渡す側が <c>shared/</c> に持っていくのは契約の dll だけで、
    /// 契約が参照している物は付いていかない（本体は契約を読み込めない）。
    /// </summary>
    [Fact]
    public void AContractThatReferencesMoreThanTheSdkStopsItsOwnBuild()
    {
        Assert.NotEqual(0, workspace.DependentContract.ExitCode);
        Assert.Contains(
            "error STK033: Greetings.Contract is a Contract project and references Clock.Library.csproj, Some.Thing. " +
            "A contract is one dll of interfaces: the module that provides it puts that dll into its shared/ folder, " +
            "and what the contract references does not go with it, so the app could not load the contract. " +
            "Keep SpoitableToolKit.Sdk as its only reference, and declare the types the interfaces need inside the contract.",
            workspace.DependentContract.Output);
        Assert.DoesNotContain("error CS", workspace.DependentContract.Output);
    }

    /// <summary>
    /// ライブラリの zip に入るのは <c>module.json</c> と <c>shared/</c> の dll だけ。出力のサブフォルダに dll（native の <c>runtimes/…</c> など）があれば、
    /// 黙って落とさずに止める（落とすと、入れた先で足りない）。
    /// </summary>
    [Fact]
    public void ALibraryWithDllsBelowASubFolderStopsThePack()
    {
        var folder = Path.Combine(workspace.OutputOf("Clock.Library"), "runtimes");
        var nested = Path.Combine(folder, "win-x64", "native", "clock.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        File.WriteAllText(nested, "native");
        var before = File.ReadAllBytes(LibraryZip);

        try
        {
            var build = workspace.Build("Clock.Library", restore: false, "-t:SpoitablePackModule");

            Assert.NotEqual(0, build.ExitCode);
            Assert.Contains(
                @"error STK024: Clock.Library: the output folder has dlls below a sub-folder (runtimes\win-x64\native\clock.dll). " +
                "A library's zip carries module.json and the dlls for shared/ only, and the app loads only the dlls right in shared/, " +
                "so these would be left out and missing when the library is used. A library cannot ship native or translated dlls.",
                build.Output);
            Assert.Equal(before, File.ReadAllBytes(LibraryZip));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// ライブラリの zip は出力を丸ごとは拾わない（<c>module.json</c> と dll だけ）ので、置き場が出力の中でも太らない。止めない。
    /// </summary>
    [Fact]
    public void ALibraryMayPutItsZipInsideTheOutputFolder()
    {
        var zips = Path.Combine(workspace.OutputOf("Clock.Library"), "zips");

        try
        {
            var build = workspace.Build("Clock.Library", restore: false,
                "-t:SpoitablePackModule", "-p:SpoitableModulePackageDirectory=bin/Release/net10.0/zips");

            Assert.True(build.ExitCode == 0, build.Output);
            string[] zip = ["module.json", "shared/Clock.Library.dll"];
            Assert.Equal(zip, SdkSandbox.EntriesOf(File.ReadAllBytes(Path.Combine(zips, "clock-lib-2.1.0-module.zip"))));
        }
        finally
        {
            if (Directory.Exists(zips)) Directory.Delete(zips, recursive: true);
        }
    }

    /// <summary>契約は、それだけでは zip にしない（入れる単位ではない）。持っていくのは渡す側のモジュール。</summary>
    [Fact]
    public void AContractIsNotPackedOnItsOwn()
    {
        var build = workspace.Build("Greetings.Contract", restore: false, "-t:SpoitablePackModule");

        Assert.NotEqual(0, build.ExitCode);
        Assert.Contains(
            "error STK020: Greetings.Contract is a Contract project, and only a Module or a Library is packed into a zip.",
            build.Output);
    }
}
