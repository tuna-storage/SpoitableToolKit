using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace SpoitableToolKit.Tests.Licenses;

/// <summary>第三者の原文はパッケージの中にしか無いので、build が引いてこなければ出力のどこにも無い。</summary>
public sealed class LicenseOutputTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpoitableToolKit.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("repo の一番上が見つからない");
    }

    private static string HostOutput()
    {
        var configuration = typeof(LicenseOutputTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";
        var output = Path.Combine(RepoRoot(), "src", "SpoitableToolKit.Host", "bin", configuration, "net10.0-windows", "win-x64");
        Assert.True(File.Exists(Path.Combine(output, "SpoitableToolKit.Host.exe")), $"{output} に本体が無い（build してから回すこと）");
        return output;
    }

    [Theory]
    [InlineData("LICENSE")]
    [InlineData("THIRD-PARTY-NOTICES.md")]
    public void TheAppsLicenseFilesAreCopiedAsTheyAre(string name)
    {
        var shipped = Path.Combine(HostOutput(), name);

        Assert.True(File.Exists(shipped), $"出力に {name} が無い");
        Assert.Equal(File.ReadAllBytes(Path.Combine(RepoRoot(), name)), File.ReadAllBytes(shipped));
    }

    [Theory]
    [InlineData(@"licenses\CefSharp\LICENSE", "Copyright © The CefSharp Authors")]
    [InlineData(@"licenses\CEF\LICENSE.txt", "Marshall A. Greenblatt")]
    [InlineData(@"licenses\dotnet\THIRD-PARTY-NOTICES.TXT", ".NET Runtime uses third-party libraries")]
    public void ThePackagesLicenseTextsAreCopiedOut(string path, string expected)
    {
        var shipped = Path.Combine(HostOutput(), path);

        Assert.True(File.Exists(shipped), $"出力に {path} が無い");
        Assert.Contains(expected, File.ReadAllText(shipped));
    }

    [Fact]
    public void TheSdkPackageSaysItsLicenseAndNamesWhatItCarries()
    {
        var package = Assert.Single(Directory.GetFiles(Path.Combine(HostOutput(), "sdk"), "SpoitableToolKit.Sdk.*.nupkg"));
        using var zip = ZipFile.OpenRead(package);

        var nuspec = zip.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        using (var stream = nuspec.Open())
        {
            var license = XDocument.Load(stream).Descendants().Single(element => element.Name.LocalName == "license");
            Assert.Equal(("expression", "MIT"), (license.Attribute("type")?.Value, license.Value));
        }

        string Read(string name)
        {
            var entry = zip.GetEntry(name);
            Assert.True(entry is not null, $"SDK のパッケージに {name} が無い");
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }

        Assert.Equal(File.ReadAllText(Path.Combine(RepoRoot(), "LICENSE")), Read("LICENSE"));

        var notices = Read("THIRD-PARTY-NOTICES.md");
        string[] carried =
        [
            .. zip.Entries
                .Where(entry => entry.FullName.StartsWith("ref/", StringComparison.Ordinal) && entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(entry => Path.GetFileNameWithoutExtension(entry.Name))
                .Where(name => !name.StartsWith("SpoitableToolKit.", StringComparison.Ordinal)),
        ];
        Assert.NotEmpty(carried);
        Assert.All(carried, name => Assert.Contains("## " + name, notices));
        Assert.Contains("licenses/Microsoft.Extensions.Logging.Abstractions/THIRD-PARTY-NOTICES.TXT", zip.Entries.Select(entry => entry.FullName));
    }
}
