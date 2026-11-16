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

    /// <summary>
    /// 待ち受けポートのすぐ下の「ファイアウォール」の行。値ではない（保存しない）。結ぶのは <see cref="HrsModule"/> の OnInit。
    /// 状態は適用済みの入/切とポートの話なので、どちらかを打ち替えて適用する前は、ボタンの代わりに「変更を適用すると使えます」が出る。
    /// </summary>
    [SpoitableSettingsLabelLocalize("hrs.settings.hds.firewall.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.hds.firewall.description")]
    public readonly SpoitableSettingsAction Firewall = new(dependsOn: [nameof(Enabled), nameof(Port)]);

    [SpoitableSettingsLabelLocalize("hrs.settings.hds.noSignalSeconds.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.hds.noSignalSeconds.description")]
    [SpoitableSettingsRange(10, 600)]
    public int NoSignalSeconds = 60;
}
