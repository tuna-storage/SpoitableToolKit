using System.Numerics;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Shortcuts;

namespace SpoitableToolKit.Host.Shortcuts.Gestures;

/// <summary>
/// 手首のドラッグだけのタイル（モジュールが登録した <see cref="IDraggableWristShortcut"/>）への、押した・動いた・離した・途切れたの受け口。
/// 押した瞬間にドラッグを始め（<see cref="ShortcutBoard.BeginDrag"/>）、動けば <see cref="ShortcutDragRun.Dragging"/>、離したら <see cref="ShortcutDragRun.End"/>。
/// 押したかドラッグかは見分けない（押すだけのタイルは click で押す。手首のページが道を分ける）。
/// 押しには手首のページが番号を付ける（押すたびに増える。動いた・離した・途切れたも同じ番号）。今の押しと違う番号は捨て、
/// 前に見た番号以下の押し始めも捨てる — 知らせの順が狂っても押しを 1 回失うだけで、終わらないドラッグは残らない。
/// 次の押しが来たら前の押しは途切れたことにする（離したが届かなかった）。
/// 番号はページの読み込みごとに 1 から数え直す（SteamVR に入り直すと、手首の browser は作り直される — <c>VrLoop</c>）。
/// なので知らせには読み込みの印（<c>page</c>）も付いてくる。印が変わった押し始めが来たら数え直し、前のページの遅れた知らせは捨てる。
/// 知らせは CEF のスレッドから来るので、決めるのは全部 <see cref="_gate"/> の中。モジュールを呼ぶのは <see cref="ShortcutDragRun"/> で、ここでは待たない。
/// </summary>
internal sealed class ShortcutGestures(ShortcutBoard board, Func<bool> inLayout, ILogger logger)
{
    private readonly Lock _gate = new();

    private Press? _press;

    private string? _page;

    private long _lastDown = long.MinValue;

    private Task _lastWork = Task.CompletedTask;

    private bool _stopped;

    private sealed class Press(long seq, Vector2 start, Vector2 size, ShortcutDragRun? run)
    {
        public long Seq { get; } = seq;

        public Vector2 Start { get; } = start;

        public Vector2 Size { get; set; } = size;

        public Vector2 Last { get; set; } = start;

        public ShortcutDragRun? Run { get; } = run;

        public ShortcutDragEventParams Params() => new(Last / Size, Start / Size);
    }

    internal Task Idle
    {
        get
        {
            lock (_gate) return _lastWork;
        }
    }

    /// <summary>
    /// 手首のページからの知らせ。<paramref name="x"/>・<paramref name="y"/> はタイルの左上からの画素、<paramref name="width"/>・<paramref name="height"/> はタイルの大きさ。
    /// 画面から来た値はそのまま信じない: 数でない・大きさが 0 以下・台帳に無い名前・ドラッグだけのショートカットでない名前は捨てる。
    /// 途切れた（<see cref="ShortcutPointerKind.Cancel"/>）は位置を見ない（外したタイルは大きさが 0 になる。終わりの位置は前の動きのもの）。
    /// <paramref name="page"/> は手首のページの読み込みの印（番号はこの印ごとに数える）。
    /// </summary>
    public void Pointer(string page, string key, long seq, ShortcutPointerKind kind, float x, float y, float width, float height)
    {
        if (kind != ShortcutPointerKind.Cancel &&
            (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0))
        {
            logger.LogWarning("The wrist sent a pointer on {Key} that is not a position ({X}, {Y} in {Width} x {Height}); ignoring it", key, x, y, width, height);
            return;
        }

        var at = new Vector2(x, y);
        var size = new Vector2(width, height);

        lock (_gate)
        {
            if (_stopped) return;

            if (page != _page)
            {
                // 前のページの遅れた知らせ（作り直す前に送られた離しなど）は捨てる。今の押しを途切れさせない。
                if (kind != ShortcutPointerKind.Down) return;

                // 作り直したページの最初の押し。番号はそのページの 1 から数え直す。
                logger.LogInformation("The wrist page was loaded again; counting its presses afresh");
                _page = page;
                _lastDown = long.MinValue;
            }

            switch (kind)
            {
                case ShortcutPointerKind.Down:
                    if (seq <= _lastDown) return;

                    _lastDown = seq;
                    Interrupt();
                    Begin(key, seq, at, size);
                    return;

                case ShortcutPointerKind.Move:
                    if (Current(seq) is not { } moving) return;

                    moving.Last = at;
                    moving.Size = size;
                    moving.Run?.Dragging(moving.Params());
                    return;

                case ShortcutPointerKind.Up:
                    if (Current(seq) is not { } lifted) return;

                    lifted.Last = at;
                    lifted.Size = size;
                    _press = null;
                    lifted.Run?.End(lifted.Params());
                    return;

                case ShortcutPointerKind.Cancel:
                    if (Current(seq) is null) return;

                    Interrupt();
                    return;

                default:
                    return;
            }
        }
    }

    /// <summary>
    /// 手首の板が隠れた・VR の browser を閉じた（C# が知っている途切れ。<c>VrLoop</c> が呼ぶ）。今の押しを途切れたことにする（ドラッグの終わりを 1 回）。
    /// ページからの離しを待たない — 隠した browser・閉じた browser に離しが届くとは限らず、届かないとドラッグが始まったまま終わらない。
    /// あとから離しが届いても、もう今の押しではないので捨てる。
    /// </summary>
    public void CutOff()
    {
        lock (_gate) Interrupt();
    }

    public void Stop()
    {
        lock (_gate)
        {
            _stopped = true;
            _press = null;
        }
    }

    /// <summary>
    /// 今の押し（番号が同じもの）。名前は見ない — 番号は手首のページが全部のタイルを通して数えるので、番号だけで押しが決まる。
    /// 押したあとに来た名前を信じても、呼ぶのは押し始めたときに台帳で引き直したショートカットだけ。
    /// </summary>
    private Press? Current(long seq) => _press is { } current && current.Seq == seq ? current : null;

    private void Begin(string key, long seq, Vector2 at, Vector2 size)
    {
        if (inLayout()) return;

        if (board.FindDraggable(key) is null)
        {
            logger.LogWarning("The wrist dragged {Key}, which is not a shortcut a module registered for dragging; ignoring it", key);
            return;
        }

        // 前のドラッグの呼び出しが返り切っていなければ、この押しは捨てる。
        if (board.IsRunning(key))
        {
            logger.LogInformation("The shortcut {Key} is still running; ignoring the press", key);
            return;
        }

        var start = new ShortcutDragEventParams(at / size, at / size);
        var run = board.BeginDrag(key, start);
        _press = new Press(seq, at, size, run);

        if (run is not null) _lastWork = run.Completion;
    }

    private void Interrupt()
    {
        if (_press is not { } current) return;

        _press = null;
        current.Run?.End(current.Params());
    }
}
