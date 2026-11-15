using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Config.Attributes;

namespace SpoitableToolKit.Modules.Hrs.Config;

/// <summary>
/// VRCOSC のプリセットで送る 9 個の名前。
/// 既定の名前に <c>/</c> が入っている（<c>VRCOSC/Heartrate/Value</c>）。VRChat ではこれで 1 つのパラメータ名なので、
/// 名前の検証は <c>/</c> を拒まない（<see cref="Avatar.ParameterNames.ErrorOf"/>）。
/// </summary>
/// <param name="owner">constructor で結ぶ（<see cref="IConditionalSettings"/> の決まり）。</param>
[SpoitableSettingsLabelLocalize("hrs.settings.vrcosc.label")]
[SpoitableSettingsDescriptionLocalize("hrs.settings.vrcosc.description")]
[SpoitableSettingsCategory(SpoitableCategory.Foldable)]
public sealed class VrcoscParameterSettings(HrsConfig owner) : IConditionalSettings
{
    public bool IsEnabled => owner.OscPreset == HrsOscPreset.VRCOSC;

    [SpoitableSettingsLabelLocalize("hrs.settings.vrcosc.connected.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.vrcosc.connected.description")]
    public string Connected = "VRCOSC/Heartrate/Connected";

    [SpoitableSettingsLabelLocalize("hrs.settings.vrcosc.value.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.vrcosc.value.description")]
    public string Value = "VRCOSC/Heartrate/Value";

    [SpoitableSettingsLabelLocalize("hrs.settings.vrcosc.normalised.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.vrcosc.normalised.description")]
    public string Normalised = "VRCOSC/Heartrate/Normalised";

    [SpoitableSettingsLabelLocalize("hrs.settings.vrcosc.average.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.vrcosc.average.description")]
    public string Average = "VRCOSC/Heartrate/Average";

    [SpoitableSettingsLabelLocalize("hrs.settings.vrcosc.beat.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.vrcosc.beat.description")]
    public string Beat = "VRCOSC/Heartrate/Beat";

    [SpoitableSettingsLabelLocalize("hrs.settings.vrcosc.enabled.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.vrcosc.enabled.description")]
    public string Enabled = "VRCOSC/Heartrate/Enabled";

    [SpoitableSettingsLabelLocalize("hrs.settings.vrcosc.units.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.vrcosc.digit.description")]
    public string Units = "VRCOSC/Heartrate/Units";

    [SpoitableSettingsLabelLocalize("hrs.settings.vrcosc.tens.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.vrcosc.digit.description")]
    public string Tens = "VRCOSC/Heartrate/Tens";

    [SpoitableSettingsLabelLocalize("hrs.settings.vrcosc.hundreds.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.vrcosc.digit.description")]
    public string Hundreds = "VRCOSC/Heartrate/Hundreds";
}
