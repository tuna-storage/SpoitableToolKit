using SpoitableToolKit.Abstractions.Events.Video;

namespace SpoitableToolKit.Modules.VideoUrl.Failures;

/// <summary>
/// 検出した再生失敗の一覧。<b>パネルではなくここが正</b>で、パネルは開いたときにこれを取りに来る。
/// <para>
/// パネルは開かれていないことのほうが多い（VRChat を見ている間はそもそも窓が無い）。
/// push が届かなかった分もここには残っているので、あとから開いても揃って見える。
/// </para>
/// <para>
/// 状態を持つのはモジュール側だが、触るスレッドは 2 つある。失敗の記録はログ監視のスレッドから、
/// 読み出しは UI からの呼び出し（CEF のスレッド）から来るので、素の <see cref="List{T}"/> は使えない。
/// </para>
/// </summary>
public sealed class FailureLog
{
    /// <summary>抱える上限。常駐アプリなので、放っておくと 1 セッション分伸び続ける。</summary>
    public const int Capacity = 50;

    private readonly Lock _gate = new();

    /// <summary>新しい（＝最後に失敗した）ものが先頭。</summary>
    private readonly List<VideoFailure> _entries = [];

    /// <summary>
    /// 1 回の失敗を記録し、記録後の一覧を返す。
    /// <para>
    /// <b>リトライも数える。</b> 通知は <see cref="VideoPlaybackFailedEventParams.IsRepeatedFail"/> で 1 本 1 回に絞るが、一覧は
    /// 「4 回失敗している」と出したいので全部受ける。回数を
    /// <see cref="VideoPlaybackInfo.AttemptCount"/> ではなく自前で持つのは、ホスト側の数え方は
    /// 再生成功や 5 分の間隔で 1 に戻る（＝リトライの判定用）ため。一覧では減ると不自然になる。
    /// </para>
    /// <para>返した配列をそのまま JSON にして push できるよう、ロックの中で作り切る。</para>
    /// </summary>
    public VideoFailure[] Record(VideoPlaybackInfo info)
    {
        lock (_gate)
        {
            var index = _entries.FindIndex(e => e.Url == info.Url);

            var failures = 1;
            string? reason = info.Reason;

            if (index >= 0)
            {
                var previous = _entries[index];
                failures = previous.Failures + 1;

                // 理由は取れたり取れなかったりする（Unity バックエンドでは常に null）。
                // 一度取れたものを、あとの空で消さない。
                reason ??= previous.Reason;

                _entries.RemoveAt(index);
            }

            _entries.Insert(0, new VideoFailure
            {
                Url = info.Url,
                Reason = reason,
                Failures = failures,
                LastSeen = info.Timestamp,
            });

            if (_entries.Count > Capacity) _entries.RemoveRange(Capacity, _entries.Count - Capacity);

            return [.. _entries];
        }
    }

    public VideoFailure[] Snapshot()
    {
        lock (_gate) return [.. _entries];
    }
}
