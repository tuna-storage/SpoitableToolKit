namespace SpoitableToolKit.Abstractions.Config.Attributes;

/// <summary>
/// ラベルの下に出す補足を、翻訳のキーで指定する。
/// <b>入れ子の設定のクラスにも付けられる</b>（メンバー側が勝つのはラベルと同じ）。
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class)]
public sealed class SpoitableSettingsDescriptionLocalizeAttribute(string key) : Attribute
{
    public string Key { get; } = key;
}
