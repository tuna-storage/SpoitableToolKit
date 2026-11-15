using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Host.Notifications.History;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Push;

namespace SpoitableToolKit.Host.Notifications.Toasts;

/// <summary>右下のトースト。出ているものの台帳を持ち、閉じるのは 1 回だけを保つ。消える時計は 1 枚ずつ（<see cref="HostToast"/>）が持つ。</summary>
internal sealed class ToastBoard(
    UiBroadcast broadcast, HostConfig config, NotificationHistory history, ILogger logger, Action<NotifyAction> run)
{
    /// <summary>出ているトースト。閉じるのは 1 回だけを保つ台帳でもある。</summary>
    private readonly ConcurrentDictionary<long, HostToast> _toasts = new();

    private long _lastToastId;

    /// <summary>
    /// 押せるものが付いているトーストを、最低これだけは出しておく。出しておく秒数は「読めれば足りる」長さ（既定 3 秒）なので、
    /// 押すには気付いて・狙って・手を動かす時間が要る。押せるものを出しておいて押す前に消えるのは、出さないより悪い。
    /// </summary>
    private static readonly TimeSpan ActionLinger = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 右下へ 1 枚。消える時計はここ（C# 側）が持つ — 取っ手が後から書き換えられる以上、
    /// 「まだ生きているか」の正はホストに無いと、画面で消えたものが更新で生き返る。
    /// </summary>
    public IToast Push(NotifyKind kind, string title, string? body, NotifyAction[] actions)
    {
        logger.LogInformation("Notify: {Title} ({Body})", title, body);

        var seq = history.Record("toast", kind, title, body);
        var toast = new HostToast(this, Interlocked.Increment(ref _lastToastId), kind, seq, title, body, actions);
        _toasts[toast.Id] = toast;
        var linger = TimeSpan.FromSeconds(config.ToastSeconds);
        if (actions.Length > 0 && linger < ActionLinger) linger = ActionLinger;

        toast.Start(linger);

        return toast;
    }

    /// <summary>
    /// 引っ込める。時計・利用者の「閉じる」・<c>Dispose</c> のどれから来ても 1 回だけ効く
    /// （<see cref="Confirms.ConfirmBoard.Answer"/> と同じ形）。
    /// </summary>
    public void Close(long id)
    {
        if (!_toasts.TryRemove(id, out var toast)) return;

        toast.Stop();
        broadcast.Push("toastClosed", new { id });
    }

    /// <summary>
    /// いま出ているもの。途中で開いた画面がここで追いつく —
    /// push は受け取れない間は捨てられるので、これが無いと
    /// 進み具合のトーストが片方の画面にだけ出ないまま終わる。
    /// </summary>
    public object[] Live() => [.. _toasts.Values.Select(t => t.Payload())];

    /// <summary>
    /// 押せるものが押された。先に閉じてから走らせる —
    /// 決着を付けるためのものなので、残っていると押し直せてしまう。
    /// </summary>
    public void Invoke(long id, int index)
    {
        if (!_toasts.TryGetValue(id, out var toast)) return;
        if (toast.ActionAt(index) is not { } action) return;

        Close(id);
        run(action);
    }

    /// <summary>アプリを畳むとき。時計を止める — 畳んだあとに発火して、消えた画面へ push しに行かせない。</summary>
    public void CloseAll()
    {
        foreach (var id in _toasts.Keys) Close(id);
    }

    internal void Send(HostToast toast)
    {
        // 閉じたものは画面へ戻さない（戻すと ✕ でも消せない札になる。もう _toasts に居ないので、閉じる頼みも時計も空振りする）。
        // 履歴は書き換える — 「取得しました」はベルの一覧で読める。
        if (_toasts.ContainsKey(toast.Id)) broadcast.Push("toast", toast.Payload());
        history.Rewrite(toast.Seq, toast.Title, toast.Body);
    }
}
