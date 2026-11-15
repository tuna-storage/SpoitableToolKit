using SpoitableToolKit.Abstractions.Notifications;

namespace SpoitableToolKit.Host.Notifications.Toasts;

/// <summary>出した 1 枚。値はここが正で、画面はこれを写しているだけ。書き換えはモジュールのどのスレッドからでも来るので、読み書きは錠で括る。</summary>
internal sealed class HostToast(
    ToastBoard owner, long id, NotifyKind kind, long seq, string title, string? body, NotifyAction[] actions)
    : IToast
{
    private readonly Lock _gate = new();
    private string _title = title;
    private string? _body = body;
    private double? _progress;

    /// <summary>最後に画面へ渡した百分率。これで送る回数を抑える。</summary>
    private int _sentPercent = -1;

    /// <summary>1 に達したか。達したあとの <see cref="Dispose"/> は効かない。</summary>
    private bool _completed;

    private TimeSpan _linger;
    private System.Threading.Timer? _clock;

    /// <summary>時計で消えるときの長さ（ms）。画面は下端の線をこれで縮める。進み具合があるあいだは 0。</summary>
    private int _lingerMs;

    public long Id { get; } = id;

    public long Seq { get; } = seq;

    public string Title
    {
        get { lock (_gate) return _title; }
        set
        {
            lock (_gate)
            {
                if (_title == value) return;
                _title = value;
            }

            owner.Send(this);
        }
    }

    public string? Body
    {
        get { lock (_gate) return _body; }
        set
        {
            lock (_gate)
            {
                if (_body == value) return;
                _body = value;
            }

            owner.Send(this);
        }
    }

    public double? Progress
    {
        get { lock (_gate) return _progress; }
        set
        {
            bool send, completing;
            lock (_gate)
            {
                var next = value is null ? null : (double?)Math.Clamp(value.Value, 0, 1);
                _progress = next;

                // 進み具合が入った時点で時計は止まる（0 でも止まる）。終わりが定義されたものを、途中で消してしまわないため。
                // 終わったあとは止めない — 取得は最後の塊で 100% になり、置き終えてもう一度 100% を入れる。
                // そこで止めると、終わりの時計がもう掛からず消えなくなる。
                if (next is not null && !_completed) StopClock();

                // 百分率が動いたときだけ送る。40MB の取得でも 100 回に収まる。
                var percent = next is null ? -1 : (int)(next.Value * 100);
                send = percent != _sentPercent;
                _sentPercent = percent;

                completing = next >= 1 && !_completed;
                if (completing) _completed = true;
            }

            if (send) owner.Send(this);

            // 終わったら、ふつうのトーストと同じ扱いに戻す（しばらく見せてから消える）。
            if (completing) Arm(_linger);
        }
    }

    public void Dispose()
    {
        // 完了を見せている最中なら触らない。using を抜けた瞬間に消すと、
        // 「終わりました」が誰にも見えないまま終わる。
        lock (_gate)
        {
            if (_completed) return;
        }

        owner.Close(Id);
    }

    internal void Start(TimeSpan linger)
    {
        _linger = linger;
        Arm(linger);
        owner.Send(this);
    }

    internal void Stop()
    {
        lock (_gate) StopClock();
    }

    internal NotifyAction? ActionAt(int index) => NotificationManager.ActionAt(actions, index);

    internal object Payload()
    {
        lock (_gate)
            return new
            {
                id = Id,
                kind = NotificationManager.KindPayload(kind),
                title = _title,
                body = _body,
                progress = _progress,

                // 時計が回っているときだけ長さを渡す（画面は下端の線をこの長さで縮める）。
                lingerMs = _clock is null ? 0 : _lingerMs,
                actions = actions.Select((a, i) => new { index = i, label = a.Label }).ToArray(),
            };
    }

    private void Arm(TimeSpan after)
    {
        lock (_gate)
        {
            StopClock();
            _lingerMs = (int)after.TotalMilliseconds;
            _clock = new System.Threading.Timer(_ => owner.Close(Id), null, after, Timeout.InfiniteTimeSpan);
        }
    }

    private void StopClock()
    {
        _clock?.Dispose();
        _clock = null;
    }
}
