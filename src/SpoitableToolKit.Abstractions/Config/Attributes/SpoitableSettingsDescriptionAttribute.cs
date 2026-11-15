namespace SpoitableToolKit.Abstractions.Config.Attributes;

/// <summary>
/// ラベルの下に出す補足。何のための設定かが名前だけで分からないときに。
/// <b>入れ子の設定のクラスにも付けられる</b>（メンバー側が勝つのはラベルと同じ）。
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class)]
public sealed class SpoitableSettingsDescriptionAttribute(string description) : Attribute
{
    public string Description { get; } = description;
}
