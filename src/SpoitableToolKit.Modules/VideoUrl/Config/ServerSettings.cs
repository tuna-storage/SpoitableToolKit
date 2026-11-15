using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Config.Attributes;

namespace SpoitableToolKit.Modules.VideoUrl.Config;

/// <summary>
/// 外のサーバーに解決させるときの設定。
/// <para>
/// <b>入にしたら外だけを使う</b>。手元へ落ちないので、
/// 失敗したときに「どちらが壊れているか」が一意になる。そもそも手元に道具を
/// 置きたくない人のための機能なので、落ち先が手元だと意味が薄い。
/// </para>
/// </summary>
public sealed class ServerSettings : ITogglableSettings
{
    [SpoitableSettingsLabelLocalize("videourl.settings.server.enabled.label")]
    [SpoitableSettingsDescriptionLocalize("videourl.settings.server.enabled.description")]
    public bool Enabled { get; set; }

    [SpoitableSettingsLabelLocalize("videourl.settings.server.url.label")]
    [SpoitableSettingsDescriptionLocalize("videourl.settings.server.url.description")]
    public string Url = "";

    /// <summary>
    /// サーバーが要求する token。<b>空なら付けずに投げる</b>。
    /// <para>
    /// 開いたまま置いたサーバーは、任意の URL に対して yt-dlp を走らせる踏み台になる。
    /// 塞ぐなら共有の token を <c>Authorization: Bearer</c> で要求する形になるが、
    /// <b>要求しないサーバーもある</b>（待ち受けを localhost に寄せて SSH のトンネル越しに
    /// 使う形など）。空を「付けない」に割り当てておけば、どちらも同じ欄で足りる。
    /// </para>
    /// <para>
    /// <b>伏せるのは画面だけ。</b> 値は config.json に生のまま入る。
    /// </para>
    /// </summary>
    [SpoitableSettingsLabelLocalize("videourl.settings.server.token.label")]
    [SpoitableSettingsDescriptionLocalize("videourl.settings.server.token.description")]
    [SpoitableSettingsSecret]
    public string Token = "";

    /// <summary>返事を待つ秒数。<b>手元より遅いことがある</b>ので、既定は長めにしてある。</summary>
    [SpoitableSettingsLabelLocalize("videourl.settings.server.timeout.label")]
    [SpoitableSettingsDescriptionLocalize("videourl.settings.server.timeout.description")]
    [SpoitableSettingsRange(5, 300)]
    public int TimeoutSeconds = 60;
}
