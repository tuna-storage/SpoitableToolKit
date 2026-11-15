using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Host.Notifications.History;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Push;

namespace SpoitableToolKit.Host.Notifications.Confirms;

/// <summary>
/// 答えを待つ確認。どの面から答えても 1 回だけ効き、答えた面以外からも引っ込める。
/// 時間切れになったものは、<see cref="NotifyRequest.Later"/> があれば履歴から後で答えられる。
/// </summary>
internal sealed class ConfirmBoard(
    UiBroadcast broadcast, HostConfig config, NotificationHistory history, ILogger logger, Action<NotifyAction> run)
{
    private readonly ConcurrentDictionary<long, Ask> _pending = new();
    private long _lastId;

    public async Task<bool> PushAsync(NotifyRequest request, CancellationToken cancellationToken)
    {
        // 秒数はホストが決める（設定画面から変えられる）。
        // モジュールが指定してきたときだけ、そちらを尊重する。
        var duration = request.Timeout ?? TimeSpan.FromSeconds(config.ConfirmSeconds);

        var id = Interlocked.Increment(ref _lastId);
        var seq = history.Record("ask", request.Kind, request.Title, request.Body, state: "pending", confirmLabel: request.ConfirmLabel);
        var ask = new Ask(request, seq, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
            DateTimeOffset.Now + duration, (int)duration.TotalMilliseconds);
        _pending[id] = ask;

        logger.LogInformation("Confirm: {Title} ({Body})", request.Title, request.Body);

        broadcast.Push("notify", ask.Payload(id));

        // 出しっぱなしにしない。VRChat を見ている間に溜まっていくため。
        using var timeout = new CancellationTokenSource(duration);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var registration = linked.Token.Register(() =>
            Settle(id, accepted: false, timeout.IsCancellationRequested ? "expired" : "declined"));

        return await ask.Completion.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 答えを待っている確認。読み込み直した画面・後から繋いだ手首はここで追いつく
    /// （push だけだと、出たあとに開いた面には答える場所が無い）。残り時間は今から数え直す。
    /// </summary>
    public object[] Live() => [.. _pending.Select(p => p.Value.Payload(p.Key))];

    /// <summary>どのサーフェスから答えても、ここへ来る。二重に答えても最初の 1 回だけが効く。</summary>
    public void Answer(long id, bool accepted) => Settle(id, accepted, accepted ? "accepted" : "declined");

    /// <summary>「今後は聞かない」。答えは「承諾されなかった」で閉じてから、モジュールの <see cref="NotifyRequest.OptOut"/> を走らせる。</summary>
    public void OptOut(long id)
    {
        if (!_pending.TryGetValue(id, out var ask)) return;
        if (ask.Request.OptOut is not { } optOut) return;

        // ここが本当の守り。上の TryGetValue は Request を読むためで、別の面が同時に答えたときは間に割り込まれる。
        // Settle が負けたら（先に答えられていたら）走らせない。
        if (!Settle(id, accepted: false, "optedOut")) return;

        run(optOut);
    }

    /// <summary>
    /// 時間切れの確認に、履歴から後で答えた。1 回だけ効く（押し直しても二度は走らない）。
    /// </summary>
    public void AnswerLate(long seq)
    {
        if (history.TakeLater(seq) is not { } later) return;

        history.SetState(seq, "answeredLate");
        run(later);
    }

    /// <summary>アプリを畳むとき。待っている確認は「承諾されなかった」で終わらせる。</summary>
    public void CancelAll()
    {
        foreach (var id in _pending.Keys) Answer(id, accepted: false);
    }

    /// <summary>決着を付ける。付けたのがこの呼び出しなら真（二重に来たら偽）。</summary>
    private bool Settle(long id, bool accepted, string state)
    {
        if (id == 0) return false;
        if (!_pending.TryRemove(id, out var ask)) return false;

        // 時間切れで答えそびれたものは、後で答えられるように取っておく。
        if (state == "expired" && ask.Request.Later is { } later) history.KeepLater(ask.Seq, later);

        history.SetState(ask.Seq, state);

        // 答えた面以外も引っ込める。片方だけに残っている状態を作らない。
        broadcast.Push("notifyClosed", new { id });
        ask.Completion.TrySetResult(accepted);

        return true;
    }

    /// <summary>待っている確認 1 本。答えは <see cref="Completion"/> に 1 回だけ入る。</summary>
    private sealed record Ask(NotifyRequest Request, long Seq, TaskCompletionSource<bool> Completion,
        DateTimeOffset Deadline, int TimeoutMs)
    {
        public object Payload(long id) => new
        {
            id,
            kind = NotificationManager.KindPayload(Request.Kind),
            title = Request.Title,
            body = Request.Body,
            confirmLabel = Request.ConfirmLabel,
            optOutLabel = Request.OptOut?.Label,

            // UI 側はこれで残り時間の線を出し、0 になったら閉じる。
            // C# 側にも同じ時間の保険を張ってあるので、UI が居なくても止まらない。
            // 残りを今から数える（追いついた画面が、もう過ぎた分まで待たないように）。
            timeoutMs = Math.Max(0, (int)(Deadline - DateTimeOffset.Now).TotalMilliseconds),
            totalMs = TimeoutMs,
        };
    }
}
