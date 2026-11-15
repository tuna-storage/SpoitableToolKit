namespace SpoitableToolKit.Abstractions.Config.Attributes;

/// <summary>
/// 設定画面で<b>見出しを付けて束ねる</b>（「画面」「通知」「VR・手首」など）。<b>見せ方だけ</b>で、保存の形は変わらない。
/// <para>
/// 入れ子（<see cref="ISpoitableSettings"/>）にすると config.json の形が変わって、<b>前に打った値が読めなくなる</b>。
/// 平らなまま並びを分けたいときはこちらを使う。同じ名前が続く項目が 1 つの束になり、設定画面のモジュールのページの右の目次にも出る。
/// 入れ物（<see cref="ISpoitableSettings"/>）のメンバーに付けると、中の項目ごと同じ束に入る（中の項目に付けた印は効かない）。
/// </para>
/// </summary>
/// <param name="key">見出しの翻訳キー。辞書に無ければキーがそのまま出る。</param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class SpoitableSettingsGroupAttribute(string key) : Attribute
{
    public string Key { get; } = key;
}
