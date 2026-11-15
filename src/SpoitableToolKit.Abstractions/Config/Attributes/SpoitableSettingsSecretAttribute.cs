namespace SpoitableToolKit.Abstractions.Config.Attributes;

/// <summary>
/// 画面では伏せて打たせる（打った字が見えない）。token や合鍵のような、
/// <b>肩越しに読まれたくない</b>文字列に付ける。
/// <para>
/// <b>保存は生のまま</b>で、設定ファイルを開けば読める。隠しているのは画面だけで、
/// 守っているのは「配信に映り込む」「隣から見える」までの範囲。
/// 暗号化の代わりにはならない。
/// </para>
/// <para>
/// 文字列にだけ効く。ほかの型に付けても、<b>その型の描き方が変わらないだけ</b>で害は無い。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class SpoitableSettingsSecretAttribute : Attribute;
