namespace SpoitableToolKit.Abstractions.Config.Attributes;

/// <summary>
/// 文字列の一覧を、<b>候補ごとのスイッチ</b>で見せる（打たせない）。一覧は「切ってあるもの」（隠す一覧）として持つ。
/// 候補は <see cref="IModuleConfig.SuggestValuesFor"/> から出す。候補に無い値が入っていても消さない。
/// <para>
/// 「サイドバーに出さない状態」「手首の帯に出さないもの」のように、<b>選べるものが決まっていて名前を打つ意味が無い</b>一覧に使う。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class SpoitableSettingsSwitchesAttribute : Attribute;
