using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Config.Attributes;

namespace SpoitableToolKit.Modules.Hrs.Config;

/// <summary>Apple Watch（Health Data Server）から受けるときの設定。</summary>
[SpoitableSettingsLabelLocalize("hrs.settings.hds.label")]
[SpoitableSettingsDescriptionLocalize("hrs.settings.hds.description")]
public sealed class HdsSettings : ITogglableSettings
{
    public bool Enabled { get; set; }

    [SpoitableSettingsLabelLocalize("hrs.settings.hds.port.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.hds.port.description")]
    [SpoitableSettingsRange(1, 65535)]
    public int Port = 3476;

    [SpoitableSettingsLabelLocalize("hrs.settings.hds.noSignalSeconds.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.hds.noSignalSeconds.description")]
    [SpoitableSettingsRange(10, 600)]
    public int NoSignalSeconds = 60;
}
