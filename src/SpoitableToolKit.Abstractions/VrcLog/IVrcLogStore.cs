namespace SpoitableToolKit.Abstractions.VrcLog;

/// <summary>
/// 本体が残した VRChat のログの記録を読む。
/// <para>
/// VRChat 自身のログは <c>old_logs</c> へ回されて最後は消えるので、本体が読んだ出来事を全部ここへ残している。
/// <b>書くのは本体だけ</b>（モジュールが書けると、本体の記録と見分けが付かなくなる）。
/// </para>
/// </summary>
public interface IVrcLogStore
{
    /// <summary>新しい順に取り出す。<paramref name="kind"/> を渡すとその種別だけ。</summary>
    Task<IReadOnlyList<StoredLogEvent>> QueryRecentAsync(
        int limit, string? kind = null, CancellationToken cancellationToken = default);
}
