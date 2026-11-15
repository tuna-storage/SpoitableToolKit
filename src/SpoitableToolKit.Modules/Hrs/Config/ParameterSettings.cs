using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Config.Attributes;

namespace SpoitableToolKit.Modules.Hrs.Config;

/// <summary>
/// VRCHR のプリセットで送る 9 個の名前（設定の鍵は <c>Parameters</c>）。
/// 入/切は持たない（切れる形にすると「名前が消えた」のか「送っていない」のか区別が付かない）。
/// </summary>
/// <param name="owner">constructor で結ぶ（<see cref="IConditionalSettings"/> の決まり）。</param>
[SpoitableSettingsLabelLocalize("hrs.settings.parameters.label")]
[SpoitableSettingsDescriptionLocalize("hrs.settings.parameters.description")]
[SpoitableSettingsCategory(SpoitableCategory.Foldable)]
public sealed class ParameterSettings(HrsConfig owner) : IConditionalSettings
{
    /// <summary>
    /// VRCOSC でなければ出す。知らない値（設定ファイルを手で書き換えた）のときも VRCHR で送るので
    /// （<see cref="Avatar.Presets.HrsOscSenders"/>）、その名前の欄も出す。<c>== VRCHR</c> にすると、送っている名前の欄がどこにも出ない。
    /// </summary>
    public bool IsEnabled => owner.OscPreset != HrsOscPreset.VRCOSC;

    [SpoitableSettingsLabelLocalize("hrs.settings.parameters.ones.label")]
    public string Ones = "onesHR";

    [SpoitableSettingsLabelLocalize("hrs.settings.parameters.tens.label")]
    public string Tens = "tensHR";

    [SpoitableSettingsLabelLocalize("hrs.settings.parameters.hundreds.label")]
    public string Hundreds = "hundredsHR";

    [SpoitableSettingsLabelLocalize("hrs.settings.parameters.isConnected.label")]
    public string IsConnected = "isHRConnected";

    [SpoitableSettingsLabelLocalize("hrs.settings.parameters.isActive.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.parameters.isActive.description")]
    public string IsActive = "isHRActive";

    [SpoitableSettingsLabelLocalize("hrs.settings.parameters.isBeat.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.parameters.isBeat.description")]
    public string IsBeat = "isHRBeat";

    [SpoitableSettingsLabelLocalize("hrs.settings.parameters.percent.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.parameters.percent.description")]
    public string Percent = "HRPercent";

    [SpoitableSettingsLabelLocalize("hrs.settings.parameters.fullPercent.label")]
    [SpoitableSettingsDescriptionLocalize("hrs.settings.parameters.fullPercent.description")]
    public string FullPercent = "FullHRPercent";

    [SpoitableSettingsLabelLocalize("hrs.settings.parameters.heartRate.label")]
    public string HeartRate = "HR";
}
