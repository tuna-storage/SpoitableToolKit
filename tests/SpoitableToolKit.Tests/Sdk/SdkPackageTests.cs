using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Localization;
using SpoitableToolKit.Host.Modules;
using SpoitableToolKit.Host.Modules.GitHub;
using SpoitableToolKit.Host.Modules.Management;
using SpoitableToolKit.Host.Modules.Packaging;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Sdk;

/// <summary><c>dotnet build</c> を何回か走らせるので重い（全部で 20 秒前後）。外すなら <c>dotnet test --filter "Category!=SdkBuild"</c>。読み込みをするので、ローダの試験と同じ並び（直列）に入れてある。</summary>
[Collection("module-loader")]
[Trait("Category", "SdkBuild")]
public sealed class SdkPackageTests(SdkWorkspace workspace) : IClassFixture<SdkWorkspace>
{
    private ModuleManifest Expected => new("mymodule", SdkWorkspace.ModuleName, "0.3.0", workspace.SdkVersion);

    /// <summary>出力はモジュールのフォルダの形で、本体の API の dll は入っていない（パッケージの <c>ref/</c> にだけあり、コンパイルにしか使われない）。</summary>
    [Fact]
    public void AModuleBuildsFromThePackageAlone()
    {
        Assert.True(workspace.Packed.ExitCode == 0, workspace.Packed.Output);

        string[] expected =
        [
            "MyModule.deps.json", "MyModule.dll", "MyModule.pdb", "MyModule.runtimeconfig.json",
            "locales/en.json", "locales/ja.json", "module.json", "web/index.html",
        ];
        Assert.Equal(expected, workspace.OutputFiles);
        Assert.DoesNotContain("SpoitableToolKit", File.ReadAllText(Path.Combine(workspace.Output, "MyModule.deps.json")));

        // ILogger の dll（Microsoft.Extensions.Logging.Abstractions）も、パッケージの ref/ にだけある。出力にも deps.json にも来ない（実行時は本体のもの）。
        Assert.DoesNotContain("Microsoft.Extensions.Logging", File.ReadAllText(Path.Combine(workspace.Output, "MyModule.deps.json")));

        // パッケージのアナライザーは読み込めている（読めないと CS8032 の警告だけで、決まりが黙って効かなくなる）。見本はどの決まりにも当たらない。
        Assert.DoesNotContain("CS8032", workspace.Packed.Output);
        Assert.DoesNotContain("STK04", workspace.Packed.Output);

        // パッケージは置き場に展開された（利用者の ~/.nuget/packages ではなく）。
        Assert.True(Directory.Exists(Path.Combine(workspace.Root, "packages", "spoitabletoolkit.sdk", workspace.SdkVersion)));
    }

    /// <summary>名前の特殊な字（<c>;</c>・<c>"</c>・<c>\</c>・<c>%</c> など）や日本語も、そのまま届く。</summary>
    [Fact]
    public void TheManifestSaysWhatTheProjectSays()
    {
        var manifest = ModuleManifest.Parse(File.ReadAllText(Path.Combine(workspace.Output, ModuleManifest.FileName)));

        Assert.Equal(Expected, manifest);
        Assert.Null(manifest.NewerSdkProblem(ModuleManifest.AppSdk));
        Assert.Equal(ModuleManifest.AppSdk.ToString(3), workspace.SdkVersion);
    }

    /// <summary><c>*.pdb</c> は入れない（作った機械のパスが入っている）。</summary>
    [Fact]
    public void TheZipHoldsTheOutputWithoutTheSymbols()
    {
        string[] expected =
        [
            "MyModule.deps.json", "MyModule.dll", "MyModule.runtimeconfig.json",
            "locales/en.json", "locales/ja.json", "module.json", "web/index.html",
        ];

        Assert.Equal(expected, SdkWorkspace.EntriesOf(workspace.Zip));
    }

    [Fact]
    public async Task TheAppInstallsAndLoadsWhatTheSdkPacked()
    {
        var root = Path.Combine(workspace.Root, "app-" + Guid.NewGuid().ToString("N"));
        var user = Path.Combine(root, "user");
        var app = Path.Combine(root, "app");
        var github = new FakeGitHub { Zip = workspace.Zip, Tag = "v0.3.0" };
        var installer = new ModuleInstaller(user, app,
            new GitHubReleases(new HttpClient(github), new Uri("http://127.0.0.1:1/")), NullLogger.Instance);

        var staged = await installer.StageFromGitHubAsync("https://github.com/someone/mymodule", default);

        Assert.Equal((Expected, "module"), (staged.Manifest, staged.Kind));
        Assert.True(installer.Confirm(staged.Token));
        var applied = PendingInstalls.Apply(user, app, NullLogger.Instance);
        Assert.Equal(["mymodule"], applied);

        var text = new Localizer(NullLogger<Localizer>.Instance);
        text.Load("ja", [LocaleSource.Host]);
        var logs = new Logs();
        var manager = SdkSandbox.NewManager(root, [user, app], text, logs);
        manager.LoadAll();

        Assert.Empty(manager.Skipped);
        var module = Assert.Single(manager.Modules);
        Assert.Equal(("mymodule", SdkWorkspace.ModuleName, ModuleState.Running), (module.Id, module.Instance.DisplayName, module.State));
        Assert.Equal(Path.Combine(user, "modules", "mymodule"), module.Candidate.Directory);
        Assert.Equal("見本のモジュール", text["mymodule.title"]);
        Assert.True(File.Exists(Path.Combine(module.WebRoot, "index.html")));

        // 見本は OnInit で host.Logger に書く。本体の ILogger をそのまま受け取れている（モジュールの出力に Logging の dll が無く、型が二重でない）。
        Assert.Contains("SpoitableToolKit.Modules.mymodule", logs.Categories);
        Assert.Contains("The sample module is running", logs.Infos);
    }

    /// <summary>作れないものは build を止めて、直し方を言う（モジュールを書くのは AI なので、文だけで直せるように）。止まるのは build の頭で、コンパイルまで行かない。</summary>
    [Theory]
    [InlineData("-p:SpoitableModuleId=", "STK010",
        "MyModule: SpoitableModuleId is not set. Add <SpoitableModuleId>mymodule</SpoitableModuleId> to the csproj (lower case). " +
        "It becomes the module's folder (modules/<Id>/), the start of its translation keys, and ModuleInfo.Id. " +
        "If this project is not a module but a dll of interfaces that modules share, set <SpoitableProjectKind>Contract</SpoitableProjectKind> instead.")]
    [InlineData("-p:SpoitableModuleId=Host.Extra", "STK011",
        "MyModule: SpoitableModuleId is 'Host.Extra', which is reserved for the app itself ('host' and 'common', and any Id that starts with 'host.' or 'common.'). " +
        "Give the module an Id of its own, for example its name in lower case.")]
    [InlineData("-p:SpoitableModuleId=My_Module", "STK012",
        "MyModule: SpoitableModuleId is 'My_Module'. It must be 1-64 characters of a-z, 0-9, '.', '_' or '-', starting and ending with a letter or digit, " +
        "without '..', and not a Windows device name such as con or nul. " +
        "It becomes a folder name and a part of URLs, so upper case and other characters are not accepted.")]
    [InlineData("-p:SpoitableModuleId=nul.x", "STK012", "MyModule: SpoitableModuleId is 'nul.x'. It must be 1-64 characters")]
    [InlineData("-p:SpoitableModuleId=a..b", "STK012", "MyModule: SpoitableModuleId is 'a..b'. It must be 1-64 characters")]
    [InlineData("-p:SpoitableModuleName=", "STK013",
        "MyModule: SpoitableModuleName is not set. Add <SpoitableModuleName>My module</SpoitableModuleName> to the csproj. " +
        "It is the name people see in the app (the module list and the sidebar), and it becomes ModuleInfo.Name.")]
    [InlineData("-p:SpoitableModuleName=A\u2028B", "STK014",
        "MyModule: SpoitableModuleName has a line break, a control character or an invisible formatting character " +
        "(such as a zero-width space or a text-direction mark) in it. " +
        "Write the name on one line with visible characters only: <SpoitableModuleName>My module</SpoitableModuleName>.")]
    [InlineData("-p:SpoitableModuleName=A\u202EB", "STK014",
        "MyModule: SpoitableModuleName has a line break, a control character or an invisible formatting character")]
    [InlineData("-p:Version=1.0_x", "STK015",
        "MyModule: Version is '1.0_x'. Write it as numbers separated by dots, such as <Version>1.0.0</Version> " +
        "(a suffix such as -beta.1 is fine; at most 64 characters of letters, digits, '.', '-' and '+'). " +
        "It is written into module.json and ModuleInfo.Version, and the app shows it next to the module's name.")]
    [InlineData("-p:SpoitableProjectKind=Tool", "STK001",
        "SpoitableProjectKind is 'Tool', which is not a kind the SDK knows. Use Module (a module the app loads; the default when the property is left out), " +
        "Contract (a dll of interfaces that modules share with each other) or Library (a package that only ships shared dlls).")]
    [InlineData("-p:SpoitableSdkVersion=", "STK002",
        "SpoitableSdkVersion is empty, so only a part of the SDK's build files was loaded. " +
        "Reference the SDK as a package (<PackageReference Include=\"SpoitableToolKit.Sdk\" Version=\"...\" />) and do not import its .props or .targets by hand.")]
    [InlineData("-p:SpoitableProjectKind=Contract -t:SpoitablePackModule", "STK020",
        "MyModule is a Contract project, and only a Module or a Library is packed into a zip. A contract dll is not installed on its own: " +
        "the module that provides it carries it (mark that module's reference to this project with SpoitableContract=\"Provide\", and pack that module).")]
    public void ABuildThatCannotMakeAModuleStopsAndSaysHowToFixIt(string arguments, string code, string message)
    {
        var build = workspace.Build("MyModule", restore: false, arguments.Split(' '));

        Assert.NotEqual(0, build.ExitCode);
        Assert.Contains($"error {code}: {message}", build.Output);
        Assert.DoesNotContain("error CS", build.Output);
    }

    /// <summary>zip は出力を丸ごと入れるので、置き場が出力の中だと前の回の zip まで入れて回すたびに太る。build の前に止める。</summary>
    [Theory]
    [InlineData("-p:SpoitableModulePackageDirectory=bin/Release/net10.0/zips", @"MyModule\bin\Release\net10.0\zips\ is inside ")]
    [InlineData("-o bin", @"MyModule\bin\module\ is inside ")]
    public void AZipFolderInsideTheBuildOutputStopsThePack(string arguments, string where)
    {
        var build = workspace.Build("MyModule", restore: false, ["-t:SpoitablePackModule", .. arguments.Split(' ')]);

        Assert.NotEqual(0, build.ExitCode);
        Assert.Contains("error STK023: MyModule: the zip would be written into the build output folder (", build.Output);
        Assert.Contains(where, build.Output);
        Assert.Contains(
            "The zip holds everything in that folder, so it would take in the zip of the build before it and grow with every build. " +
            "Point SpoitableModulePackageDirectory outside the output folder, or leave it out (the zip then goes to bin/module/ next to the csproj) " +
            "and keep the output folder from containing that place (-o and OutputPath move the output folder).",
            build.Output);
        Assert.DoesNotContain("error CS", build.Output);

        Assert.Equal(workspace.OutputFiles, SdkSandbox.FilesUnder(workspace.Output));
        Assert.False(File.Exists(Path.Combine(workspace.Root, "MyModule", "bin", "MyModule.dll")));
    }

    /// <summary>128 字は、本体が入れるときに断る長さと同じ。</summary>
    [Fact]
    public void ANameLongerThanTheAppAcceptsStopsTheBuild()
    {
        var build = workspace.Build("MyModule", restore: false, "-p:SpoitableModuleName=" + new string('x', 129));

        Assert.NotEqual(0, build.ExitCode);
        Assert.Contains(
            "error STK016: MyModule: SpoitableModuleName is 129 characters long. Keep it to 128 characters or fewer: " +
            "the app shows it in the module list and when it asks before installing, and refuses a longer one.",
            build.Output);
        Assert.DoesNotContain("error CS", build.Output);
    }

    /// <summary>listener の引数を <c>IEventListener</c> にして、型引数を推論する材料を無くしてある。判定は CS0411（build が落ちただけで通すと、ほかの compile error でも緑になる）。便利な overload を足すと、ここが黙って推論できるようになる。</summary>
    [Fact]
    public void AListenerCannotBeAddedOrRemovedWithoutWritingItsType()
    {
        var build = workspace.NoListenerType;

        Assert.NotEqual(0, build.ExitCode);
        string[] errors =
        [
            .. build.Output.Split('\n').Select(line => line.Trim()).Where(line => Regex.IsMatch(line, @"error CS\d{4}")).Distinct(),
        ];
        Assert.NotEmpty(errors);
        Assert.All(errors, error => Assert.Contains("error CS0411", error));

        int[] lines = [.. errors.Select(error => int.Parse(Regex.Match(error, @"ListenerUses\.cs\((\d+),").Groups[1].Value)).Distinct().Order()];
        Assert.Equal(SdkWorkspace.UntypedLines, lines);
    }

    /// <summary>STK040（パッケージのアナライザーが、本物の build で効く）。Id・DisplayName は csproj から dll に刻まれ、<c>ISpoitableModule</c> の既定の本体が返す。書いた行ごとに 1 つ。</summary>
    [Fact]
    public void AnIdOrNameWrittenInTheModuleClassStopsTheBuild()
    {
        var build = workspace.HandwrittenIdentity;

        Assert.NotEqual(0, build.ExitCode);
        string[] errors = [.. build.Output.Split('\n').Select(line => line.Trim()).Where(line => line.Contains("error STK040")).Distinct()];
        Assert.Equal(2, errors.Length);
        Assert.Contains(errors, error => error.Contains("MyModule.cs(8,") && error.Contains(
            "error STK040: MyModule writes the module's Id by hand. Remove it: the SDK writes SpoitableModuleId and SpoitableModuleName from the csproj into the dll, " +
            "and ISpoitableModule.Id and ISpoitableModule.DisplayName already return them. " +
            "To change the Id or the name, change <SpoitableModuleId> or <SpoitableModuleName> in the csproj."));
        Assert.Contains(errors, error => error.Contains("MyModule.cs(10,") && error.Contains("error STK040: MyModule writes the module's DisplayName by hand."));
    }

    /// <summary>STK041。警告だけで build は通る。</summary>
    [Fact]
    public void APanelPageThatIsNeverRegisteredIsWarnedAbout()
    {
        var build = workspace.UnregisteredPanel;

        Assert.True(build.ExitCode == 0, build.Output);
        Assert.Contains(
            "warning STK041: This module has web/index.html but never calls host.Ui.RegisterPanel, so its panel is not shown in the sidebar. " +
            "Call host.Ui.RegisterPanel(\"index.html\") in OnInit, or remove web/index.html if the module has no panel.",
            build.Output);
        Assert.Contains("MyModule.cs(3,", build.Output);
    }

    /// <summary><c>TargetFrameworks</c>（複数形）の project には、外側の build に zip の target が無い（パッケージの <c>build/</c> は内側でしか読まれない）。MSB4057 ではなく、何を直すかを言って止める。</summary>
    [Fact]
    public void AProjectWithSeveralTargetFrameworksIsToldToNameOne()
    {
        Assert.NotEqual(0, workspace.MultiTargeted.ExitCode);
        Assert.Contains(
            "error STK025: MyModule has TargetFrameworks (net10.0), and a module is built for one target framework. " +
            "Write <TargetFramework> (singular, for example net10.0) in the csproj instead, then pack again.",
            workspace.MultiTargeted.Output);
        Assert.DoesNotContain("MSB4057", workspace.MultiTargeted.Output);
    }

    /// <summary>環境に <c>NUGET_PACKAGES</c> があると <c>nuget.config</c> より勝って、パッケージが利用者の置き場に展開される（版が同じなので、次の回に古い中身が使われる）。子の <c>dotnet</c> には、置き場の <c>packages/</c> を指し直して渡す。</summary>
    [Fact]
    public void TheOutsideBuildKeepsItsPackagesInsideTheSandbox()
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["NUGET_PACKAGES"] = @"C:\Users\someone\.nuget\packages",
            ["NUGET_FALLBACK_PACKAGES"] = @"C:\Program Files\dotnet\sdk\NuGetFallbackFolder",
            ["MSBuildSDKsPath"] = @"C:\somewhere",
            ["PATH"] = @"C:\bin",
        };

        SdkSandbox.Isolate(environment, workspace.Root);

        Assert.Equal(Path.Combine(workspace.Root, "packages"), environment["NUGET_PACKAGES"]);
        Assert.False(environment.ContainsKey("NUGET_FALLBACK_PACKAGES"));
        Assert.False(environment.ContainsKey("MSBuildSDKsPath"));
        Assert.Equal(@"C:\bin", environment["PATH"]);
    }

    /// <summary>入っていると、本体が入れるときに断る。</summary>
    [Fact]
    public void TheAppsOwnMarkerFilesAreLeftOutOfTheZip()
    {
        string[] markers = [".disabled", ".remove", ".source.json"];
        foreach (var marker in markers) File.WriteAllText(Path.Combine(workspace.Output, marker), string.Empty);

        try
        {
            var build = workspace.Build("MyModule", restore: false, "-t:SpoitablePackModule");

            Assert.True(build.ExitCode == 0, build.Output);
            var entries = SdkWorkspace.EntriesOf(File.ReadAllBytes(workspace.ZipPath));
            Assert.Equal(SdkWorkspace.EntriesOf(workspace.Zip), entries);
        }
        finally
        {
            foreach (var marker in markers) File.Delete(Path.Combine(workspace.Output, marker));
        }
    }

    /// <summary>名前を変える前の <c>*.deps.json</c> が残っていると、本体はそちらの名前の dll をモジュールだと思うので、zip にせずに止める。</summary>
    [Fact]
    public void ALeftoverDepsFileFromAnotherNameStopsThePack()
    {
        var leftover = Path.Combine(workspace.Output, "OldName.deps.json");
        File.WriteAllText(leftover, "{}");

        try
        {
            var build = workspace.Build("MyModule", restore: false, "-t:SpoitablePackModule");

            Assert.NotEqual(0, build.ExitCode);
            Assert.Contains(
                "error STK021: MyModule: the output folder has a *.deps.json that this build did not make (OldName.deps.json). " +
                "The app takes the dll named like the *.deps.json as the module, so a leftover from an earlier build under another name would be loaded instead of MyModule.dll. " +
                "Delete the bin and obj folders and build again.",
                build.Output);
        }
        finally
        {
            File.Delete(leftover);
        }
    }
}
