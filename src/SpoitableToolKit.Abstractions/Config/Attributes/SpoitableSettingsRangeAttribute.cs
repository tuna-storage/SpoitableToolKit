namespace SpoitableToolKit.Abstractions.Config.Attributes;

/// <summary>
/// 数値の範囲。画面の入力を絞るだけでなく、<b>ホスト側でも丸める</b>
/// （画面を通さずに設定ファイルを直接書き換えられるため）。
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class SpoitableSettingsRangeAttribute(double minimum, double maximum) : Attribute
{
    public double Minimum { get; } = minimum;

    public double Maximum { get; } = maximum;
}
