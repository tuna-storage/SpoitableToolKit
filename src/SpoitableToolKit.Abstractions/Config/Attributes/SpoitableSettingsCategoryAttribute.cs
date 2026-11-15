namespace SpoitableToolKit.Abstractions.Config.Attributes;

/// <summary>
/// 入れ子の設定の置き方。<b>クラスに付ける。</b>
/// <code>
/// [SpoitableSettingsLabelLocalize("hrs.settings.parameters.label")]
/// [SpoitableSettingsCategory(SpoitableCategory.Foldable)]
/// public sealed class ParameterSettings : ISpoitableSettings
/// {
///     public string Ones = "onesHR";
///     public string Tens = "tensHR";
/// }
/// </code>
/// <para>
/// <see cref="ITogglableSettings"/> と重ねてよい。見出しにチェックと開閉の両方が出て、
/// 中身は<b>入かつ畳んでいない</b>ときだけ見える。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SpoitableSettingsCategoryAttribute(SpoitableCategory category) : Attribute
{
    public SpoitableCategory Category { get; } = category;
}
