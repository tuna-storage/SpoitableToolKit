using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.Events.VRChat;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Host.App;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Status;

namespace SpoitableToolKit.Host.VRChat;

/// <summary>
/// VRChat がいま動いているか。<b>ホストが状態を持ち、起動・終了を出来事として流す</b>
/// （アプリの VRChat の口 <see cref="HostEvents.VRChat"/> から。本体の札・通知もその口の受け手 — <see cref="Connect"/>）。
/// <para>
/// 見るのは<b>プロセス</b>（<c>VRChat.exe</c> を 2 秒おき。SteamVR の vrmonitor と同じやり方）。ログでは落ちた・止められたが拾えない。
/// </para>
/// <para>
/// <b>起動の順番</b>（<see cref="Begin"/>）: アプリの起動時に一度確かめる → その値のままモジュールを全部読む（読み込みの間は見張らない）→
/// 見張りを始める。最初の見張りで値が変わっていれば、そこで初めて出来事を流す。
/// 読み込みの途中で見張っていると、先に読まれたモジュールだけが出来事を受け、後から読まれたものは取りこぼす。
/// </para>
/// <para>
/// <b>状態を変えてから流す</b>（終わったと聞いて <see cref="IsRunning"/> を読めば false）。
/// <b>見張りは重ならない</b> — 受け取る側が待つと、次の見張りはその回を飛ばす（起動と終了の順番が入れ替わらない）。
/// </para>
/// </summary>
internal sealed class VRChatPresence : IDisposable
{
    /// <summary>探すプロセスの名前（<c>VRChat.exe</c>）。</summary>
    public const string ProcessName = "VRChat";

    /// <summary>見張る間隔。</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 起動・終了の通知の種類（手首の通知の絞り込み「手首の通知に出すもの」で切れる）。電源の印。
    /// </summary>
    public static readonly NotifyKind NoteKind = new("host.vrchat", NotifyTone.Teal, "M12 3v8M6.3 6.3a8 8 0 1 0 11.4 0");

    private readonly Func<bool> _probe;
    private readonly IEventHub _vrchat;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private System.Threading.Timer? _timer;
    private volatile bool _running;
    private volatile bool _disposed;
    private bool _checked;

    /// <param name="probe">いま動いているか。<b>既定はプロセスを探す</b>（<see cref="ProcessCheck"/>）。試験と e2e は差し替える。</param>
    /// <param name="vrchat">起動・終了を配る口（アプリの <see cref="HostEvents.VRChat"/>）。</param>
    public VRChatPresence(Func<bool>? probe, IEventHub vrchat, ILogger<VRChatPresence> logger)
    {
        _probe = probe ?? (() => ProcessCheck.IsRunning(ProcessName));
        _vrchat = vrchat;
        _logger = logger;
    }

    /// <summary>いま動いているか（最後に確かめた値）。</summary>
    public bool IsRunning => _running;

    /// <summary>
    /// 起動の順番どおりに: <b>一度確かめる → <paramref name="loadModules"/> → 見張り始める</b>。
    /// 読み込みの間に VRChat が起動・終了しても、出来事は最初の見張りで 1 回だけ流れる。
    /// </summary>
    /// <param name="interval">見張る間隔。null なら <see cref="Interval"/>（試験は短くする）。</param>
    public void Begin(Action loadModules, TimeSpan? interval = null)
    {
        // アプリは先に確かめている（タイトルバーを最初から正しく出すため）。済んでいなければここで。
        if (!_checked) CheckOnce();

        loadModules();
        Start(interval ?? Interval);
    }

    /// <summary>アプリの起動時の 1 回。<b>出来事は流さない</b>（いまの状態を入れるだけ）。</summary>
    public void CheckOnce()
    {
        _checked = true;
        _running = Probe(fallback: false);
        _logger.LogInformation("VRChat is {State} at startup", _running ? "running" : "not running");
    }

    /// <summary>見張りを始める。<b>モジュールを全部読み終えてから</b>（<see cref="Begin"/>）。</summary>
    public void Start(TimeSpan interval)
    {
        if (_disposed || _timer is not null) return;

        _timer = new System.Threading.Timer(_ => Poll(), null, interval, interval);
    }

    /// <summary>
    /// 1 回確かめる。<b>変わっていたら、状態を変えてから出来事を流す</b>（<b>見張りのスレッドから</b>。受け手が投げても、残りの受け手と次の回は止まらない —
    /// 口が受け手ごとに受け止めて記録する）。
    /// <b>重ならない</b>: 前の回がまだ出来事を流している最中なら、この回は飛ばす（次の回でまた見る）。
    /// </summary>
    public void Poll()
    {
        if (_disposed || !_gate.TryEnter()) return;

        try
        {
            if (_disposed) return;

            var now = Probe(fallback: _running);
            if (now == _running) return;

            _running = now;
            _logger.LogInformation(now ? "VRChat started" : "VRChat shut down");

            if (now)
            {
                var started = new VRChatStartedEventParams { NoticedAt = DateTimeOffset.Now };
                _vrchat.Fire<IVRChatEventListener>(l => l.OnVRChatStarted(started));
            }
            else
            {
                var shutdown = new VRChatShutdownEventParams { NoticedAt = DateTimeOffset.Now };
                _vrchat.Fire<IVRChatEventListener>(l => l.OnVRChatShutdown(shutdown));
            }
        }
        finally
        {
            _gate.Exit();
        }
    }

    /// <summary>
    /// 見張りをやめる。<b>流している最中の出来事が済むまで待つ</b>（モジュールを畳む前に呼ぶ — 畳んだ後に出来事が届かないように）。
    /// 待つのは <see cref="DisposeWait"/> まで（受け取る側が止まったままでも、アプリは閉じられる）。
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();

        if (_gate.TryEnter(DisposeWait)) _gate.Exit();
        else _logger.LogWarning("A VRChat start/shutdown handler is still running after {Seconds} s; closing without waiting", DisposeWait.TotalSeconds);
    }

    /// <summary>閉じるときに、流している最中の出来事を待つ長さ。</summary>
    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(5);

    /// <summary>確かめる。<b>見に行けなかったら前の値のまま</b>（その回は変わったことにしない）。</summary>
    private bool Probe(bool fallback)
    {
        try
        {
            return _probe();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check whether VRChat is running; keeping the last answer");
            return fallback;
        }
    }

    /// <summary>
    /// 本体の札と通知を、VRChat の起動・終了に繋ぐ。アプリ自身（<see cref="HostOwner"/>）として <paramref name="vrchat"/> に足す。
    /// <b>モジュールを読み込む前に呼ぶ</b>。
    /// <para>
    /// 終わったら: ワールド・人数・動画の札を消す → タイトルバーを「VRChat は動いていません」に → 通知。
    /// 起動したら: タイトルバーを戻す → 通知。<b>本体が先</b>、モジュールはその後（モジュールが本体の札を読んでも食い違わない）—
    /// 本体の受け手は配る順がいちばん前（<see cref="HostReaction.ListenerPriority"/>）。
    /// </para>
    /// <para>
    /// <b>ログの行は止めない</b>: 札を消すのは出来事のときだけ。見張りは最大 2 秒遅れるので、終わったと見た後に届いた行も本物として数える。
    /// </para>
    /// <para>
    /// <b>本体の側が投げても、モジュールへは流れる</b>（口が受け手ごとに受け止めて、次の受け手へ配る）。状態はもう変わっていて、次の見張りでは流し直さない
    /// （通知の push が、画面の読み直しと重なって CefSharp に投げられることがある）。
    /// </para>
    /// </summary>
    public static void Connect(
        IEventSource vrchat, InstanceTracker instance, LogReadingStatus reading, INotificationManager notify, ILocalizer text) =>
        vrchat.AddListener<IVRChatEventListener>(HostOwner.Instance, new HostReaction(instance, reading, notify, text));

    /// <summary>本体の札・タイトルバー・通知（<see cref="Connect"/>）。</summary>
    private sealed class HostReaction(InstanceTracker instance, LogReadingStatus reading, INotificationManager notify, ILocalizer text)
        : IVRChatEventListener
    {
        /// <summary>
        /// <b>いちばん前</b>。モジュールが優先度を上げても、本体が札を消す前に「終わった」を聞くことはない
        /// （同じ値なら足した順で、本体はモジュールを読み込む前に足す）。
        /// </summary>
        public int ListenerPriority => int.MaxValue;

        public void OnVRChatStarted(VRChatStartedEventParams e)
        {
            reading.Show();
            notify.PushToast(NoteKind, text["host.vrchat.started"]);
        }

        public void OnVRChatShutdown(VRChatShutdownEventParams e)
        {
            instance.Reset();
            reading.Show();
            notify.PushToast(NoteKind, text["host.vrchat.stopped"]);
        }
    }
}
