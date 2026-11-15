namespace SpoitableToolKit.Abstractions.Config.Attributes;

/// <summary>
/// 保存に使う名前。未指定ならメンバー名。
/// <para>
/// メンバー名から切り離しておくと、C# 側の名前を変えても保存済みの値が飛ばない。
/// 逆に、名前を変えてしまったあとから追いつくには <see cref="SpoitableSettingsFormerKeyAttribute"/>。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class SpoitableSettingsKeyAttribute(string key) : Attribute
{
    public string Key { get; } = key;
}
