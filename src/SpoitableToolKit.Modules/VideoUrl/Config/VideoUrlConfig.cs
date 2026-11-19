using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Config.Attributes;

namespace SpoitableToolKit.Modules.VideoUrl.Config;

/// <summary>
/// videourl の設定。ホストが reflection で読んで設定画面を組み立てる。
/// インスタンスはホストと共有しているので、モジュールは使うときに読むだけでよい。
/// 変更を契機に何かする必要が無いので <c>IConfigEventListener</c> は実装していない。
/// </summary>
public sealed class VideoUrlConfig : IModuleConfig
{
    // 画面の順は reflection の順（プロパティが先、フィールドが後）なので、書く順もそれに合わせてある。

    /// <summary>手元の yt-dlp ではなく、外のサーバーに解決させる。</summary>
    [SpoitableSettingsGroup("videourl.settings.group.server")]
    [SpoitableSettingsLabelLocalize("videourl.settings.server.label")]
    [SpoitableSettingsDescriptionLocalize("videourl.settings.server.description")]
    public ServerSettings Server { get; } = new();

    /// <summary>
    /// どのクライアントを名乗って取りに行くか。<b>手元でも外でも同じものを使う</b>。
    /// </summary>
    [SpoitableSettingsGroup("videourl.settings.group.clients")]
    public PlayerClients Clients { get; } = new();

    [SpoitableSettingsGroup("videourl.settings.group.confirm")]
    [SpoitableSettingsLabelLocalize("videourl.settings.askOnFailure.label")]
    [SpoitableSettingsDescriptionLocalize("videourl.settings.askOnFailure.description")]
    public bool AskOnFailure = true;

    /// <summary>
    /// 打ち込む欄の下に出す候補。<b>選択肢ではない</b>ので、ここに無い名前も打てる
    /// （yt-dlp が増やしたときに、こちらを直さなくても使える）。
    /// <para>
    /// <b>宛先は繋いだ名前で届く</b>。<c>nameof</c> は最後の名前しか返さないので、
    /// 片方だけ書くと一致しないまま静かに候補が出なくなる。
    /// </para>
    /// </summary>
    public IReadOnlyList<string>? SuggestValuesFor(string memberName) =>
        memberName == $"{nameof(Clients)}.{nameof(PlayerClients.Hls)}"
        || memberName == $"{nameof(Clients)}.{nameof(PlayerClients.Progressive)}"
        || memberName == $"{nameof(Clients)}.{nameof(PlayerClients.Audio)}"
            ? KnownClients
            : null;

    /// <summary>実測で見たことのある名前。<b>網羅ではない</b>。<c>default</c> は yt-dlp の既定の組み合わせ。</summary>
    private static readonly string[] KnownClients =
        ["web_safari", "android", "default", "ios", "tv", "mweb", "web", "web_embedded"];
}
