using System.Text.Json;

namespace SpoitableToolKit.Host.Ui.LogView;

/// <summary>
/// JS から届くページの注文。
/// ⚠ <see cref="JsonSerializerDefaults.Web"/> で読むこと。既定は大文字小文字を区別するので、JS の <c>{"limit":100}</c> が <see cref="Limit"/> に入らず、既定値のまま黙って動く（件数を選んでも何も変わらない形でしか表に出ない）。
/// </summary>
internal sealed record LogPageRequest
{
    public int Limit { get; init; } = 500;

    public long? BeforeId { get; init; }

    /// <summary>画面で畳んでいる種別。SQL 側で除く（件数を見える行で数えるため）。</summary>
    public string[] ExcludeKinds { get; init; } = [];

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public bool IncludeLines { get; init; }

    /// <summary>
    /// 生行の上端（これ未満）。半開にするのは、継ぎ目の 1 秒が 2 ページに出ないようにするため（VRChat の時刻は秒までで、同じ秒に何行も並ぶ）。
    /// 最初のページでは null で上端を開けておく — 閉じると、最新のイベントより後の生行を引き直すたびに落とす。
    /// </summary>
    public DateTimeOffset? LinesBefore { get; init; }
}
