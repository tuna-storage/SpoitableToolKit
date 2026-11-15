using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.AppLog;
using SpoitableToolKit.Host.Events.AppLog;
using SpoitableToolKit.Host.Events.VrcLog;
using SpoitableToolKit.Host.Shortcuts;
using SpoitableToolKit.Host.Status;
using SpoitableToolKit.Host.Ui.LogView;
using SpoitableToolKit.Host.Ui.Push;
using SpoitableToolKit.Host.VrcLog.Store;

namespace SpoitableToolKit.Host.Ui.Shell;

/// <summary>
/// ログを UI へ流す前に一定間隔でまとめる。ワールド遷移の瞬間は 1 秒で数百行が流れ、1 行ごとに <c>ExecuteScriptAsync</c> を呼ぶとその分だけ CEF の UI スレッドへ post されるので、間隔を切って配列で渡す。
/// </summary>
internal sealed class UiPump : IStoredLogEventListener, IAppLogEventListener, IAsyncDisposable
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>UI が見ていない間に溜め込みすぎないための上限。溢れた分は捨てる。</summary>
    private const int MaxBuffered = 2000;

    private readonly UiBroadcast _broadcast;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly Lock _gate = new();

    private List<object> _events = [];
    private List<object> _lines = [];

    /// <summary>ソフトウェア自身のログ。同じ周期に相乗りする。</summary>
    private List<object> _appLogs = [];

    private readonly StatusBoard _status;

    private readonly ShortcutBoard? _shortcuts;

    private readonly Action<long, long>? _counted;

    /// <summary>出した通算。数えるのはここ — バッチが通る場所が既にここなので、<c>logBus</c> にもう 1 つ購読を足すと順序が 2 つになる。</summary>
    private long _eventTotal;

    private long _lineTotal;

    public UiPump(UiBroadcast broadcast, StatusBoard status, Action<long, long>? counted = null, ShortcutBoard? shortcuts = null)
    {
        _broadcast = broadcast;
        _status = status;
        _counted = counted;
        _shortcuts = shortcuts;
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>保存できたイベントを流す。<c>logBus</c> からではなく <see cref="SpoitableToolKit.Host.VrcLog.Store.SqliteVrcLogStore.Stored"/> から受ける（画面に出た行は必ず DB にある、という約束の要）。</summary>
    void IStoredLogEventListener.OnEventStored(StoredEvent stored) => Enqueue(stored);

    void IStoredLogEventListener.OnLineStored(StoredLine stored) => Enqueue(stored);

    /// <inheritdoc cref="IStoredLogEventListener.OnEventStored"/>
    public void Enqueue(StoredEvent stored)
    {
        lock (_gate)
        {
            if (_events.Count < MaxBuffered) _events.Add(LogRow.From(stored));
        }
    }

    /// <summary>保存していない生行。<c>StoreAllLines</c> が切のときの経路で、id を持たない（そのセッション限りで、遡っても出てこない）。</summary>
    public void Enqueue(VrcLogLine line)
    {
        lock (_gate)
        {
            if (_lines.Count < MaxBuffered) _lines.Add(LogRow.FromLine(null, line.Timestamp, line.Level.ToString(), line.Tag, line.Message));
        }
    }

    /// <summary>保存できた生行。<c>StoreAllLines</c> が入のときはこちらだけを通す（両方通すと、同じ行が id 付きと id 無しで 2 回出る）。</summary>
    public void Enqueue(StoredLine stored)
    {
        lock (_gate)
        {
            if (_lines.Count < MaxBuffered) _lines.Add(LogRow.FromLine(stored));
        }
    }

    void IAppLogEventListener.OnAppLogWritten(AppLogWrittenEventParams e) => Enqueue(e.Entry);

    public void Enqueue(AppLogEntry entry)
    {
        lock (_gate)
        {
            // 形は台帳の追いつき（getStatus）と同じ 1 か所で決める。片方だけに項目が載ると、画面の判定が食い違う。
            if (_appLogs.Count < MaxBuffered) _appLogs.Add(AppLogSink.ToBridge(entry));
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(FlushInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                Flush();
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>溜まっている分を流す。ふだんは <see cref="FlushInterval"/> の時計が呼ぶ。<c>internal</c> なのは試験のため（時計を待つ試験は遅い機械で運になる。呼ぶのは本物の経路そのもの）。</summary>
    internal void Flush()
    {
        List<object> events;
        List<object> lines;
        List<object> appLogs;
        lock (_gate)
        {
            events = _events;
            lines = _lines;
            appLogs = _appLogs;
            _events = [];
            _lines = [];
            _appLogs = [];
        }

        if (events.Count > 0 || lines.Count > 0)
        {
            _eventTotal += events.Count;
            _lineTotal += lines.Count;
            _counted?.Invoke(_eventTotal, _lineTotal);
        }

        // 状態の口はこの周期で読む。書き換えはどのスレッドからでも来る（BLE・ログの監視・言語の切り替え）ので、
        // 書いた側では何もせず、ここで毎回組み立てて、前に配ったものと違うときだけ配る。
        _status.Flush();
        _shortcuts?.Flush();

        if (events.Count > 0) _broadcast.Push("logEvents", events.ToArray());
        if (lines.Count > 0) _broadcast.Push("logLines", lines.ToArray());
        if (appLogs.Count > 0) _broadcast.Push("appLogs", appLogs.ToArray());
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
