using System.IO.Compression;
using System.Xml.Linq;
using SpoitableToolKit.Host.Modules.Packaging;
using Xunit;

namespace SpoitableToolKit.Tests.Sdk;

/// <summary>baseline/ と MinimumSdk が食い違っていないか。MinimumSdk を上げるのは SDK の形を壊したときだけで、壊したら比べる相手も一緒に替える。</summary>
public sealed class SdkBaselineTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpoitableToolKit.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("repo の一番上が見つからない");
    }

    [Fact]
    public void TheBaselineIsTheOldestSdkTheAppLoads()
    {
        var baseline = Path.Combine(RepoRoot(), "src", "SpoitableToolKit.Sdk", "baseline");
        var packages = Directory.GetFiles(baseline, "*.nupkg");

        var package = Assert.Single(packages);
        using var zip = ZipFile.OpenRead(package);
        var nuspec = zip.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        using var stream = nuspec.Open();
        var metadata = XDocument.Load(stream).Root!.Elements().Single(e => e.Name.LocalName == "metadata");
        var id = metadata.Elements().Single(e => e.Name.LocalName == "id").Value;
        var version = Version.Parse(metadata.Elements().Single(e => e.Name.LocalName == "version").Value);

        Assert.Equal("SpoitableToolKit.Sdk", id);
        Assert.True(
            version.Major == ModuleManifest.MinimumSdk.Major && version.Minor == ModuleManifest.MinimumSdk.Minor
            && Math.Max(version.Build, 0) == ModuleManifest.MinimumSdk.Build,
            $"The SDK baseline is {version} but ModuleManifest.MinimumSdk is {ModuleManifest.MinimumSdk.ToString(3)}. " +
            "Change both together (src/SpoitableToolKit.Sdk/baseline/README.md).");
        Assert.True(ModuleManifest.MinimumSdk <= ModuleManifest.AppSdk);
    }

    /// <summary>抑えてよいのは PKV004 だけ（lib/ を持たないのはわざと）。壊した所を抑えると、MinimumSdk を上げずに壊せてしまう。</summary>
    [Fact]
    public void OnlyTheMissingRuntimeAssetIsSuppressed()
    {
        var file = Path.Combine(RepoRoot(), "src", "SpoitableToolKit.Abstractions", "CompatibilitySuppressions.xml");

        var ids = XDocument.Load(file).Descendants().Where(e => e.Name.LocalName == "DiagnosticId").Select(e => e.Value).ToArray();

        Assert.Equal(["PKV004"], ids);
    }
}
