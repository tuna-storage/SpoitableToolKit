using System.IO.Compression;
using Xunit;

namespace SpoitableToolKit.Tests.Sdk;

/// <summary>
/// モジュールどうしで API を渡す見本を build した置き場: 契約（<c>Greetings.Contract</c>）・渡す側（<c>GreeterModule</c>、Id <c>greeter</c>）・
/// 使う側（<c>GreeterUser</c>、Id <c>greeter-user</c>）と、ライブラリ（<c>Clock.Library</c>、Id <c>clock-lib</c>）。
/// 見本は契約を ProjectReference で参照している。PackageReference でも同じ印が効くことを見るために、契約をパッケージにして置き場の feed に置き、
/// 参照の行だけを書き換えた写し（<c>PackagedProvider</c>・<c>PackagedUser</c>）も build する。印の無いもの・印が違うものの写しは、build が止まるはず。
/// </summary>
public sealed class SdkContractWorkspace : SdkSandbox
{
    private const string Contract = @"..\Greetings.Contract\Greetings.Contract.csproj";

    private const string LocalizedVersion = "9.9.9";

    public SdkBuild Provider { get; private set; } = null!;

    public SdkBuild User { get; private set; } = null!;

    public SdkBuild Library { get; private set; } = null!;

    public SdkBuild ContractPackage { get; private set; } = null!;

    public SdkBuild PackagedProvider { get; private set; } = null!;

    public SdkBuild PackagedUser { get; private set; } = null!;

    public SdkBuild UnmarkedUser { get; private set; } = null!;

    public SdkBuild BadMarkUser { get; private set; } = null!;

    /// <summary>サブフォルダのファイル（文化ごとの <c>de/…resources.dll</c>）を持つ契約のパッケージを渡そうとした結果（止まるはず）。</summary>
    public SdkBuild LocalizedProvider { get; private set; } = null!;

    /// <summary>SDK のほかも参照している契約の project を build した結果（止まるはず）。</summary>
    public SdkBuild DependentContract { get; private set; } = null!;

    public SdkContractWorkspace() => Setup(() =>
    {
        const string pack = "-t:SpoitablePackModule";
        const string sdk = """<PackageReference Include="SpoitableToolKit.Sdk" Version="$(SpoitableSdkVersionUnderTest)" />""";

        // ライブラリはほかと関わらないので、並べて走らせる。
        var library = Task.Run(() => Build("Clock.Library", restore: true, pack));
        Provider = Build("GreeterModule", restore: true, pack);
        User = Build("GreeterUser", restore: true, pack);
        ContractPackage = Pack("Greetings.Contract");
        Library = library.GetAwaiter().GetResult();

        Variant("GreeterModule", "PackagedProvider",
            $"""<ProjectReference Include="{Contract}" SpoitableContract="Provide" />""",
            """<PackageReference Include="Greetings.Contract" Version="1.0.0" SpoitableContract="Provide" />""");
        Variant("GreeterUser", "PackagedUser",
            $"""<ProjectReference Include="{Contract}" SpoitableContract="Use" />""",
            """<PackageReference Include="Greetings.Contract" Version="1.0.0" SpoitableContract="Use" />""");
        Variant("GreeterUser", "UnmarkedUser",
            $"""<ProjectReference Include="{Contract}" SpoitableContract="Use" />""",
            $"""<ProjectReference Include="{Contract}" />""");
        Variant("GreeterUser", "BadMarkUser",
            $"""<ProjectReference Include="{Contract}" SpoitableContract="Use" />""",
            $"""<ProjectReference Include="{Contract}" SpoitableContract="Maybe" />""");

        PackWithAFileInASubFolder();
        Variant("GreeterModule", "LocalizedProvider",
            $"""<ProjectReference Include="{Contract}" SpoitableContract="Provide" />""",
            $"""<PackageReference Include="Greetings.Contract" Version="{LocalizedVersion}" SpoitableContract="Provide" />""");
        Variant("Greetings.Contract", "DependentContract",
            sdk,
            sdk + """<ProjectReference Include="..\Clock.Library\Clock.Library.csproj" /><Reference Include="Some.Thing" />""");

        // パッケージを使う 3 つは契約の project に触らないので、並べて走らせる。
        // project を参照するものは、restore が参照先の project の obj にも書くので、順に（ライブラリの build はもう終わっている）。
        var packagedProvider = Task.Run(() => Build("PackagedProvider", restore: true, pack));
        var packagedUser = Task.Run(() => Build("PackagedUser", restore: true));
        var localizedProvider = Task.Run(() => Build("LocalizedProvider", restore: true));
        UnmarkedUser = Build("UnmarkedUser", restore: true);
        BadMarkUser = Build("BadMarkUser", restore: true);
        DependentContract = Build("DependentContract", restore: true);
        PackagedProvider = packagedProvider.GetAwaiter().GetResult();
        PackagedUser = packagedUser.GetAwaiter().GetResult();
        LocalizedProvider = localizedProvider.GetAwaiter().GetResult();
    });

    // 契約のパッケージ（1.0.0）に、サブフォルダのファイル（文化ごとの de/Greetings.Contract.resources.dll）を足した版を作る。中身は dll として読めなくてよい（build はコピーするだけ）。
    private void PackWithAFileInASubFolder()
    {
        var package = Path.Combine(Feed, $"Greetings.Contract.{LocalizedVersion}.nupkg");
        File.Copy(Path.Combine(Feed, "Greetings.Contract.1.0.0.nupkg"), package);

        using var archive = ZipFile.Open(package, ZipArchiveMode.Update);
        var nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        var name = nuspec.FullName;

        string text;
        using (var reader = new StreamReader(nuspec.Open())) text = reader.ReadToEnd();
        Assert.Contains("<version>1.0.0</version>", text);
        nuspec.Delete();

        using (var writer = new StreamWriter(archive.CreateEntry(name).Open()))
            writer.Write(text.Replace("<version>1.0.0</version>", $"<version>{LocalizedVersion}</version>"));

        using var satellite = archive.CreateEntry("lib/net10.0/de/Greetings.Contract.resources.dll").Open();
        satellite.Write("not a real dll"u8);
    }
}
