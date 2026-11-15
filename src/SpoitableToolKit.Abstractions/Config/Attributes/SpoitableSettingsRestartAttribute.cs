namespace SpoitableToolKit.Abstractions.Config.Attributes;

/// <summary>
/// <b>効くのは次の起動から</b>の設定。設定画面の項目名の横に「次の起動から」の札が付く
/// （説明文の中に書くだけだと、適用を押したあとに効いていないのが不具合に見える）。
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class SpoitableSettingsRestartAttribute : Attribute;
