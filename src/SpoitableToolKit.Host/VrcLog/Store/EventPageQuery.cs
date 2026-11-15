namespace SpoitableToolKit.Host.VrcLog.Store;

/// <summary>
/// 畳んでいる種別は SQL 側で除く。実データの 92% が <c>ApiRequest</c>（既定で畳む）なので、
/// 除かずに 500 行取ると見える行は 10 件ほどにしかならない。
/// 件数のドロップダウンは「見える行の件数」として意味を持たせる。
/// </summary>
internal sealed record EventPageQuery
{
    public int Limit { get; init; } = 500;

    /// <summary>この id より古いものだけ。null なら新しい方から。</summary>
    public long? BeforeId { get; init; }

    public IReadOnlyList<string> ExcludeKinds { get; init; } = [];

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }
}
