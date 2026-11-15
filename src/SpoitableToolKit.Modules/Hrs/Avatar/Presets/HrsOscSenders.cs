using SpoitableToolKit.Modules.Hrs.Config;

namespace SpoitableToolKit.Modules.Hrs.Avatar.Presets;

internal static class HrsOscSenders
{
    /// <summary>
    /// 知らない値（設定ファイルを手で書き換えた、など）は VRCHR に倒す。<b>何も送らない形にはしない</b> —
    /// 送らないと、アバターは最後に受けた心拍を出したままになる。
    /// </summary>
    public static IHrsOscSender For(HrsConfig config) => config.OscPreset switch
    {
        HrsOscPreset.VRCOSC => VrcoscSender.From(config.VrcoscParameters),
        _ => VrchrSender.From(config.Parameters),
    };
}
