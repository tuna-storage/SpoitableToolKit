using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.Playwright;
using SpoitableToolKit.E2E.Modules;
using Xunit;

namespace SpoitableToolKit.E2E;

/// <summary>本物の Host.exe を起動し、CEF の CDP へ Playwright を繋ぐ。置き場と CDP のポートは起動ごとに別なので、並べてもぶつからない。</summary>
public class AppFixture : IAsyncLifetime
{
    /// <summary>スレッドを先に用意する（並べて起動すると、足りない間は 1 秒に 1 本ずつしか増えず、アプリが 1 秒おきにしか上がらない）。</summary>
    static AppFixture()
    {
        ThreadPool.SetMinThreads(64, 64);
    }

    private Process? _process;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public IPage Page { get; private set; } = null!;

    /// <summary>繋いだあとにアプリが立てた別の窓のページは、繋いだままの Playwright に出てこないことがある。それを触る試験はここへ繋ぎ直す。</summary>
    public string CdpEndpoint { get; private set; } = "";

    /// <summary>
    /// 設定の画面を開いて、左の一覧からページを選ぶ。ページは画面を離れても覚えているので、開くだけだと前の試験が開いたページのままになる。
    /// </summary>
    /// <param name="page">本体のカテゴリ（<c>display</c>・<c>log</c>・<c>notify</c>・<c>osc</c>・<c>vr</c>・<c>modules</c>・<c>updates</c>）かモジュールの Id。</param>
    public async Task OpenSettings(string page = "display")
    {
        await Page.Locator("#sidebar .item[data-id='settings']").ClickAsync();
        await SettingsPage(page).ClickAsync();
    }

    /// <summary>左の一覧の、そのページの行（<see cref="OpenSettings"/> と同じ名前で引く）。本体のカテゴリは <c>host:</c> を前に付けた名前で持っている。</summary>
    public ILocator SettingsPage(string page) =>
        Page.Locator($"#cfgNav .cfg-nav-item[data-page='host:{page}'], #cfgNav .cfg-nav-item[data-page='{page}']");

    public IReadOnlyCollection<string> Output => _output;

    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _output = new();

    private void Capture(string? line)
    {
        if (line is null) return;
        _output.Enqueue(line);
        while (_output.Count > 500) _output.TryDequeue(out _);
    }

    private string _logFile => Path.Combine(DataDirectory, "vrclogs", "output_log_2026-08-27_00-00-00.txt");

    /// <summary>本物の VRChat.exe は見ない（手元で VRChat を起動・終了しただけで、走っている試験のタイトルバー・札・通知が変わるため）。既定は動いている。</summary>
    private string _vrchatFlag => Path.Combine(DataDirectory, "vrchat-running");

    /// <summary>どのクラスのアプリも本物の GitHub へは出ない。何も置かなければ、どの repo も 404。</summary>
    public FakeGitHubServer GitHub { get; } = new();

    public static byte[] SampleModuleZip(string version) => FakeGitHubServer.Zip(SampleModuleFiles(version));

    private static (string Name, byte[] Content)[] SampleModuleFiles(string version)
    {
        var output = Path.GetFullPath(Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "SampleModuleOutput")?.Value
            ?? throw new InvalidOperationException("SampleModuleOutput が埋め込まれていない"));
        var manifest = $"{{ \"id\": \"sample\", \"name\": \"Sample\", \"version\": \"{version}\" }}";

        return
        [
            ("module.json", System.Text.Encoding.UTF8.GetBytes(manifest)),
            ("SampleModule.dll", File.ReadAllBytes(Path.Combine(output, "SampleModule.dll"))),
            ("SampleModule.deps.json", File.ReadAllBytes(Path.Combine(output, "SampleModule.deps.json"))),
        ];
    }

    /// <summary>外から入れる道は通さない（それは ModuleInstallTests が見る）。</summary>
    protected void PlaceSampleModule()
    {
        var module = Path.Combine(DataDirectory, "modules", "sample");
        Directory.CreateDirectory(module);
        foreach (var (name, content) in SampleModuleFiles("1.0.0")) File.WriteAllBytes(Path.Combine(module, name), content);
    }

    protected virtual void Prepare()
    {
    }

    /// <summary>csproj の SDK の版は、アプリに同梱の版（sdk/ の nupkg。アプリが build に使うのはそれだけ）を数字で書く。</summary>
    public static byte[] SdkSampleSourceZip(string sample, string top)
    {
        var samples = Path.GetFullPath(Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "SdkSamples")?.Value
            ?? throw new InvalidOperationException("SdkSamples が埋め込まれていない"));
        var root = Path.Combine(samples, sample);

        var package = Directory.GetFiles(Path.Combine(HostDirectory, "sdk"), "SpoitableToolKit.Sdk.*.nupkg").Single();
        var version = Path.GetFileNameWithoutExtension(package)["SpoitableToolKit.Sdk.".Length..];
        const string placeholder = "$(SpoitableSdkVersionUnderTest)";

        List<(string Name, byte[] Content)> entries = [];
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Split('/').Any(part => part is "bin" or "obj")) continue;

            var content = File.ReadAllBytes(file);
            if (relative.EndsWith(".csproj", StringComparison.Ordinal))
            {
                var text = File.ReadAllText(file);
                if (!text.Contains(placeholder, StringComparison.Ordinal))
                    throw new InvalidOperationException($"{file} に {placeholder} が無い（見本の書き方が変わった）");
                content = System.Text.Encoding.UTF8.GetBytes(text.Replace(placeholder, version));
            }

            entries.Add(($"{top}/{relative}", content));
        }

        return FakeGitHubServer.Zip([.. entries]);
    }

    /// <summary>守りが効かなかったときに備えて、外へ出ない・VR を出さない・クリップボードに触らない・本物の VRChat のログを読まないは 1 つ目と同じにする。終わらなかったら、試験の側で必ず止める。</summary>
    public Process StartAnotherWithTheSameDataFolder()
    {
        var executable = HostExecutable();
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["SPOITABLE_DATA_DIR"] = DataDirectory;
        startInfo.Environment["SPOITABLE_UI_OFFSCREEN"] = "1";
        startInfo.Environment["SPOITABLE_VRCLOG_DIR"] = Path.GetDirectoryName(_logFile)!;
        startInfo.Environment["SPOITABLE_NO_VR"] = "1";
        startInfo.Environment["SPOITABLE_CLIPBOARD"] = "memory";
        startInfo.Environment["SPOITABLE_VRCHAT_FLAG"] = _vrchatFlag;
        startInfo.Environment["SPOITABLE_RESTART_RECORD"] = RestartRecord;
        startInfo.Environment["SPOITABLE_GITHUB_API"] = GitHub.Url;
        startInfo.Environment["SPOITABLE_DOTNET_RELEASES"] = GitHub.DotnetReleases;

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"起動できなかった: {executable}");
        process.OutputDataReceived += (_, e) => Capture(e.Data is null ? null : "[second] " + e.Data);
        process.ErrorDataReceived += (_, e) => Capture(e.Data is null ? null : "[second] " + e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    public string RestartRecord => Path.Combine(DataDirectory, "restart-args.txt");

    public int ProcessId => _process?.Id ?? throw new InvalidOperationException("the app is not running");

    /// <summary>アプリが気付くのは次の見張り（2 秒おき）。</summary>
    public void SetVRChatRunning(bool running)
    {
        if (running) File.WriteAllText(_vrchatFlag, "");
        else File.Delete(_vrchatFlag);
    }

    public void AppendLog(params string[] bodies)
    {
        var stamp = DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss");
        File.AppendAllLines(_logFile, bodies.Select(body => $"{stamp} Debug      -  {body}"));
    }

    /// <summary>スキーマは写さない（写すと、本物が変わったときに試験だけが古いまま静かに通る）。記録が 0 件だと hrs のパネルは書き出しボタンを押させない。</summary>
    public void SeedHeartRates(int count)
    {
        var database = Path.Combine(DataDirectory, "module-data", "hrs", "records.db");

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database,

            // 作らない。無いなら「アプリがまだ作っていない」なので、黙って空の DB を置くより落ちた方がいい。
            Mode = SqliteOpenMode.ReadWrite,

            // pool に残さない（残すと、試験のプロセスが閉じるまで records.db を掴んだままになり、片付けで置き場を消せない）。
            Pooling = false,
        }.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO heart_rate_samples (at, bpm) VALUES ($at, $bpm)";
        var at = command.Parameters.Add("$at", SqliteType.Integer);
        var bpm = command.Parameters.Add("$bpm", SqliteType.Integer);

        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < count; i++)
        {
            at.Value = now.AddSeconds(-i).ToUnixTimeMilliseconds();
            bpm.Value = 70 + i;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>SeedHeartRates は「いま」から遡って積むので、ほかの試験と同じ日に混ざる。こちらは過去の決まった日に置く。</summary>
    public void SeedHeartRatesAt(params (DateTimeOffset At, int Bpm)[] rows)
    {
        var database = Path.Combine(DataDirectory, "module-data", "hrs", "records.db");

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO heart_rate_samples (at, bpm) VALUES ($at, $bpm)";
        var at = command.Parameters.Add("$at", SqliteType.Integer);
        var bpm = command.Parameters.Add("$bpm", SqliteType.Integer);

        foreach (var row in rows)
        {
            at.Value = row.At.ToUnixTimeMilliseconds();
            bpm.Value = row.Bpm;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>居ないアドレスを指して、開発機の近くのベルトの GATT を取り合わない（空に戻す試験の後片付けも、これに戻す）。</summary>
    public const string NoHeartRateMonitor = "E2E [00:00:00:00:00:01]";

    public string DataDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "stk-e2e", Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(DataDirectory);

        // わざと読み込めないモジュールを 1 つ置く（探索には引っかかって読み込みで落ちる）。画面より先に走るので、バナーが getStatus で追いつけるかもここで通る。
        var broken = Path.Combine(DataDirectory, "modules", "broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "broken.dll"), "これは dll ではない");

        // 既定は OS の表示言語なので、固定しないと英語の Windows で文言を見る試験が落ちる。
        File.WriteAllText(Path.Combine(DataDirectory, "config.json"), @"{ ""Language"": ""Ja"" }");

        // 指定が空だと最初に見つかった心拍計に繋ぎ、本物のアプリと GATT を取り合う。居ないアドレスを指せば、スキャンはしても繋ぎには行かない。
        var hrs = Path.Combine(DataDirectory, "module-data", "hrs");
        Directory.CreateDirectory(hrs);
        File.WriteAllText(Path.Combine(hrs, "config.json"), $"{{ \"Device\": \"{NoHeartRateMonitor}\" }}");

        Prepare();
        await StartAsync();
    }

    /// <summary>kill で落とすので、まだキューに居るものは消える。残したいものは記録に入ったのを確かめてから呼ぶ。</summary>
    public async Task RestartAsync()
    {
        await StopAsync();
        await StartAsync();
    }

    /// <summary>CDP の先が自分の起動したアプリでなければ、番号を替えて起動し直す。</summary>
    private async Task StartAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await LaunchAsync();
                return;
            }
            catch (PortTakenException ex) when (attempt < 3)
            {
                Capture($"[e2e] {ex.Message}。番号を替えて起動し直す（{attempt} 回目）");
                await StopAsync();
            }
        }
    }

    private async Task LaunchAsync()
    {
        // 前の起動の「Tailing」を拾って、tail が始まる前に進まないようにする。
        _output.Clear();

        var port = FreePort();
        var executable = HostExecutable();

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["SPOITABLE_DATA_DIR"] = DataDirectory;
        startInfo.Environment["SPOITABLE_DEBUG_PORT"] = port.ToString();

        // 窓を画面の外へ（実画面に出すと、走っている間の人の操作で落ちる）。
        startInfo.Environment["SPOITABLE_UI_OFFSCREEN"] = "1";

        // 実際の VRChat のログを読ませない。
        Directory.CreateDirectory(Path.GetDirectoryName(_logFile)!);
        File.WriteAllText(_logFile, string.Empty);
        startInfo.Environment["SPOITABLE_VRCLOG_DIR"] = Path.GetDirectoryName(_logFile)!;

        // 試験の最中にヘッドセットへ板を出さない（SteamVR が起動していると実際に出てしまう）。
        startInfo.Environment["SPOITABLE_NO_VR"] = "1";

        // Windows のクリップボードに触らない（手元のコピーと取り合って貼り付けの試験が落ち、手元の履歴も荒らす）。
        startInfo.Environment["SPOITABLE_CLIPBOARD"] = "memory";

        // 本物の VRChat を見ない（印のファイルで決める）。
        SetVRChatRunning(true);
        startInfo.Environment["SPOITABLE_VRCHAT_FLAG"] = _vrchatFlag;

        // 管理画面の「再起動」で本当には起動し直さない（試験の知らないアプリが残る）。
        startInfo.Environment["SPOITABLE_RESTART_RECORD"] = RestartRecord;

        // 本物の GitHub へ出さない。
        startInfo.Environment["SPOITABLE_GITHUB_API"] = GitHub.Url;

        // 本物の .NET の配布元へも出さない（偽物は PublishDotnetSdk するまで SDK の一覧に 404 を返す）。
        startInfo.Environment["SPOITABLE_DOTNET_RELEASES"] = GitHub.DotnetReleases;

        foreach (var (name, value) in ExtraEnvironment) startInfo.Environment[name] = value;

        _process = Process.Start(startInfo)
                   ?? throw new InvalidOperationException($"起動できなかった: {executable}");

        // 必ず読み続ける（読まないとパイプが埋まって相手側の Console 書き込みが止まり、アプリごと固まる）。
        _process.OutputDataReceived += (_, e) => Capture(e.Data);
        _process.ErrorDataReceived += (_, e) => Capture(e.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        var endpoint = $"http://127.0.0.1:{port}";
        CdpEndpoint = endpoint;
        await WaitForCdpAsync(endpoint);

        _playwright = await Playwright.CreateAsync();
        Page = await ConnectToTheShellAsync(endpoint);

        // ページのロードだけでは JS ブリッジの bind が済んだ保証がない。
        await Page.WaitForFunctionAsync("() => window.__stkReady === true", null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });

        // Playwright は CDP に繋ぐと自前の色スキーム（既定 light）を当て、アプリが DevTools で指定したダークを打ち消すので合わせる。
        await Page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = ColorScheme.Dark });

        await WaitForLogTailAsync();
    }

    /// <summary>tail は「いまのファイル末尾」から始まるので、掴まれる前に書いた行は読まれない。待たないと拾われたり拾われなかったりする。</summary>
    private async Task WaitForLogTailAsync()
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (_output.Any(line => line.Contains("Tailing", StringComparison.Ordinal))) return;
            await Task.Delay(250);
        }

        throw new TimeoutException("ログの tail が始まらなかった");
    }

    public async Task DisposeAsync()
    {
        await StopAsync();
        GitHub.Dispose();
        await DeleteDataDirectoryAsync();
    }

    /// <summary>落としたばかりのアプリの子（CEF）がまだファイルを掴んでいることがあるので、少し待って何度か試す。消せなくても試験は落とさない。</summary>
    private async Task DeleteDataDirectoryAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= 5)
                {
                    Capture($"[e2e] 置き場を消せなかった（{DataDirectory}）: {ex.Message}");
                    return;
                }

                await Task.Delay(200 * attempt);
            }
        }
    }

    private async Task StopAsync()
    {
        if (_browser is not null) await _browser.CloseAsync();
        _playwright?.Dispose();
        _browser = null;
        _playwright = null;

        try
        {
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
            _process?.WaitForExit(5_000);
        }
        catch (InvalidOperationException) { }

        _process?.Dispose();
        _process = null;
    }

    /// <summary>CDP が開いた時点ではまだページが無いことがあり、そのあとにできたページを Playwright が見つけられないことがある。見えなければ繋ぎ直す。</summary>
    private async Task<IPage> ConnectToTheShellAsync(string endpoint)
    {
        for (var round = 0; round < 6; round++)
        {
            _browser = await _playwright!.Chromium.ConnectOverCDPAsync(endpoint);
            if (round == 0) await EnsureItIsOursAsync(endpoint);

            for (var attempt = 0; attempt < 50; attempt++)
            {
                if (_browser.Contexts.SelectMany(context => context.Pages).FirstOrDefault() is { } page) return page;
                await Task.Delay(100);
            }

            // 繋ぎを切るだけ（CDP で繋いだ browser の CloseAsync は、アプリの browser を閉じない）。
            Capture($"[e2e] 5 秒待ってもページが見えないので CDP に繋ぎ直す（{round + 1} 回目）");
            await _browser.CloseAsync();
            _browser = null;
        }

        var state = _process is { HasExited: true } ? $"アプリは落ちていた（終了コード {_process.ExitCode}）" : "アプリは動いている";
        throw new TimeoutException(
            $"CDP に繋がったが、アプリの画面のページが 30 秒現れなかった。{state}。最後の出力:{Environment.NewLine}" +
            string.Join(Environment.NewLine, _output.TakeLast(30)));
    }

    /// <summary>CefSharp では CEF の browser のプロセスがアプリ（Host.exe）そのものなので、CDP の SystemInfo.getProcessInfo の browser の番号がアプリの PID と同じになる。FreePort はプロセスの中でしか重なりを防げず、重なると後のアプリは CDP を開けないまま試験は相手のアプリを触る。</summary>
    private async Task EnsureItIsOursAsync(string endpoint)
    {
        var session = await _browser!.NewBrowserCDPSessionAsync();
        try
        {
            var info = await session.SendAsync("SystemInfo.getProcessInfo");
            var browser = info?.GetProperty("processInfo").EnumerateArray()
                .Where(process => process.GetProperty("type").GetString() == "browser")
                .Select(process => process.GetProperty("id").GetInt32())
                .FirstOrDefault();

            if (browser != _process!.Id)
                throw new PortTakenException($"{endpoint} の先は別のアプリだった（PID {browser}、起動したのは {_process.Id}）");
        }
        finally
        {
            await session.DetachAsync();
        }
    }

    private sealed class PortTakenException(string message) : Exception(message);

    private static async Task WaitForCdpAsync(string endpoint)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                using var response = await http.GetAsync($"{endpoint}/json/version");
                if (response.IsSuccessStatusCode) return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"{endpoint} に CDP が現れなかった");
    }

    /// <summary>同じ番号を 2 つのアプリに渡すと、後の方は CDP を開けず試験は前の方のアプリへ繋がる。クラスごとのアプリは同じ瞬間に起動するので、一度渡した番号は二度と渡さない。</summary>
    private static int FreePort()
    {
        while (true)
        {
            using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            lock (HandedOutPorts)
            {
                if (HandedOutPorts.Add(port)) return port;
            }
        }
    }

    private static readonly HashSet<int> HandedOutPorts = [];

    public Dictionary<string, string> ExtraEnvironment { get; } = new(StringComparer.Ordinal);

    public static string HostDirectory => Path.GetDirectoryName(HostExecutable())!;

    private static string HostExecutable()
    {
        var path = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "HostExecutable")?.Value
            ?? throw new InvalidOperationException("HostExecutable が埋め込まれていない");

        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException($"Host をビルドしてから実行すること: {full}");
        return full;
    }
}
