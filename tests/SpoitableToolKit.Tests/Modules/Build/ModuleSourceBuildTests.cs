using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Modules.Build;
using SpoitableToolKit.Host.Modules.GitHub;
using SpoitableToolKit.Host.Modules.Management;
using SpoitableToolKit.Host.Modules.Management.Models;
using SpoitableToolKit.Host.Modules.Packaging;
using SpoitableToolKit.Tests.Helpers;
using SpoitableToolKit.Tests.Sdk;
using Xunit;

namespace SpoitableToolKit.Tests.Modules.Build;

/// <summary>本物の <c>dotnet build</c> を走らせるので重い。外したいときは <c>dotnet test --filter "Category!=SdkBuild"</c>。</summary>
[Trait("Category", "SdkBuild")]
public sealed class ModuleSourceBuildTests(SourceBuildWorkspace workspace) : IClassFixture<SourceBuildWorkspace>
{
    private static readonly TimeSpan Long = TimeSpan.FromMinutes(5);

    private static Task<string> Build(string work, ModuleBuildTools tools, TimeSpan? timeout = null) =>
        new ModuleSourceBuild(NullLogger.Instance)
            .BuildAsync(ModuleSourceBuild.FindProject(Path.Combine(work, "s")), work, tools, timeout ?? Long, default);

    private static ModuleManifest ManifestOf(string zip, string work)
    {
        using var stream = File.OpenRead(zip);
        return ModulePackage.Extract(stream, Path.Combine(work, "check")).Manifest;
    }

    /// <summary>
    /// 出来た zip は、こちらが決めた置き場（作業場の <c>o/</c>）に出る。中身は、リリースに付ける zip と同じ（本体が入れられる）。
    /// パッケージは、その build の作業場の中（<c>p/</c>）に展開される（利用者の <c>~/.nuget/packages</c> にも、ほかの build の展開先にも触らない）。
    /// </summary>
    [Fact]
    public async Task ASourceTreeBuildsIntoTheZipTheAppInstalls()
    {
        var work = workspace.Tree("plain");

        var zip = await Build(work, workspace.Tools());

        Assert.Equal(Path.Combine(work, "o", "mymodule-0.3.0-module.zip"), zip);
        var manifest = ManifestOf(zip, work);
        Assert.Equal(("mymodule", SdkWorkspace.ModuleName, "0.3.0", workspace.SdkVersion), (manifest.Id, manifest.Name, manifest.Version, manifest.Sdk));
        Assert.True(Directory.Exists(Path.Combine(work, ModuleSourceBuild.PackagesDirectoryName, "spoitabletoolkit.sdk", workspace.SdkVersion)));
    }

    /// <summary>
    /// 2 つの build を同時に走らせても、互いの邪魔をしない（本物の <c>dotnet build</c> を 2 本）。パッケージの展開先が build ごとなので、
    /// 片方の restore が、もう片方の使っているパッケージに触らない。
    /// </summary>
    [Fact]
    public async Task TwoBuildsAtOnceBothFinish()
    {
        var first = workspace.Tree("side-a");
        var second = workspace.Tree("side-b");
        var tools = workspace.Tools();

        var zips = await Task.WhenAll(Build(first, tools), Build(second, tools));

        Assert.Equal(Path.Combine(first, "o", "mymodule-0.3.0-module.zip"), zips[0]);
        Assert.Equal(Path.Combine(second, "o", "mymodule-0.3.0-module.zip"), zips[1]);
        Assert.True(Directory.Exists(Path.Combine(first, ModuleSourceBuild.PackagesDirectoryName, "spoitabletoolkit.sdk")));
        Assert.True(Directory.Exists(Path.Combine(second, ModuleSourceBuild.PackagesDirectoryName, "spoitabletoolkit.sdk")));
    }

    /// <summary>
    /// SDK のパッケージは、アプリの置き場からしか取らない。csproj が、<b>よその入れ元にちょうどある版</b>を書いていても同じ
    /// （結び付けが無いと、NuGet は書かれた版がちょうどある方を取り、よその <c>build/</c> が利用者の PC で動く）。
    /// </summary>
    [Fact]
    public async Task TheSdkPackageComesOnlyFromTheAppsOwnFeed()
    {
        var work = workspace.Tree("mapped", workspace.AskForTheRogueVersion);

        var zip = await Build(work, workspace.Tools(workspace.Rogue));

        Assert.Equal(workspace.SdkVersion, ManifestOf(zip, work).Sdk);
    }

    /// <summary>repo が持ってきた <c>nuget.config</c> は読まない（読むと、repo が入れ元を足せる）。</summary>
    [Fact]
    public async Task TheRepositorysOwnNuGetConfigIsNotRead()
    {
        var work = workspace.Tree("own-config", workspace.AskForTheRogueVersion, tree => File.WriteAllText(
            Path.Combine(tree, "nuget.config"),
            $"""<configuration><packageSources><add key="rogue" value="{workspace.Rogue}" /></packageSources></configuration>"""));

        var zip = await Build(work, workspace.Tools());

        Assert.Equal(workspace.SdkVersion, ManifestOf(zip, work).Sdk);
    }

    /// <summary>
    /// 作業場より上のフォルダにあるもの（<c>Directory.Build.targets</c>・<c>global.json</c>）は継がない。MSBuild と dotnet は project から上へ辿って探すので、
    /// 作業場の一番上に、そこで止まる印を置く（利用者のデータの置き場の上に何があっても、build が変わらない）。
    /// </summary>
    [Fact]
    public async Task FilesAboveTheWorkFolderAreNotInherited()
    {
        var above = Path.Combine(workspace.Root, "above");
        Directory.CreateDirectory(above);
        File.WriteAllText(Path.Combine(above, "Directory.Build.targets"),
            """<Project><Target Name="FromAbove" BeforeTargets="CoreCompile"><Error Text="INHERITED FROM ABOVE" /></Target></Project>""");
        File.WriteAllText(Path.Combine(above, "global.json"), """{ "sdk": { "version": "1.0.100", "rollForward": "disable" } }""");
        var work = workspace.Tree(Path.Combine("above", "work"));

        var zip = await Build(work, workspace.Tools());

        Assert.Equal("mymodule", ManifestOf(zip, work).Id);
    }

    /// <summary>
    /// 作業場の path にコンマと空白が入っていても build できる（データの置き場は利用者のフォルダの下で、利用者の名前が入る。
    /// MSBuild は <c>-p:</c> の値をコンマでも区切るので、書き換えて渡す）。
    /// </summary>
    [Fact]
    public async Task AWorkFolderWithACommaAndSpacesInItsPathStillBuilds()
    {
        var work = workspace.Tree("Tuna, The Second");

        var zip = await Build(work, workspace.Tools());

        Assert.Equal(Path.Combine(work, "o", "mymodule-0.3.0-module.zip"), zip);
    }

    /// <summary>
    /// build が通っても、頼んだ置き場に zip が出来ていなければ断る（SDK の決まりを使っていない project。<c>SpoitableModuleId</c> と同じ名前の target だけを持つもの）。
    /// </summary>
    [Fact]
    public async Task AProjectThatMakesNoZipIsRefused()
    {
        var work = Path.Combine(workspace.Root, "no-zip");
        var tree = Path.Combine(work, "s", "someone-fake-0123456");
        Directory.CreateDirectory(tree);
        File.WriteAllText(Path.Combine(tree, "Fake.csproj"), """
            <Project>
              <PropertyGroup><SpoitableModuleId>fake</SpoitableModuleId></PropertyGroup>
              <Target Name="Restore" />
              <Target Name="Build" />
              <Target Name="SpoitablePackModule" />
            </Project>
            """);

        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => Build(work, workspace.Tools()));

        Assert.Equal(
            "the build finished but made 0 zip files where the app asked for the module's one; " +
            "the project has to use the SpoitableToolKit SDK's own SpoitablePackModule target",
            refused.Message);
    }

    /// <summary>build が止まったら、build が言った error を文に入れる（モジュールを作った側が、そのまま直せる）。行の頭のパスと終わりの project は落とす。</summary>
    [Fact]
    public async Task ABuildThatFailsSaysWhatTheBuildSaid()
    {
        var name = $"<SpoitableModuleName>{SdkWorkspace.ModuleName}</SpoitableModuleName>";
        var work = workspace.Tree("too-long", csproj =>
        {
            Assert.Contains(name, csproj);
            return csproj.Replace(name, $"<SpoitableModuleName>{new string('x', 129)}</SpoitableModuleName>");
        });

        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => Build(work, workspace.Tools()));

        Assert.Equal(
            "the build failed (dotnet build exited with 1): error STK016: MyModule: SpoitableModuleName is 129 characters long. " +
            "Keep it to 128 characters or fewer: the app shows it in the module list and when it asks before installing, and refuses a longer one.",
            refused.Message);
    }

    /// <summary>build の出力は UTF-8 として読む（日本語の error が化けない）。</summary>
    [Fact]
    public async Task WhatTheBuildSaysIsReadAsUtf8()
    {
        var work = workspace.Tree("japanese", more: tree =>
            File.AppendAllText(Path.Combine(tree, "MyModule.cs"), "\n#error 日本語のエラー\n"));

        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => Build(work, workspace.Tools()));

        Assert.Contains("error CS1029: #error: '日本語のエラー'", refused.Message);
    }

    /// <summary>
    /// 終わらない build は時間で止める。<b>子のプロセスごと</b>止める — 残っていれば、作業場（その作業フォルダ）は消せない。
    /// <para>
    /// build が起こすのは 2 つ: 待たせ続ける子（ping）と、<b>親が先に抜けた孫</b>（内側の cmd が起こして、すぐ抜ける）。
    /// 孫は、親から辿って止める（<c>Process.Kill(entireProcessTree: true)</c>）だけでは届かない。入れ物（Job Object）ごと止める。
    /// </para>
    /// </summary>
    [Fact]
    public async Task ABuildThatDoesNotFinishIsStoppedWithItsChildren()
    {
        var work = workspace.Tree("hang", csproj => csproj.Replace("</Project>",
            """
            <Target Name="Hang" BeforeTargets="SpoitableCheckProject">
              <Exec Command="cmd /c &quot;start /b ping -n 300 127.0.0.1 &gt; nul&quot; &amp; ping -n 300 127.0.0.2 &gt; nul" />
            </Target></Project>
            """));

        var refused = await Assert.ThrowsAsync<InvalidDataException>(
            () => Build(work, workspace.Tools(), TimeSpan.FromSeconds(15)));

        Assert.Equal("the build did not finish in 15 seconds and was stopped", refused.Message);
        Assert.True(SourceBuildWorkspace.Delete(work));
    }

    [Fact]
    public async Task ABuildTheUserCancelsIsStoppedWithItsChildren()
    {
        var work = workspace.Tree("cancelled", csproj => csproj.Replace("</Project>",
            """
            <Target Name="Hang" BeforeTargets="SpoitableCheckProject">
              <Exec Command="cmd /c &quot;start /b ping -n 300 127.0.0.1 &gt; nul&quot; &amp; ping -n 300 127.0.0.2 &gt; nul" />
            </Target></Project>
            """));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ModuleSourceBuild(NullLogger.Instance)
            .BuildAsync(ModuleSourceBuild.FindProject(Path.Combine(work, "s")), work, workspace.Tools(), Long, stop.Token));

        Assert.True(SourceBuildWorkspace.Delete(work));
    }

    [Fact]
    public async Task ARepositoryIsBuiltFromTheCommitTheUserWasShown()
    {
        var github = new FakeGitHub { Commit = SourceBuildWorkspace.Commit, Source = SourceBuildWorkspace.ZipOf(workspace.Tree("repo")) };
        var user = Path.Combine(workspace.Root, "user");
        var installer = new ModuleInstaller(user, Path.Combine(workspace.Root, "app"),
            new GitHubReleases(new HttpClient(github), new Uri("http://127.0.0.1:1/")), NullLogger.Instance);
        var tools = workspace.Tools();
        List<SourceBuildPhase> phases = [];

        var offer = await installer.OfferSourceAsync("https://github.com/someone/sample", default);

        Assert.Equal(("someone/sample", SourceBuildWorkspace.Commit), (offer.Repository.ToString(), offer.Commit));
        Assert.DoesNotContain(github.Asked, u => u.AbsolutePath.Contains("/zipball/", StringComparison.Ordinal));

        var staged = await installer.StageFromSourceAsync(offer, tools, default, Long, new DirectProgress<SourceBuildPhase>(phases.Add));

        Assert.Equal([SourceBuildPhase.Source, SourceBuildPhase.Build, SourceBuildPhase.Check], phases);
        Assert.Equal(("mymodule", "0.3.0", "module"), (staged.Manifest.Id, staged.Manifest.Version, staged.Kind));
        Assert.Equal(
            (ModuleSource.GitHubSourceKind, "someone/sample", SourceBuildWorkspace.Commit, string.Empty),
            (staged.Source.Kind, staged.Source.Repository, staged.Source.Tag, staged.Source.Asset));
        Assert.Contains(github.Asked, u => u.AbsolutePath == $"/repos/someone/sample/zipball/{SourceBuildWorkspace.Commit}");
        Assert.Empty(Directory.GetDirectories(Path.Combine(user, ModuleInstaller.BuildDirectoryName)));

        Assert.True(installer.Confirm(staged.Token));
        var pending = Assert.Single(installer.Pending());
        Assert.Equal((ModuleSource.GitHubSourceKind, SourceBuildWorkspace.Commit), (pending.Source!.Kind, pending.Source.Tag));
    }
}
