namespace SpoitableToolKit.Host.Updates;

/// <param name="Downgrade">チャンネルを変えて版が下がるもの（nightly → stable）。</param>
/// <param name="Notes">変更点（Markdown。書かれていなければ空）。</param>
internal sealed record AppUpdateOffer(string Version, bool Downgrade, string Notes)
{
    /// <summary>入れ元が落とす・入れるときに使う物（Velopack の版の情報）。画面へは出さない。</summary>
    public object? Ticket { get; init; }
}
