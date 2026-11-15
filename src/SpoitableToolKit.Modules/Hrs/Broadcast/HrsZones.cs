using SpoitableToolKit.Modules.Hrs.Config;

namespace SpoitableToolKit.Modules.Hrs.Broadcast;

/// <summary>
/// 心拍の帯。配信に載せる <c>zone</c> はこれで、オーバーレイの色と文字はこの 1 語で決まる。
/// しきい値は設定で変えられる（安静時の心拍は人によって 20 以上違うので、決め打ちだと座っているだけで light に居続ける人が出る）。
/// </summary>
internal static class HrsZones
{
    /// <summary>帯の名前。既存のオーバーレイがこの 4 語の文字列で色を引いている。</summary>
    public static string Of(HrsConfig config, int bpm) =>
        bpm >= config.Zones.HardBpm ? "hard"
        : bpm >= config.Zones.ModerateBpm ? "moderate"
        : bpm >= config.Zones.LightBpm ? "light"
        : "rest";

    /// <summary>
    /// 順番が崩れていないか。崩れると上の帯に二度と入れない（moderate ≧ hard なら hard の条件を満たす心拍は必ず先に moderate で拾われる）。
    /// 返すのは理由のキーだけ（下の層で日本語を組むと翻訳できない）。
    /// </summary>
    public static string? ErrorIn(HrsConfig config) =>
        config.Zones.LightBpm < config.Zones.ModerateBpm && config.Zones.ModerateBpm < config.Zones.HardBpm
            ? null
            : "hrs.error.zoneOrder";
}
