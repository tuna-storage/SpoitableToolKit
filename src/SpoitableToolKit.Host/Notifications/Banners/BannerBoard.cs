using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Host.Notifications.History;
using SpoitableToolKit.Host.Ui.Push;

namespace SpoitableToolKit.Host.Notifications.Banners;

/// <summary>消すまで出続ける帯。画面がまだ無くても消えないので、後から追いつける。</summary>
internal sealed class BannerBoard(UiBroadcast broadcast, NotificationHistory history, ILogger logger, Action<NotifyAction> run)
{
    /// <summary>出ているバナー。消すのは利用者だけなので、時計も取っ手も無い。</summary>
    private readonly ConcurrentDictionary<long, Banner> _banners = new();

    private long _lastBannerId;

    public void Push(NotifyKind kind, string title, string? body, NotifyAction[] actions)
    {
        logger.LogWarning("Banner: {Title} ({Body})", title, body);

        history.Record("banner", kind, title, body);
        var banner = new Banner(Interlocked.Increment(ref _lastBannerId), kind, title, body, actions);
        _banners[banner.Id] = banner;

        broadcast.Push("banner", banner.Payload());
    }

    public void Close(long id)
    {
        if (!_banners.TryRemove(id, out _)) return;

        broadcast.Push("bannerClosed", new { id });
    }

    /// <summary>モジュールの読み込みは画面より先に走るので、そこで出た分はここでしか拾えない。</summary>
    public object[] Live() => [.. _banners.Values.Select(b => b.Payload())];

    public void Invoke(long id, int index)
    {
        if (!_banners.TryGetValue(id, out var banner)) return;
        if (NotificationManager.ActionAt(banner.Actions, index) is not { } action) return;

        Close(id);
        run(action);
    }

    /// <summary>出した 1 本。出したあとは変わらないので、錠も要らない。</summary>
    private sealed record Banner(long Id, NotifyKind Kind, string Title, string? Body, NotifyAction[] Actions)
    {
        public object Payload() => new
        {
            id = Id,
            kind = NotificationManager.KindPayload(Kind),
            title = Title,
            body = Body,
            actions = Actions.Select((a, i) => new { index = i, label = a.Label }).ToArray(),
        };
    }
}
