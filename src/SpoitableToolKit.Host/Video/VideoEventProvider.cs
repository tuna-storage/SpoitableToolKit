using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.Events.Video;
using SpoitableToolKit.Abstractions.Events.VrcLog;
using SpoitableToolKit.Abstractions.VrcLog;

namespace SpoitableToolKit.Host.Video;

/// <summary>
/// 構造化ログイベントを、動画というドメインの言葉に翻訳して、動画の口（<see cref="Events.HostEvents.Videos"/>）から配る。
/// <para>
/// 「同じ URL で何回目の失敗か」のように複数行・複数イベントにまたがる状態はここが持つ。
/// パーサは 1 行の解釈だけに専念させ、意味の積み上げはこの層でやる。
/// </para>
/// </summary>
internal sealed class VideoEventProvider : IVrcLogEventListener, IDisposable
{
    /// <summary>この時間を超えて間が空いたら、同じ URL でも別の試行として数え直す。</summary>
    private static readonly TimeSpan AttemptGrouping = TimeSpan.FromMinutes(5);

    /// <summary>URL ごとの状態を無限に抱えないための上限。古いものから捨てる。</summary>
    private const int MaxTrackedUrls = 64;

    private readonly IVrcLogBus _logBus;
    private readonly IEventHub _hub;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Attempt> _attempts = new(StringComparer.Ordinal);

    private sealed class Attempt
    {
        public int Count;
        public DateTimeOffset LastSeen;
    }

    public VideoEventProvider(IVrcLogBus logBus, IEventHub hub)
    {
        _logBus = logBus;
        _hub = hub;
        _logBus.AddListener<IVrcLogEventListener>(Events.HostOwner.Instance, this);
    }

    void IVrcLogEventListener.OnLogEvent(VrcLogEventParams e) => OnLogEvent(e);

    private void OnLogEvent(VrcLogEventParams logEvent)
    {
        switch (logEvent)
        {
            case VideoPlaybackFailed failed:
                var repeated = CountAttempt(failed.Url, failed.Timestamp) > 1;
                var failure = new VideoPlaybackFailedEventParams
                {
                    Video = new VideoPlaybackInfo
                    {
                        Url = failed.Url,
                        ResolvedUrl = failed.ResolvedUrl,
                        Reason = failed.Reason,
                        AttemptCount = CurrentCount(failed.Url),
                        Timestamp = failed.Timestamp,
                    },
                    IsRepeatedFail = repeated,
                };
                _hub.Fire<IVideoEventListener>(l => l.OnVideoPlaybackFailed(failure));
                break;

            case VideoPlaybackStarted started:
                // 再生できたので、この URL の失敗回数は忘れる。
                // 同じ動画をあとでもう一度かけたときに「2 回目の失敗」扱いにしない。
                var count = Forget(started.Url);
                var start = new VideoPlaybackStartedEventParams
                {
                    Video = new VideoPlaybackInfo
                    {
                        Url = started.Url,
                        ResolvedUrl = started.ResolvedUrl,
                        AttemptCount = count + 1,
                        Timestamp = started.Timestamp,
                    },
                };
                _hub.Fire<IVideoEventListener>(l => l.OnVideoPlaybackStarted(start));
                break;

            case WorldJoined:
                // ワールドを移ったら持ち越さない。
                lock (_gate) _attempts.Clear();
                break;
        }
    }

    private int CountAttempt(string url, DateTimeOffset timestamp)
    {
        lock (_gate)
        {
            if (_attempts.TryGetValue(url, out var attempt) && timestamp - attempt.LastSeen <= AttemptGrouping)
            {
                attempt.Count++;
                attempt.LastSeen = timestamp;
                return attempt.Count;
            }

            if (_attempts.Count >= MaxTrackedUrls)
            {
                var oldest = _attempts.MinBy(pair => pair.Value.LastSeen).Key;
                _attempts.Remove(oldest);
            }

            _attempts[url] = new Attempt { Count = 1, LastSeen = timestamp };
            return 1;
        }
    }

    private int CurrentCount(string url)
    {
        lock (_gate) return _attempts.TryGetValue(url, out var attempt) ? attempt.Count : 1;
    }

    private int Forget(string url)
    {
        lock (_gate)
        {
            if (!_attempts.Remove(url, out var attempt)) return 0;
            return attempt.Count;
        }
    }

    public void Dispose() => _logBus.RemoveListener<IVrcLogEventListener>(Events.HostOwner.Instance, this);
}
