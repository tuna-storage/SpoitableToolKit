using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace SpoitableToolKit.Tests.Licenses;

/// <summary>
/// 版は先頭の数（major）だけを比べる。本体と hrs は <c>10.*</c> で引くので、restore し直すだけで 10.0.x の末尾が動く。
/// 末尾まで比べると、ソースを変えていないのに新しい修正版が出た日から落ちる。
/// </summary>
public sealed class ThirdPartyNoticesTests
{
    /// <summary>
    /// self-contained の publish で restore し直すと、assets の <c>downloadDependencies</c> に入る .NET の runtime pack。
    /// 一覧では「.NET」の節が持つ（パッケージの行には書かない）。
    /// </summary>
    private static readonly string[] DotnetPackPrefixes = ["Microsoft.NETCore.App.", "Microsoft.WindowsDesktop.App.", "Microsoft.AspNetCore.App."];

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpoitableToolKit.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("repo の一番上が見つからない");
    }

    private static string[] ShippedAssets(string root)
    {
        var modules = Directory.GetDirectories(Path.Combine(root, "src", "SpoitableToolKit.Modules"))
            .Where(directory => Directory.EnumerateFiles(directory, "*.csproj").Any());

        string[] projects = [Path.Combine(root, "src", "SpoitableToolKit.Host"), .. modules];
        return [.. projects.Select(project => Path.Combine(project, "obj", "project.assets.json"))];
    }

    private static List<(string Id, string Version)> PackagesIn(string assets)
    {
        Assert.True(File.Exists(assets), $"{assets} が無い（build してから回すこと）");

        using var document = JsonDocument.Parse(File.ReadAllText(assets));
        List<(string, string)> packages = [];

        foreach (var library in document.RootElement.GetProperty("libraries").EnumerateObject())
        {
            if (library.Value.GetProperty("type").GetString() != "package") continue;
            var slash = library.Name.IndexOf('/');
            packages.Add((library.Name[..slash], library.Name[(slash + 1)..]));
        }

        foreach (var framework in document.RootElement.GetProperty("project").GetProperty("frameworks").EnumerateObject())
        {
            if (!framework.Value.TryGetProperty("downloadDependencies", out var downloads)) continue;
            foreach (var download in downloads.EnumerateArray())
            {
                var id = download.GetProperty("name").GetString()!;
                if (DotnetPackPrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) continue;

                // "[10.0.19041.57, 10.0.19041.57]" の形。
                var version = download.GetProperty("version").GetString()!.Trim('[', ']', '(', ')').Split(',')[0].Trim();
                packages.Add((id, version));
            }
        }

        return packages;
    }

    private static List<(string Id, string Version)> Shipped()
    {
        var root = RepoRoot();
        return [.. ShippedAssets(root).SelectMany(PackagesIn).Distinct()];
    }

    private static string Notices() => File.ReadAllText(Path.Combine(RepoRoot(), "THIRD-PARTY-NOTICES.md"));

    private static List<(string Id, string Version)> Listed(string notices) =>
    [
        .. Regex.Matches(notices, @"^- NuGet: (.+?)\r?$", RegexOptions.Multiline)
            .SelectMany(match => match.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries))
            .Select(entry =>
            {
                var parts = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                Assert.True(parts.Length == 2, $"「{entry}」は「Id 版」の形でない");
                return (parts[0], parts[1]);
            }),
    ];

    private static string Major(string version) => version.Split('.', '-')[0];

    [Fact]
    public void EveryPackageTheAppShipsIsListed()
    {
        var shipped = Shipped();
        var listed = Listed(Notices()).Select(package => package.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 数え忘れの保険。assets を読み違えていたら、何も見ていない。
        Assert.Contains(shipped, package => package.Id == "CefSharp.Common.NETCore");
        Assert.Contains(shipped, package => package.Id == "Microsoft.Windows.SDK.NET.Ref");

        string[] missing = [.. shipped.Where(package => !listed.Contains(package.Id)).Select(package => $"{package.Id} {package.Version}").Distinct().Order()];
        Assert.True(missing.Length == 0,
            "THIRD-PARTY-NOTICES.md に載っていないパッケージ: " + string.Join(", ", missing) +
            "。ライセンス・著作権の行と一緒に足すこと（docs/third-party-notices.md）。");
    }

    [Fact]
    public void NoPackageIsListedThatTheAppNoLongerShips()
    {
        var shipped = Shipped().Select(package => package.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var listed = Listed(Notices());

        Assert.NotEmpty(listed);

        string[] stale = [.. listed.Where(package => !shipped.Contains(package.Id)).Select(package => $"{package.Id} {package.Version}").Order()];
        Assert.True(stale.Length == 0,
            "THIRD-PARTY-NOTICES.md に、もう出力に入らないパッケージが載っている: " + string.Join(", ", stale) + "。その行（使っていなければ節ごと）を消すこと。");
    }

    /// <summary>1 つのパッケージが project ごとに違う版で入るなら、一覧にもその版を全部書く。</summary>
    [Fact]
    public void ListedVersionsHaveTheMajorVersionsTheAppShips()
    {
        static string Key((string Id, string Version) package) => package.Id.ToLowerInvariant() + " " + Major(package.Version);

        var shipped = Shipped();
        var listed = Listed(Notices()).Where(package => shipped.Any(s => s.Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase))).ToList();
        var shippedKeys = shipped.Select(Key).ToHashSet();
        var listedKeys = listed.Select(Key).ToHashSet();

        string[] differ =
        [
            .. shipped.Where(package => !listedKeys.Contains(Key(package))).Select(package => $"出力 {package.Id} {package.Version}")
                .Concat(listed.Where(package => !shippedKeys.Contains(Key(package))).Select(package => $"一覧 {package.Id} {package.Version}"))
                .Distinct()
                .Order(),
        ];

        Assert.True(differ.Length == 0, "版の先頭の数が一覧と食い違っている: " + string.Join(", ", differ) + "。一覧の版を出力に入る版に直すこと。");
    }

    /// <summary>パッケージと同じ名前で始まるもの（CefSharp の CefSharp.*）は、その節が持つ。</summary>
    [Fact]
    public void EveryNativeFileThePackagesPutInTheOutputIsNamed()
    {
        var notices = Notices();
        List<(string Package, string File)> files = [];

        foreach (var assets in ShippedAssets(RepoRoot()))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(assets));
            foreach (var target in document.RootElement.GetProperty("targets").EnumerateObject())
            foreach (var library in target.Value.EnumerateObject())
            {
                if (library.Value.GetProperty("type").GetString() != "package") continue;
                var id = library.Name[..library.Name.IndexOf('/')];

                foreach (var group in new[] { "native", "runtimeTargets", "contentFiles" })
                {
                    if (!library.Value.TryGetProperty(group, out var entries)) continue;
                    foreach (var entry in entries.EnumerateObject())
                    {
                        var name = Path.GetFileName(entry.Name);
                        if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                        if (name.StartsWith(id.Split('.')[0] + ".", StringComparison.OrdinalIgnoreCase)) continue;
                        files.Add((id, name));
                    }
                }
            }
        }

        // 数え忘れの保険。assets の読み方を間違えていたら、何も見ていない。
        Assert.Contains(files, file => file.File == "libcef.dll");
        Assert.Contains(files, file => file.File == "d3dcompiler_47.dll");

        string[] unnamed =
        [
            .. files.Where(file => !notices.Contains(file.File, StringComparison.OrdinalIgnoreCase))
                .Select(file => $"{file.File}（{file.Package}）").Distinct().Order(),
        ];
        Assert.True(unnamed.Length == 0,
            "THIRD-PARTY-NOTICES.md に名前の無い、パッケージが出力に置く dll・exe: " + string.Join(", ", unnamed) +
            "。どの節のもので、どのライセンスかを書くこと（chrome://credits にあるか・Windows SDK の REDIST の一覧にあるか。docs/third-party-notices.md）。");
    }

    [Fact]
    public void EveryListedEntryNamesItsLicenseAndCopyright()
    {
        var entries = Regex.Split(Notices().Replace("\r\n", "\n"), @"^### ", RegexOptions.Multiline).Skip(1).ToArray();
        var withPackages = entries.Where(entry => entry.Contains("\n- NuGet: ", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(withPackages);
        Assert.All(withPackages, entry =>
        {
            var name = entry[..entry.IndexOf('\n')];
            Assert.True(entry.Contains("\n- License: ", StringComparison.Ordinal), $"{name} にライセンスの行が無い");
            Assert.True(entry.Contains("\n- Copyright: ", StringComparison.Ordinal), $"{name} に著作権の行が無い");
        });
    }
}
