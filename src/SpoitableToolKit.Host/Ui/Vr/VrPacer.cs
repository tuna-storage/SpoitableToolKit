using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SpoitableToolKit.Host.Ui.Vr;

/// <summary>
/// VR の周をヘッドセットの速さで打つ係。打つのは自分のスレッドで、周そのものは UI のスレッドで走らせる（<paramref name="ui"/> へ投げる）。
/// WinForms のタイマーは 10 ms より短くできない（<c>USER_TIMER_MINIMUM</c>）ので 120 Hz に届かず、90 Hz も、ほかの誰かがシステムのタイマーの細かさを上げているときしか出ない。
/// そこで細かい待ち合わせのタイマー（<c>CREATE_WAITABLE_TIMER_HIGH_RESOLUTION</c>）で待つ。システム全体の細かさは変えない。
/// 締め切りは足し込む（前の締め切り + 1 周）。待ち始めから測ると、周の中身の分だけ毎回遅れていく。1 周より遅れたら今に合わせ直す（取り返そうとして連打しない）。
/// 溜めない: UI のスレッドが前の周をまだ片付けていなければ、その周は打たない（投げた周が列を作ると、遅れが溜まったまま戻らない）。
/// SteamVR の vsync とは揃えていないので、数秒に 1 枚ぶん重なったり飛んだりしうる。気になったら周の中で <c>GetTimeSinceLastVsync</c> を読んで位相を合わせる。
/// </summary>
internal sealed class VrPacer : IDisposable
{
    private readonly SynchronizationContext _ui;

    private readonly Action _tick;

    private readonly Thread _thread;

    private readonly AutoResetEvent _wake = new(false);

    private long _periodTicks;

    /// <summary>1 = UI のスレッドへ投げた周がまだ片付いていない。</summary>
    private int _pending;

    private volatile bool _stopping;

    public VrPacer(SynchronizationContext ui, Action tick, TimeSpan period)
    {
        _ui = ui;
        _tick = tick;
        _periodTicks = period.Ticks;

        _thread = new Thread(Run) { IsBackground = true, Name = "VR pacer", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>周期。変えたらすぐ効く（繋がっていない間の 2 秒から、繋がった瞬間に速める）。</summary>
    public TimeSpan Period
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _periodTicks));
        set
        {
            if (Interlocked.Exchange(ref _periodTicks, value.Ticks) != value.Ticks) _wake.Set();
        }
    }

    /// <summary>止める。UI のスレッドから呼んでよい（打つ側は待ちから起こしてすぐ抜ける）。</summary>
    public void Dispose()
    {
        if (_stopping) return;

        _stopping = true;
        _wake.Set();

        // 抜けきらなかったら起こす口は残す（抜ける途中で触って落ちないように）。
        if (_thread.Join(TimeSpan.FromSeconds(1))) _wake.Dispose();
    }

    private void Run()
    {
        using var timer = new Waitable(CreateTimer());
        var handles = new WaitHandle[] { timer, _wake };

        var next = Stopwatch.GetTimestamp();

        while (!_stopping)
        {
            var period = TimeSpan.FromTicks(Interlocked.Read(ref _periodTicks));
            var step = (long)(period.TotalSeconds * Stopwatch.Frequency);

            next += step;

            var now = Stopwatch.GetTimestamp();

            if (now - next > step) next = now;

            var wait = next - now;

            if (wait > 0)
            {
                // 負の値は「今から」。単位は 100 ns。
                var due = -(long)(wait * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency);

                if (!SetWaitableTimer(timer.SafeWaitHandle, in due, 0, 0, 0, false))
                {
                    Thread.Sleep(period);
                }
                else if (WaitHandle.WaitAny(handles) == 1)
                {
                    next = Stopwatch.GetTimestamp();
                    continue;
                }
            }

            if (_stopping) break;

            if (Interlocked.CompareExchange(ref _pending, 1, 0) != 0) continue;

            try
            {
                _ui.Post(_ =>
                {
                    try
                    {
                        _tick();
                    }
                    finally
                    {
                        Volatile.Write(ref _pending, 0);
                    }
                }, null);
            }
            catch
            {
                // **このスレッドからは投げない**（背景のスレッドの例外はプロセスごと落とす）。
                // 投げ先が無くなった（終わり際）なら、次の周でまた試すか、止められて抜ける。
                Volatile.Write(ref _pending, 0);
            }
        }
    }

    /// <summary>細かい待ち合わせのタイマー。使えない Windows では普通のもの。</summary>
    private static SafeWaitHandle CreateTimer()
    {
        var timer = CreateWaitableTimerExW(0, null, CreateWaitableTimerHighResolution, TimerAllAccess);

        if (!timer.IsInvalid) return timer;

        timer.Dispose();
        return CreateWaitableTimerExW(0, null, 0, TimerAllAccess);
    }

    private sealed class Waitable : WaitHandle
    {
        public Waitable(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    private const uint CreateWaitableTimerHighResolution = 0x00000002;

    private const uint TimerAllAccess = 0x001F0003;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(
        SafeWaitHandle timer, in long dueTime, int period, nint completion, nint argument,
        [MarshalAs(UnmanagedType.Bool)] bool resume);
}
