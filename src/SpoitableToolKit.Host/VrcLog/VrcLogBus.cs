using System.Text;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.Events.VrcLog;
using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Events.VrcLog;

namespace SpoitableToolKit.Host.VrcLog;

/// <summary>
/// VRChat が書き込み中のファイルを掴みっぱなしにしないよう、読み取りのたびに開いて閉じる。
/// 行は <see cref="HostEvents.VrcLog"/>（アプリの口）から配る。足す・外すもその口へ渡すだけ。
/// 受け手が投げても読むのは止まらない（口が記録して次へ配る）。
/// </summary>
internal sealed class VrcLogBus : IVrcLogBus, IAsyncDisposable
{
    /// <summary>
    /// %LOCALAPPDATA%Low\VRChat\VRChat（LocalLow は SpecialFolder に無いので Local に "Low" を足す）。
    /// <c>SPOITABLE_VRCLOG_DIR</c> で差し替えられる。
    /// </summary>
    private static readonly string LogDirectory =
        Environment.GetEnvironmentVariable("SPOITABLE_VRCLOG_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low",
                "VRChat", "VRChat");

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DirectoryScanInterval = TimeSpan.FromSeconds(5);

    private readonly ILogger<VrcLogBus> _logger;
    private readonly IEventHub _hub;
    private readonly EventHub _fileChanges;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    private VrcLogParser _parser = new();
    private string? _currentLogPath;
    private long _position;
    private DateTime _lastDirectoryScan = DateTime.MinValue;
    private bool _attachedOnce;

    public VrcLogBus(ILoggerFactory loggerFactory, IEventHub hub)
    {
        _logger = loggerFactory.CreateLogger<VrcLogBus>();
        _hub = hub;
        _fileChanges = EventHubs.HostOnly<ILogFileEventListener>(_logger);
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public string? CurrentLogFile => _currentLogPath;

    /// <summary>
    /// 読んでいるファイルが変わった（見つけた・次のファイルに移った・消えた。<see cref="ILogFileEventListener"/>）。監視のスレッドから呼ばれる。
    /// Host の中だけ（<see cref="IVrcLogBus"/> には無い。足せるのもアプリ自身だけ）。
    /// 行の数ではなくこちらで知らせるのは、VRChat が止まっていると行が 1 本も来ないから。
    /// </summary>
    public IEventSource FileChanges => _fileChanges.Source;

    public void AddListener<TListener>(ISpoitableModule owner, IEventListener listener) where TListener : class, IEventListener =>
        _hub.Source.AddListener<TListener>(owner, listener);

    public void RemoveListener<TListener>(ISpoitableModule owner, IEventListener listener) where TListener : class, IEventListener =>
        _hub.Source.RemoveListener<TListener>(owner, listener);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Watching VRChat logs in {Directory}", LogDirectory);

        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    Poll();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Log poll failed: {Message}", ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Poll()
    {
        var now = DateTime.UtcNow;
        if (now - _lastDirectoryScan >= DirectoryScanInterval)
        {
            _lastDirectoryScan = now;
            ScanForLatestLog();
        }

        if (_currentLogPath is not null)
            ReadNewLines();
    }

    private void ScanForLatestLog()
    {
        if (!Directory.Exists(LogDirectory)) return;

        var files = new DirectoryInfo(LogDirectory).GetFiles("output_log_*.txt", SearchOption.TopDirectoryOnly);
        if (files.Length == 0) return;

        Array.Sort(files, static (a, b) => a.CreationTimeUtc.CompareTo(b.CreationTimeUtc));
        var latest = files[^1];
        if (latest.FullName == _currentLogPath) return;

        // 起動時に見つけた既存ログは履歴を読み返さず末尾から。
        // 監視中に新しいログが生まれた場合（VRChat の再起動）は、そのセッションを頭から読む。
        _position = _attachedOnce ? 0 : latest.Length;
        _attachedOnce = true;
        _currentLogPath = latest.FullName;
        _parser = new VrcLogParser();

        _logger.LogInformation("Tailing {File} from offset {Offset}", latest.Name, _position);
        FileChanged();
    }

    private void ReadNewLines()
    {
        var file = new FileInfo(_currentLogPath!);
        if (!file.Exists)
        {
            _currentLogPath = null;
            FileChanged();
            return;
        }

        if (file.Length == _position) return;

        if (file.Length < _position)
        {
            // 切り詰められた / 差し替えられた。
            _position = 0;
            _parser = new VrcLogParser();
        }

        var sourceFile = Path.GetFileName(_currentLogPath!);

        // VRChat が書き込み中なので FileShare.ReadWrite で開く。
        using var stream = new FileStream(
            _currentLogPath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 65536, FileOptions.SequentialScan);
        stream.Position = _position;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
            Publish(_parser.Feed(line, sourceFile));

        // バッチの終わりで保留中のエントリを確定させる。
        // これをしないと最後の 1 件が次の書き込みまで表に出てこない。
        Publish(_parser.Flush());

        _position = stream.Position;
    }

    private void FileChanged()
    {
        var changed = new LogFileChangedEventParams(_currentLogPath);
        _fileChanges.Fire<ILogFileEventListener>(l => l.OnLogFileChanged(changed));
    }

    private void Publish(ParsedEntry? entry) => Publish(_hub, entry);

    /// <summary>
    /// 1 行を配る: 全部の受け手へ行 → 意味の分かった行なら全部の受け手へ出来事、の順。
    /// 試験から直接通す（バスを作ると利用者の VRChat のログを読みに行くので）。
    /// </summary>
    internal static void Publish(IEventHub hub, ParsedEntry? entry)
    {
        if (entry is not { } parsed) return;

        var line = parsed.Line;
        hub.Fire<IVrcLogEventListener>(l => l.OnLogLine(line));
        if (parsed.Event is { } logEvent)
            hub.Fire<IVrcLogEventListener>(l => l.OnLogEvent(logEvent));
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
    }
}
