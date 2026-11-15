using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Config.Attributes;

namespace SpoitableToolKit.Modules.Hrs.Config;

/// <summary>OBS のオーバーレイと、心拍を読む外部ツール向けの口。切っている間はポートを出さない。</summary>
[SpoitableSettingsLabelLocalize("hrs.settings.broadcast.label")]
[SpoitableSettingsDescriptionLocalize("hrs.settings.broadcast.description")]
public sealed class BroadcastSettings : ITogglableSettings
{
    public bool Enabled { get; set; }

    [SpoitableSettingsLabelLocalize("hrs.settings.broadcast.port.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.broadcast.port.description")]
    [SpoitableSettingsRange(1, 65535)]
    public int Port = 9100;
}
