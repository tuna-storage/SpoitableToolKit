using System.IO.Compression;
using SpoitableToolKit.Host.Modules.Build;
using SpoitableToolKit.Tests.Sdk;
using Xunit;

namespace SpoitableToolKit.Tests.Modules.Build;

/// <summary>
/// ソースから build する試験の置き場。入れ元は置き場の <c>feed/</c> だけ（nuget.org へは出ない）。
/// <see cref="Rogue"/> はよその入れ元の代わりで、同じ名前の別の版のパッケージを置く。中の <c>build/</c> は動いたら build を止める。
/// </summary>
public sealed class SourceBuildWorkspace : SdkSandbox
{
    public const string Commit = "0123456789abcdef0123456789abcdef01234567";

    public const string RogueVersion = "0.0.1";

    public string Rogue => Path.Combine(Root, "rogue");

    public SourceBuildWorkspace() => Setup(() =>
    {
        Directory.CreateDirectory(Rogue);
        var real = Assert.Single(Directory.GetFiles(Feed, "SpoitableToolKit.Sdk.*.nupkg"));
        var package = Path.Combine(Rogue, $"SpoitableToolKit.Sdk.{RogueVersion}.nupkg");
        File.Copy(real, package);

        using var archive = ZipFile.Open(package, ZipArchiveMode.Update);

        var nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        var nuspecName = nuspec.FullName;
        string text;
        using (var reader = new StreamReader(nuspec.Open())) text = reader.ReadToEnd();
        Assert.Contains($"<version>{SdkVersion}</version>", text);
        nuspec.Delete();
        using (var writer = new StreamWriter(archive.CreateEntry(nuspecName).Open()))
            writer.Write(text.Replace($"<version>{SdkVersion}</version>", $"<version>{RogueVersion}</version>"));

        var targets = archive.Entries.Single(entry => entry.FullName == "build/SpoitableToolKit.Sdk.targets");
        targets.Delete();
        using (var writer = new StreamWriter(archive.CreateEntry("build/SpoitableToolKit.Sdk.targets").Open()))
            writer.Write("""<Project><Target Name="Rogue" BeforeTargets="CoreCompile"><Error Text="THE ROGUE SDK RAN" /></Target></Project>""");
    });

    internal ModuleBuildTools Tools(string? packageFeed = null) =>
        new(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", Feed, packageFeed);

    /// <summary>見本（<c>MyModule</c>）のソースを、落とした repo と同じ形（<c>s/</c> の中に一番上のフォルダ 1 つ）で作業場に置く。csproj には SDK の版を数字で書く。</summary>
    public string Tree(string name, Func<string, string>? project = null, Action<string>? more = null)
    {
        var work = Path.Combine(Root, name);
        var tree = Path.Combine(work, "s", "someone-sample-0123456");
        CopyDirectory(Path.Combine(Root, "MyModule"), tree);

        var csproj = Path.Combine(tree, "MyModule.csproj");
        var text = File.ReadAllText(csproj);
        Assert.Contains("$(SpoitableSdkVersionUnderTest)", text);
        text = text.Replace("$(SpoitableSdkVersionUnderTest)", SdkVersion);
        File.WriteAllText(csproj, project?.Invoke(text) ?? text);

        more?.Invoke(tree);
        return work;
    }

    public string AskForTheRogueVersion(string csproj)
    {
        var line = $"Version=\"{SdkVersion}\"";
        Assert.Contains(line, csproj);
        return csproj.Replace(line, $"Version=\"{RogueVersion}\"");
    }

    public static byte[] ZipOf(string work)
    {
        using var stream = new MemoryStream();
        ZipFile.CreateFromDirectory(Path.Combine(work, "s"), stream);
        return stream.ToArray();
    }

    /// <summary>フォルダを消す。子のプロセスが残っていれば消せない（少し間を置いて 5 回まで）。</summary>
    public static bool Delete(string directory)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(300);
            }
        }

        return false;
    }
}
