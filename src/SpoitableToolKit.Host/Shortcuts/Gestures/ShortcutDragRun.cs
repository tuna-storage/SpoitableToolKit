using SpoitableToolKit.Abstractions.Shortcuts;

namespace SpoitableToolKit.Host.Shortcuts.Gestures;

/// <summary>
/// 1 回のドラッグ（押してから離すまで）の、モジュールへの呼び出し。呼ぶのは 1 本ずつ、この順:
/// <see cref="IDraggableWristShortcut.OnStartDragAsync"/> → <see cref="IDraggableWristShortcut.OnDraggingAsync"/>（返るまでに来た分は最後の 1 つにまとめる）→
/// <see cref="IDraggableWristShortcut.OnEndDragAsync"/>（最後の位置で 1 回）。
/// 呼ぶのは裏のスレッド（CEF のスレッドでも VR の周でもない）。例外は 1 回目だけ <c>failed</c> へ渡し、そのあとも順は続ける
/// （始めたら終わりは必ず呼ぶ）。畳むとき（<c>token</c> が切れた）だけは、残りを呼ばずに終える。
/// 終えたら <c>finished</c> を 1 回呼ぶ（台帳が「走っている」を外す）。
/// </summary>
internal sealed class ShortcutDragRun
{
    private readonly Lock _gate = new();

    private readonly IDraggableWristShortcut _handler;

    private readonly CancellationToken _token;

    private readonly Action<Exception> _failed;

    private readonly Action _finished;

    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ShortcutDragEventParams? _start;

    private ShortcutDragEventParams? _latest;

    private ShortcutDragEventParams? _end;

    private bool _ending;

    private bool _pumping;

    private bool _reported;

    public ShortcutDragRun(IDraggableWristShortcut handler, ShortcutDragEventParams start, CancellationToken token, Action<Exception> failed, Action finished)
    {
        _handler = handler;
        _token = token;
        _failed = failed;
        _finished = finished;
        _start = start;

        Pump();

        // 畳んだら、引きの途中で止まっていても（離されないまま）終える。終えないと台帳の「走っている」が残る。
        var cancelled = token.Register(Pump);
        lock (_gate) _cancelled = cancelled;
    }

    /// <summary>畳むときの合図の登録（終えたら外す。外さないと、畳むまで引きごとに残る）。</summary>
    private CancellationTokenRegistration _cancelled;

    public Task Completion => _done.Task;

    /// <summary>動いた。<b>まだ呼んでいない分は捨てて、これに替える</b>（溜めない）。終わりを頼んだあとは受けない。</summary>
    public void Dragging(ShortcutDragEventParams e)
    {
        lock (_gate)
        {
            if (_ending) return;

            _latest = e;
        }

        Pump();
    }

    /// <summary>引き終わった。最後の <see cref="IDraggableWristShortcut.OnDraggingAsync"/> が返ってから、<paramref name="e"/> で 1 回呼ぶ。2 回目からは何もしない。</summary>
    public void End(ShortcutDragEventParams e)
    {
        lock (_gate)
        {
            if (_ending) return;

            _ending = true;
            _end = e;
        }

        Pump();
    }

    private void Pump()
    {
        lock (_gate)
        {
            if (_pumping) return;

            _pumping = true;
        }

        _ = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (true)
        {
            Func<Task>? next;
            var last = false;

            lock (_gate)
            {
                if (_token.IsCancellationRequested)
                {
                    // 畳んでいる。残りは呼ばない（終わりも）。
                    _start = _latest = _end = null;
                    _ending = true;
                    next = null;
                    last = true;
                }
                else if (_start is { } start)
                {
                    _start = null;
                    next = () => _handler.OnStartDragAsync(start, _token);
                }
                else if (_latest is { } moved)
                {
                    _latest = null;
                    next = () => _handler.OnDraggingAsync(moved, _token);
                }
                else if (_end is { } end)
                {
                    _end = null;
                    next = () => _handler.OnEndDragAsync(end, _token);
                    last = true;
                }
                else
                {
                    // 今は呼ぶものが無い。次の Dragging / End がまた回す。
                    _pumping = false;
                    return;
                }
            }

            if (next is not null) await CallAsync(next).ConfigureAwait(false);

            if (!last) continue;

            CancellationTokenRegistration cancelled;
            lock (_gate) cancelled = _cancelled;
            cancelled.Dispose();

            _finished();
            _done.TrySetResult();
            return;
        }
    }

    private async Task CallAsync(Func<Task> call)
    {
        try
        {
            await call().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (_reported) return;

            _reported = true;
            _failed(ex);
        }
    }
}
