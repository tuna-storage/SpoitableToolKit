using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Config.Attributes;

namespace SpoitableToolKit.Modules.Hrs.Config;

/// <summary>オーバーレイに出る帯（rest / light / moderate / hard）の境目。入/切は持たない（帯は常に要る）。</summary>
[SpoitableSettingsLabelLocalize("hrs.settings.zones.label")]
[SpoitableSettingsDescriptionLocalize("hrs.settings.zones.description")]
[SpoitableSettingsCategory(SpoitableCategory.Foldable)]
public sealed class ZoneSettings : ISpoitableSettings
{
    [SpoitableSettingsLabelLocalize("hrs.settings.zones.light.label")]
    [SpoitableSettingsRange(1, 400)]
    public int LightBpm = 100;

    [SpoitableSettingsLabelLocalize("hrs.settings.zones.moderate.label")]
    [SpoitableSettingsRange(1, 400)]
    public int ModerateBpm = 120;

    [SpoitableSettingsLabelLocalize("hrs.settings.zones.hard.label")]
    [SpoitableSettingsRange(1, 400)]
    public int HardBpm = 140;
}
