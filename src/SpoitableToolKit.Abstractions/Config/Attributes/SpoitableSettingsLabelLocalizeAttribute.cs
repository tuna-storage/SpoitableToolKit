namespace SpoitableToolKit.Abstractions.Config.Attributes;

/// <summary>
/// 設定画面に出す名前を<b>翻訳のキーで</b>指定する。
/// <see cref="SpoitableSettingsLabelAttribute"/> と両方付いていたら、こちらが勝つ。
/// <para>
/// キーが辞書に無ければ<b>キーがそのまま画面に出る</b>。訳が抜けていることを隠さないため。
/// 翻訳を持たないモジュールは、直書きの
/// <see cref="SpoitableSettingsLabelAttribute"/> を使えばよい。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class)]
public sealed class SpoitableSettingsLabelLocalizeAttribute(string key) : Attribute
{
    public string Key { get; } = key;
}
