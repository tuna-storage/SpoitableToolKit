using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Events.AppLog;

namespace SpoitableToolKit.Host.AppLog;

/// <summary>
/// アプリログの受け口。ファイル・記録・画面の 3 つへ配る。
/// 画面を開いた瞬間に出したいものは、読み直しではなくメモリの台帳から描く（ファイルは人に送る用、記録 <c>applog.db</c> は起動をまたいで遡る用）。
/// 台帳は輪で、起動したら前の起動までの行で埋め直す（<see cref="Restore"/>）。
/// </summary>
internal sealed class AppLogSink : IDisposable
{
    /// <summary>台帳に残す行数。溢れたら古い方から捨てる。</summary>
    public const int Capacity = 1000;

    private readonly AppLogFile? _file;
    private readonly AppLogStore? _store;
    private readonly long _run;
    private readonly Lock _gate = new();
    private readonly Queue<AppLogEntry> _ring = new(Capacity);

    /// <summary>
    /// 出た行を配る口（<see cref="IAppLogEventListener"/>。足せるのはアプリだけ）。listener が投げたことは標準エラーへ書く
    /// （ILogger へ書くとこの受け口へ戻ってきて、入れ子を止める印で捨てられ、握り潰したのと同じになる）。
    /// </summary>
    private readonly EventHub _written;

    private bool _restored;

    /// <summary>
    /// 自分の中で呼ばれた分を数える。受け手（<see cref="IAppLogEventListener"/>）が <c>ILogger</c> を呼ぶと自分へ戻ってくるので、
    /// そのときは配らずに捨てる（無いと 1 行が無限に増える）。
    /// </summary>
    [ThreadStatic]
    private static bool _reentrant;

    /// <summary>最後に振った通し番号。記録があればその続きから（0 は「まだ振られていない」の印）。</summary>
    private long _seq;

    /// <param name="store">起動をまたぐ記録。開けなかったときは <c>null</c>（台帳はこの起動の分だけになる）。</param>
    /// <param name="errors">受け手が投げたことを書く先。既定は標準エラー（<c>Console.SetError</c> はプロセスで共有なので、試験からはこれで差し替える）。</param>
    public AppLogSink(AppLogFile? file = null, AppLogStore? store = null, long run = 0, TextWriter? errors = null)
    {
        _file = file;
        _store = store;
        _run = run;
        _seq = store?.LastSeq ?? 0;
        _written = EventHubs.HostOnly<IAppLogEventListener>(errors is null ? StandardErrorLogger.Instance : new StandardErrorLogger(errors));
    }

    /// <summary>いま書いているファイル。作れなかった／持っていないときは <c>null</c>。</summary>
    public string? Path => _file?.Path;

    /// <summary>出た行を受ける。どのスレッドから呼ばれてもよい。</summary>
    public void Append(AppLogEntry entry)
    {
        if (_reentrant) return;

        lock (_gate)
        {
            // 番号を振るのも台帳へ入れるのも同じ錠の中で（分けると、振ってから入れるまでに別のスレッドが割り込んで、番号と並びの順が食い違う）。
            entry = entry with { Seq = ++_seq, Run = _run };

            if (_ring.Count == Capacity) _ring.Dequeue();
            _ring.Enqueue(entry);
        }

        // ファイルと記録は錠の外で（中で書くと、1 行ごとの flush が出す側全員を待たせる。AppLogFile 自身が錠を持っている）。
        // 記録はキューへ積むだけ。番号を持っているので、積む順が前後しても入る場所は同じ。
        _file?.Write(entry);
        _store?.Write(entry);

        // 1 人が投げても他を巻き添えにしない（口が listener ごとに受け止め、標準エラーへ書く）。
        // listener の中で ILogger を呼ぶと自分へ戻ってくるので、配っている間は入れ子を止める。
        var written = new AppLogWrittenEventParams(entry);
        _reentrant = true;
        try
        {
            _written.Fire<IAppLogEventListener>(l => l.OnAppLogWritten(written));
        }
        finally
        {
            _reentrant = false;
        }
    }

    /// <summary>
    /// 前の起動までの行で台帳を埋める（<paramref name="since"/> より後の、新しい方から <see cref="Capacity"/> 行まで）。
    /// 受け口を作ったときではなく、設定を読んだあとに呼ぶ（残す期間は設定にあり、設定を読めるのはロガーが出来たあと）。
    /// それまでに出たこの起動の行は、前の起動の行の後ろにそのまま並ぶ。2 回目からは何もしない。
    /// </summary>
    public void Restore(DateTimeOffset since)
    {
        if (_store is null) return;

        lock (_gate)
        {
            if (_restored) return;
            _restored = true;
        }

        // この起動の行は読まない（もう台帳に居て、記録へも書かれ始めている）。
        var earlier = _store.Read(Capacity, beforeSeq: _store.LastSeq + 1, since: since);
        if (earlier.Count == 0) return;

        lock (_gate)
        {
            AppLogEntry[] merged = [.. earlier, .. _ring];
            _ring.Clear();
            foreach (var entry in merged.TakeLast(Capacity)) _ring.Enqueue(entry);
        }
    }

    /// <summary>
    /// 遡り。<paramref name="beforeSeq"/> より前を、新しい方から <paramref name="limit"/> 行（古い順で）。記録が無ければ空。
    /// 台帳ではなく記録から読むので、台帳から押し出された行も、前の起動の行も出る。
    /// </summary>
    /// <returns>行と、それより前がまだあるか。</returns>
    public (IReadOnlyList<AppLogEntry> Rows, bool HasMore) Older(long beforeSeq, int limit)
    {
        if (_store is null || limit <= 0) return ([], false);

        // 1 行多く読んで、続きがあるかを知る（数え直しの問い合わせを別に打たない）。
        var rows = _store.Read(limit + 1, beforeSeq: beforeSeq);
        return rows.Count > limit ? (rows.Skip(1).ToArray(), true) : (rows, false);
    }

    /// <summary><paramref name="olderThan"/> より古い記録を消す。台帳と画面には手を付けない。</summary>
    public void Prune(DateTimeOffset olderThan) => _store?.Prune(olderThan);

    /// <summary>
    /// 出た行を受け取る口（<see cref="IAppLogEventListener"/>。足せるのはアプリだけ — <c>Written.AddListener&lt;IAppLogEventListener&gt;(HostOwner.Instance, listener)</c>）。
    /// 足す前に出た分は届かないので、画面は <see cref="Recent"/> で追いついてから足すこと。
    /// </summary>
    public IEventSource Written => _written.Source;

    public IReadOnlyList<AppLogEntry> Recent()
    {
        lock (_gate) return [.. _ring];
    }

    /// <summary>
    /// 橋（<c>getStatus</c>・遡り）と push に渡す 1 行の形。record は <c>EqualityContract</c>（<c>System.Type</c>）を持ち、
    /// CefSharp の bind 越しには渡せないので匿名型で返す。形は 1 か所で決める（項目を足すと片方だけに載り、
    /// 追いつきの行と流れてきた行で画面の判定が食い違う）。
    /// </summary>
    public static object ToBridge(AppLogEntry entry) => new
    {
        seq = entry.Seq,
        run = entry.Run,

        // 形は画面が決める。ISO のまま渡して、VRChat の行と同じ関数を通す。
        ts = entry.Timestamp.ToString("O"),
        level = entry.Level.ToString(),
        shortLevel = entry.Short,
        category = entry.ShortCategory,
        fullCategory = entry.Category,
        thread = entry.ThreadId,
        message = entry.Message,
        exception = entry.Exception,
    };

    public object[] ForBridge() => [.. Recent().Select(ToBridge)];

    public void Dispose()
    {
        _file?.Dispose();
        _store?.Dispose();
    }
}
