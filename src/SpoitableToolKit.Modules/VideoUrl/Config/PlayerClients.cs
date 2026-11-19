using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Config.Attributes;

namespace SpoitableToolKit.Modules.VideoUrl.Config;

/// <summary>
/// yt-dlp に名乗らせるクライアント（<c>--extractor-args youtube:player_client=…</c>）。
/// <para>
/// <b>取れるものがクライアントごとに違う</b>ので 3 本に分けてある。既定は実測で決めたもので、
/// 普通は触らなくてよい。<b>畳んであるのはそのため</b>。
/// </para>
/// <para>
/// <b>空にするとその 1 本を頼まない。</b> 要らないものを空にすれば、要求を減らせる。
/// </para>
/// </summary>
[SpoitableSettingsCategory(SpoitableCategory.Foldable)]
[SpoitableSettingsLabelLocalize("videourl.settings.clients.label")]
[SpoitableSettingsDescriptionLocalize("videourl.settings.clients.description")]
public sealed class PlayerClients : ISpoitableSettings
{
    /// <summary>
    /// HLS を取るクライアント。<b>HLS の muxed（itag 91〜96 / 300 / 301）を出すのはここだけ</b>
    /// （web / ios / tv / android はどれも出さない）。
    /// <b>ここから progressive を取ってはいけない</b>（下記の通り 403 になる）。
    /// </summary>
    [SpoitableSettingsLabelLocalize("videourl.settings.clients.hls.label")]
    [SpoitableSettingsDescriptionLocalize("videourl.settings.clients.hls.description")]
    public string Hls = "web_safari";

    /// <summary>
    /// progressive な muxed（itag 18）を取るクライアント。
    /// <para>
    /// <b>クライアントによって、取れた URL が素の GET で弾かれる。</b> itag 18 が返ってくるので
    /// 直ったように見えるのが厄介で、yt-dlp 自身は正しいヘッダを付けるため手元では通ってしまい、
    /// URL だけ渡す VRChat のプレイヤーで初めて死ぬ。実測（同じ動画・同じ itag 18）:
    /// </para>
    /// <code>
    /// android      → HTTP 206
    /// mweb         → HTTP 403
    /// web_safari   → HTTP 403
    /// </code>
    /// </summary>
    [SpoitableSettingsLabelLocalize("videourl.settings.clients.progressive.label")]
    [SpoitableSettingsDescriptionLocalize("videourl.settings.clients.progressive.description")]
    public string Progressive = "android";

    /// <summary>
    /// 音声だけ（itag 140 の m4a、251 の webm）を取るクライアント。<c>default</c> は yt-dlp の既定の組み合わせを指す名前で、
    /// yt-dlp の版が変わっても、その版の既定に付いていく。
    /// <para>
    /// <b>音声だけを返すのは、いまのところ <c>default</c> だけ。</b> 実測（yt-dlp 2026.08.19、3 本の動画）:
    /// </para>
    /// <code>
    /// default        → 音声だけ 5〜120 本、素の GET で 206（途中の位置も、全体も取れた）
    /// web_safari・android・ios・tv・mweb・web・web_embedded → 0 本
    /// </code>
    /// </summary>
    [SpoitableSettingsLabelLocalize("videourl.settings.clients.audio.label")]
    [SpoitableSettingsDescriptionLocalize("videourl.settings.clients.audio.description")]
    public string Audio = "default";
}
