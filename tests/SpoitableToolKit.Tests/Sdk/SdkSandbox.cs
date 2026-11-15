using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Config;
using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Localization;
using SpoitableToolKit.Host.ExternalModules;
using SpoitableToolKit.Host.Modules;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Push;
using SpoitableToolKit.Tests.Fakes;
using Xunit;

namespace SpoitableToolKit.Tests.Sdk;

/// <summary>
/// repo の外に作る置き場。パッケージ <c>SpoitableToolKit.Sdk</c>（この project の build が出力の <c>sdk/</c> に作ったもの）だけを入れ元にして、
/// 見本（<c>tests/fixtures/sdk-samples</c>）を build する。外の repo でモジュールを作るのと同じ形。
/// <list type="bullet">
///   <item>nuget.org へは出ない。入れ元は置き場の <c>feed/</c> だけ（<c>nuget.config</c> で <c>&lt;clear/&gt;</c>）</item>
///   <item>パッケージは置き場の <c>packages/</c> に展開する。利用者の <c>~/.nuget/packages</c> に入れると、同じ版の古い中身が次の回に使われる</item>
///   <item>build server も node も残さない（残ると置き場のファイルを掴む）</item>
///   <item>repo の <c>Directory.Build.props</c> は継がない（置き場に自分のものを置く）</item>
/// </list>
/// </summary>
public abstract class SdkSandbox : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "stk-tests", Guid.NewGuid().ToString("N"));

    public string SdkVersion { get; }

    public string Feed => Path.Combine(Root, "feed");

    protected SdkSandbox()
    {
        try
        {
            SdkVersion = Prepare();
        }
        catch
        {
            // 作る途中で投げると、xUnit は Dispose を呼ばない。置き場を残さない。
            Dispose();
            throw;
        }
    }

    // 投げたら置き場を消してから投げ直す。fixture のコンストラクタが投げると xUnit は Dispose を呼ばず、%TEMP%\stk-tests に残る。
    protected void Setup(Action setup)
    {
        try
        {
            setup();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private string Prepare()
    {
        var package = Assert.Single(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "sdk"), "SpoitableToolKit.Sdk.*.nupkg"));
        var version = Path.GetFileNameWithoutExtension(package)["SpoitableToolKit.Sdk.".Length..];

        Directory.CreateDirectory(Feed);
        File.Copy(package, Path.Combine(Feed, Path.GetFileName(package)));

        File.WriteAllText(Path.Combine(Root, "nuget.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="feed" />
              </packageSources>
              <fallbackPackageFolders>
                <clear />
              </fallbackPackageFolders>
              <config>
                <add key="globalPackagesFolder" value="packages" />
              </config>
            </configuration>
            """);
        File.WriteAllText(Path.Combine(Root, "Directory.Build.props"),
            $"<Project><PropertyGroup><SpoitableSdkVersionUnderTest>{version}</SpoitableSdkVersionUnderTest></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(Root, "Directory.Build.targets"), "<Project />");

        CopyDirectory(Path.Combine(RepoRoot(), "tests", "fixtures", "sdk-samples"), Root);
        return version;
    }

    public void Dispose()
    {
        // 子の dotnet が抜けた直後は、まだファイルを掴んでいることがある。少し間を置いてやり直す。
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
                return;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            Thread.Sleep(200);
        }
    }

    public string OutputOf(string project) => Path.Combine(Root, project, "bin", "Release", "net10.0");

    public string ZipOf(string project, string id, string version) => Path.Combine(Root, project, "bin", "module", $"{id}-{version}-module.zip");

    public static IReadOnlyList<string> FilesUnder(string directory) =>
        Directory.Exists(directory)
            ? [.. Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)]
            : [];

    public static IReadOnlyList<string> EntriesOf(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        return [.. archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal)];
    }

    /// <summary><c>bin</c>・<c>obj</c> は写さない。</summary>
    protected static void CopyDirectory(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")) continue;

            var copy = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
    }

    protected void Variant(string sample, string name, string line, string replacement)
    {
        CopyDirectory(Path.Combine(Root, sample), Path.Combine(Root, name));

        var project = Path.Combine(Root, name, sample + ".csproj");
        var text = File.ReadAllText(project);
        Assert.Contains(line, text);
        File.WriteAllText(project, text.Replace(line, replacement));
    }

    public SdkBuild Build(string project, bool restore, params string[] arguments) =>
        Run(project, ["build", "-c", "Release", .. restore ? (string[])[] : ["--no-restore"], .. arguments]);

    public SdkBuild Pack(string project) =>
        Run(project, ["pack", "-c", "Release", "--no-build", "-o", Feed]);

    private SdkBuild Run(string project, string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = Path.Combine(Root, project),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        string[] quiet = ["-nologo", "-v:q", "--disable-build-servers", "-nodeReuse:false", "-p:UseSharedCompilation=false"];
        foreach (var argument in quiet) start.ArgumentList.Add(argument);

        Isolate(start.Environment, Root);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet を起動できなかった");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"dotnet {arguments[0]} {project} が 5 分で終わらなかった");
        }

        return new SdkBuild(process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }

    // 子の dotnet に渡す環境を、置き場の中だけで済むように整える。
    // MSBUILD*: 試験を走らせている dotnet test が子に渡した MSBuild の設定を、外の build に継がせない。
    // NUGET_PACKAGES: 環境にあると nuget.config の globalPackagesFolder より勝ち、パッケージが利用者の置き場に展開される（版が同じなので、次の回に古い中身が使われる）。置き場の packages/ を指し直す。
    // NUGET_FALLBACK_PACKAGES: 置き場の外のパッケージを使わせない。
    internal static void Isolate(IDictionary<string, string?> environment, string root)
    {
        foreach (var key in environment.Keys.Where(k => k.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase)).ToArray())
            environment.Remove(key);

        environment.Remove("NUGET_FALLBACK_PACKAGES");
        environment["NUGET_PACKAGES"] = Path.Combine(root, "packages");

        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        environment["DOTNET_NOLOGO"] = "1";
        environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpoitableToolKit.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("repo の一番上が見つからない");
    }

    internal static ModuleManager NewManager(string root, IReadOnlyList<string> roots, Localizer? text = null, ILoggerFactory? logs = null)
    {
        var events = new HostEvents(NullLoggerFactory.Instance);
        return new ModuleManager(
            new NoServices(),
            new FakeVrcLogBus(),
            new FakeVrcLogStore(),
            new FakeExternalToolProvider(),
            new FakeNotifications(),
            new FakeClipboard(),
            new FakeOscBus(),
            events,
            new UiBroadcast(),
            new ModuleConfigStore(events.Hubs, NullLoggerFactory.Instance),
            new ExternalModuleRegistry(NullLoggerFactory.Instance),
            text ?? new Localizer(NullLogger<Localizer>.Instance),
            new HostConfig(),
            logs ?? NullLoggerFactory.Instance,
            roots,
            Path.Combine(root, "module-data"),
            () => false)
        {
            Vr = new(),
        };
    }

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
