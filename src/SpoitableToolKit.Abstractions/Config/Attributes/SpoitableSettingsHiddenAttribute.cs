namespace SpoitableToolKit.Abstractions.Config.Attributes;

/// <summary>
/// 保存はするが設定画面には出さない。利用者が触るものではない内部状態
/// （最後に確認した時刻、キャッシュの世代など）に使う。
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class SpoitableSettingsHiddenAttribute : Attribute;
