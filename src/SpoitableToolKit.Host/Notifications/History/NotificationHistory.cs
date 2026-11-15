using System.Collections.Concurrent;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Host.Ui.Push;

namespace SpoitableToolKit.Host.Notifications.History;

/// <summary>
/// このセッションで出したものの履歴（タイトルバーのベル・手首の通知の板）。新しいものが先で、ディスクには書かない。
/// 時間切れになった確認の「後で答える」もここが持つ。履歴の番号で引き、履歴から落ちたら一緒に落とす。
/// </summary>
internal sealed class NotificationHistory(UiBroadcast broadcast)
{
    /// <summary>履歴に残す数。1 セッションで数百も出るのは壊れているときで、そこから先は古いものから落とす。</summary>
    public const int Limit = 200;

    private readonly Lock _gate = new();
    private readonly LinkedList<HistoryEntry> _entries = [];
    private long _lastSeq;

    private readonly ConcurrentDictionary<long, NotifyAction> _late = new();

    /// <summary>
    /// 履歴の 1 件。<c>Type</c> は <c>toast</c> / <c>banner</c> / <c>ask</c>、
    /// <c>State</c> は確認のときだけ（<c>pending</c> / <c>accepted</c> / <c>declined</c> / <c>expired</c> /
    /// <c>optedOut</c> / <c>answeredLate</c>）。
    /// </summary>
    private sealed record HistoryEntry(
        long Seq, string Type, NotifyKind Kind, string Title, string? Body, DateTimeOffset At,
        string? State, string? ConfirmLabel);

    /// <summary>このセッションの履歴。新しいものが先。</summary>
    public object[] Snapshot()
    {
        lock (_gate) return [.. _entries.Select(Payload)];
    }

    /// <summary>1 件足して、画面へ配る。返すのは履歴の番号。溢れた古いものは「後で答える」ごと落とす。</summary>
    public long Record(string type, NotifyKind kind, string title, string? body,
        string? state = null, string? confirmLabel = null)
    {
        HistoryEntry entry;
        lock (_gate)
        {
            entry = new HistoryEntry(++_lastSeq, type, kind, title, body, DateTimeOffset.Now, state, confirmLabel);
            _entries.AddFirst(entry);

            while (_entries.Count > Limit)
            {
                _late.TryRemove(_entries.Last!.Value.Seq, out _);
                _entries.RemoveLast();
            }
        }

        broadcast.Push("notifyHistory", Payload(entry));
        return entry.Seq;
    }

    /// <summary>トーストの見出し・本文が書き換わったら、履歴も同じものにする（「取得しています」→「取得しました」）。</summary>
    public void Rewrite(long seq, string title, string? body) =>
        Update(seq, e => e.Title == title && e.Body == body ? e : e with { Title = title, Body = body });

    public void SetState(long seq, string state) => Update(seq, e => e with { State = state });

    /// <summary>
    /// 時間切れの確認の「後で答える」を取っておく。状態を書き換える前に入れる
    /// （書き換えで配る履歴に「後で答えられる」が載るように）。
    /// </summary>
    public void KeepLater(long seq, NotifyAction later) => _late[seq] = later;

    /// <summary>「後で答える」を取り出す。取り出せるのは 1 回だけ（2 回目からは null）。</summary>
    public NotifyAction? TakeLater(long seq) => _late.TryRemove(seq, out var later) ? later : null;

    private void Update(long seq, Func<HistoryEntry, HistoryEntry> change)
    {
        HistoryEntry? next = null;
        lock (_gate)
        {
            for (var node = _entries.First; node is not null; node = node.Next)
            {
                if (node.Value.Seq != seq) continue;

                var changed = change(node.Value);
                if (changed == node.Value) return;

                node.Value = next = changed;
                break;
            }
        }

        if (next is not null) broadcast.Push("notifyHistory", Payload(next));
    }

    private object Payload(HistoryEntry e) => new
    {
        seq = e.Seq,
        type = e.Type,
        kind = NotificationManager.KindPayload(e.Kind),
        title = e.Title,
        body = e.Body,
        at = e.At.ToString("O"),
        state = e.State,
        confirmLabel = e.ConfirmLabel,
        later = _late.ContainsKey(e.Seq),
    };
}
