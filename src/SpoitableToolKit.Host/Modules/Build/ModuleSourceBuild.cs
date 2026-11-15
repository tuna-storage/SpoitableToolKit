using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Modules.Packaging;

namespace SpoitableToolKit.Host.Modules.Build;

/// <summary>
/// 落としたソースから、モジュールの zip を作る。<b>利用者の PC の上で、利用者の権限で、よその repo の build の手順が動く</b> —
/// restore が落とすパッケージ・MSBuild の target・source generator は、どれもコードを走らせる。囲いは作れないので、ここを呼ぶ前に
/// 利用者の同意を取る（呼ぶ側の仕事）。ここで守るのは次のこと。
/// <list type="bullet">
///   <item>shell を通さない（引数は 1 つずつ渡す）</item>
///   <item><b>SDK のパッケージは、アプリに同梱した置き場からしか取らせない。</b> その名前は nuget.org では誰でも取れるので、
///         csproj が古い版を書いていて、よそにちょうどその版があると、よその <c>build/</c> が動く。repo の <c>nuget.config</c> は読まない</item>
///   <item>出来た zip は、こちらが決めた置き場から取る（csproj は zip の置き場を動かせる）</item>
///   <item>パッケージの展開先は <b>build ごと</b>に作業場の中（<see cref="PackagesDirectoryName"/>）。利用者の <c>~/.nuget/packages</c> は使わない。
///         build どうしでも分けるので、並べて走らせても互いの展開先に触らず、前の build が書き換えた中身も次に残らない
///         （NuGet は、同じ版がもう展開してあれば、入れ元を見ずにそれを使う）</item>
///   <item>時間で打ち切り、止めるときは子のプロセスごと。build server も node も残さない（作業場のファイルを掴む）</item>
///   <item>build が言ったことを文に入れるときは、error の行だけを、字を絞って（repo が好きに書ける字）</item>
/// </list>
/// </summary>
internal sealed class ModuleSourceBuild(ILogger logger)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    /// <summary>本体のパッケージの名前の頭。この名前で始まるものは同梱の置き場からだけ取る。</summary>
    public const string SdkPackagePattern = "SpoitableToolKit.*";

    /// <summary>作業場の中の、パッケージの展開先の名前（path を短くするため 1 字）。</summary>
    public const string PackagesDirectoryName = "p";

    /// <summary>csproj を探す深さの上限と、読む csproj の大きさの上限。</summary>
    private const int MaxDepth = 12;
    private const long MaxProjectBytes = 1024 * 1024;

    /// <summary>覚えておく出力の行数（断る文とログ用。終わりの方だけ）と、1 行の長さの上限。</summary>
    private const int MaxLines = 400;
    private const int MaxLineLength = 2000;

    private static readonly Regex ErrorLine = new(@"(?<![\w.])error\s+[A-Za-z]+[0-9]+\s*:.*$", RegexOptions.CultureInvariant);
    private static readonly Regex ProjectSuffix = new(@"\s*\[[^\]]*\]\s*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// build する project を決める: <c>&lt;SpoitableModuleId&gt;</c> の書いてある csproj が<b>ちょうど 1 つ</b>。
    /// 0 個・2 個以上は、何を直すかを言って止める（勝手に選ぶと、別のものを入れうる）。<c>bin</c>・<c>obj</c>・<c>.</c> で始まるフォルダは見ない。
    /// </summary>
    public static string FindProject(string source)
    {
        List<string> found = [];
        Walk(new DirectoryInfo(source), 0, found);

        if (found.Count == 1) return found[0];

        if (found.Count == 0)
            throw new InvalidDataException(
                "no module project was found: the app builds the one .csproj that has <SpoitableModuleId> in it " +
                "(a repository that only holds a contract or other code cannot be installed)");

        var names = string.Join(", ", found
            .Select(project => ModuleManifest.Shown(Path.GetRelativePath(source, project).Replace('\\', '/')))
            .Order(StringComparer.Ordinal));
        throw new InvalidDataException(
            $"{found.Count} module projects were found ({names}); the app builds a repository that holds exactly one .csproj with <SpoitableModuleId>");
    }

    private static void Walk(DirectoryInfo directory, int depth, List<string> found)
    {
        foreach (var file in directory.EnumerateFiles("*.csproj"))
            if (NamesAModule(file)) found.Add(file.FullName);

        if (depth >= MaxDepth) return;

        foreach (var child in directory.EnumerateDirectories())
        {
            if (child.Name.StartsWith('.') || child.Name is "bin" or "obj" || child.LinkTarget is not null) continue;
            Walk(child, depth + 1, found);
        }
    }

    /// <summary>csproj に <c>SpoitableModuleId</c> の要素があるか。XML として読めないもの・大きすぎるものは数えない。</summary>
    private static bool NamesAModule(FileInfo project)
    {
        if (project.Length > MaxProjectBytes) return false;

        try
        {
            using var reader = XmlReader.Create(project.FullName,
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            return XDocument.Load(reader).Descendants().Any(element => element.Name.LocalName == "SpoitableModuleId");
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// build して、出来た zip の場所を返す。<paramref name="workspace"/> は作業場（ソースはこの下にある）。ここに設定と出力の置き場を作る。
    /// </summary>
    public async Task<string> BuildAsync(string project, string workspace, ModuleBuildTools tools, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var output = Path.Combine(workspace, "o");
        Directory.CreateDirectory(output);

        var config = Path.Combine(workspace, "nuget.config");
        File.WriteAllText(config, NuGetConfig(tools));
        WriteStoppers(workspace);

        var start = new ProcessStartInfo(tools.Dotnet)
        {
            WorkingDirectory = Path.GetDirectoryName(project)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        string[] arguments =
        [
            "build", project, "-c", "Release", "-t:SpoitablePackModule",
            Property("RestoreConfigFile", config),
            Property("SpoitableModulePackageDirectory", output + Path.DirectorySeparatorChar),
            "-p:UseSharedCompilation=false", "-nodeReuse:false", "--disable-build-servers", "-nologo", "-v:minimal",
        ];
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        Prepare(start.Environment, Path.Combine(workspace, PackagesDirectoryName));

        // project の場所は repo が好きに書ける字（フォルダと csproj の名前）なので、ログにも絞って出す。
        logger.LogInformation("Building {Project}", ModuleManifest.Shown(Path.GetRelativePath(workspace, project)));
        var lines = new Queue<string>();
        void Keep(string line)
        {
            lock (lines)
            {
                lines.Enqueue(line);
                if (lines.Count > MaxLines) lines.Dequeue();
            }
        }

        // 子のプロセスは、その子孫ごと入れ物（Job Object）に入れる。止めるとき・終わったとき・アプリが閉じたときに、全部が止まる。
        using var job = ProcessJob.TryCreate();
        using var process = new Process { StartInfo = start };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException($"The build could not be started ({tools.Dotnet}): {ex.Message}");
        }

        var contained = job?.TryAdd(process) ?? false;
        if (!contained) logger.LogWarning("The build could not be put in a job; if it has to be stopped, its children are stopped one by one");

        var reading = Task.WhenAll(ReadLinesAsync(process.StandardOutput, Keep), ReadLinesAsync(process.StandardError, Keep));

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Stop(process, contained ? job : null);
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidDataException($"the build did not finish in {Describe(timeout)} and was stopped");
        }

        // build が残した子（抜けたあとも動いているもの）を止める。出力の口も、ここで閉じる。
        if (contained) job!.Dispose();
        await Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None)).ConfigureAwait(false);

        string[] said;
        lock (lines) said = [.. lines];

        if (process.ExitCode != 0)
        {
            foreach (var line in said.TakeLast(40)) logger.LogWarning("build: {Line}", Line(line, 500));
            throw new InvalidDataException($"the build failed (dotnet build exited with {process.ExitCode}): {Errors(said)}");
        }

        var zips = Directory.GetFiles(output, "*.zip");
        if (zips.Length != 1)
            throw new InvalidDataException(
                $"the build finished but made {zips.Length} zip files where the app asked for the module's one; " +
                "the project has to use the SpoitableToolKit SDK's own SpoitablePackModule target");

        return zips[0];
    }

    /// <summary>
    /// build に渡す <c>nuget.config</c>。<b>これだけが読まれる</b>（<c>RestoreConfigFile</c>。repo や利用者の設定は読まれない）。
    /// <c>SpoitableToolKit.*</c> は同梱の置き場に、ほかは <see cref="ModuleBuildTools.PackageFeed"/> に結び付ける
    /// （結び付けが無いと、NuGet は入れ元を全部見て、書かれた版がちょうどある方を取る）。
    /// </summary>
    internal static string NuGetConfig(ModuleBuildTools tools)
    {
        var sources = new XElement("packageSources", new XElement("clear"), Source("spoitable-sdk", tools.SdkFeed));
        var mapping = new XElement("packageSourceMapping", new XElement("clear"), Mapping("spoitable-sdk", SdkPackagePattern));

        if (tools.PackageFeed is { } feed)
        {
            sources.Add(Source("packages", feed));
            mapping.Add(Mapping("packages", "*"));
        }

        return new XDocument(new XElement("configuration",
            sources,
            mapping,
            new XElement("disabledPackageSources", new XElement("clear")),
            new XElement("fallbackPackageFolders", new XElement("clear")))).ToString();

        static XElement Source(string key, string value) =>
            new("add", new XAttribute("key", key), new XAttribute("value", value));

        static XElement Mapping(string key, string pattern) =>
            new("packageSource", new XAttribute("key", key), new XElement("package", new XAttribute("pattern", pattern)));
    }

    /// <summary>
    /// 作業場の一番上に、上のフォルダを探しに行かせない印を置く。MSBuild と dotnet は、project から上へ辿って
    /// <c>Directory.Build.props</c>・<c>global.json</c> などを探す。repo が自分で持っていればそちらが近いので、そのまま使われる。
    /// </summary>
    private static void WriteStoppers(string workspace)
    {
        foreach (var name in (string[])["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"])
            File.WriteAllText(Path.Combine(workspace, name), "<Project />");

        File.WriteAllText(Path.Combine(workspace, "global.json"), "{}");
    }

    /// <summary>
    /// 子の <c>dotnet</c> に渡す環境。<c>MSBUILD*</c>: 親（<c>dotnet test</c> など）が子に渡す MSBuild の設定を継がせない。
    /// <c>NUGET_PACKAGES</c>: 展開先（環境にあると設定より勝つので、ここで指す）。文は英語（error の行を読む）。
    /// </summary>
    private static void Prepare(IDictionary<string, string?> environment, string packages)
    {
        foreach (var key in environment.Keys.Where(k => k.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase)).ToArray())
            environment.Remove(key);

        environment.Remove("NUGET_FALLBACK_PACKAGES");
        environment["NUGET_PACKAGES"] = packages;

        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        environment["DOTNET_NOLOGO"] = "1";
        environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
    }

    /// <summary>
    /// <c>-p:名前=値</c>。値の <c>%</c>・<c>;</c>・<c>,</c> は MSBuild が意味を持つ字として読む（<c>%XX</c> は字に戻し、<c>;</c> と <c>,</c> で区切る）ので、
    /// 書き換えて渡す。値は利用者のデータの置き場の下の path（利用者の名前が入る）。
    /// </summary>
    internal static string Property(string name, string value) =>
        "-p:" + name + "=" + value
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace(";", "%3B", StringComparison.Ordinal)
            .Replace(",", "%2C", StringComparison.Ordinal);

    /// <summary>
    /// 出力を 1 行ずつ読む。<b>1 行は <see cref="MaxLineLength"/> 字まで</b>（それより後ろは捨てる）— build は改行の無い長い出力を書けるので、
    /// 行ごと覚えると、いくらでも覚えさせられる。
    /// </summary>
    internal static async Task ReadLinesAsync(StreamReader reader, Action<string> keep)
    {
        var buffer = new char[4096];
        var line = new StringBuilder();
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                for (var i = 0; i < read; i++)
                {
                    var c = buffer[i];
                    if (c == '\n')
                    {
                        keep(line.ToString().TrimEnd('\r'));
                        line.Clear();
                    }
                    else if (line.Length < MaxLineLength)
                    {
                        line.Append(c);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // 口が閉じた（プロセスを止めた）。読めた所まで。
        }

        if (line.Length > 0) keep(line.ToString());
    }

    /// <summary>
    /// 止める。入れ物（Job Object）に入っていれば、<b>入れ物ごと</b>（親が先に抜けていても、孫まで止まる）。
    /// 入れられなかったときだけ、親から辿って 1 つずつ。
    /// </summary>
    private static void Stop(Process process, ProcessJob? job)
    {
        try
        {
            if (job is not null) job.Dispose();
            else process.Kill(entireProcessTree: true);

            process.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or AggregateException)
        {
            // もう抜けている（辿って止めるとき、止められなかった子があれば AggregateException）。
        }
    }

    /// <summary>
    /// build が言った error（頭の 5 つ）。行の頭のパスと、終わりの <c>[project]</c> は落とす。error の行が無ければ、終わりの 3 行。
    /// </summary>
    internal static string Errors(IReadOnlyList<string> said)
    {
        var errors = said
            .Select(line => ErrorLine.Match(line))
            .Where(match => match.Success)
            .Select(match => Line(ProjectSuffix.Replace(match.Value, string.Empty), 300))
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToArray();

        if (errors.Length > 0) return string.Join(" / ", errors);

        var last = said.Where(line => line.Trim().Length > 0).TakeLast(3).Select(line => Line(line.Trim(), 300)).ToArray();
        return last.Length > 0 ? string.Join(" / ", last) : "the build said nothing";
    }

    /// <summary>外から来た 1 行を、文に入れられる形にする（制御文字と書式の字は <c>?</c>、長さに上限）。</summary>
    private static string Line(string text, int max)
    {
        var cut = text.Length > max ? text[..(char.IsHighSurrogate(text[max - 1]) ? max - 1 : max)] + "…" : text;
        return Regex.Replace(cut, ModuleManifest.NameForbiddenPattern, "?", RegexOptions.CultureInvariant);
    }

    private static string Describe(TimeSpan time) =>
        time.TotalMinutes >= 1 ? $"{time.TotalMinutes:0} minutes" : $"{time.TotalSeconds:0} seconds";
}
