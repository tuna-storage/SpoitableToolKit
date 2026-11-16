namespace SpoitableToolKit.Host.Config.Models;

internal static class ConfigKind
{
    public const string Bool = "bool";
    public const string Integer = "int";
    public const string Number = "number";
    public const string Text = "string";
    public const string Choice = "enum";
    public const string List = "list";

    /// <summary>専用のコントロールが無い型。JSON のテキスト欄として出す（画面から消さない）。</summary>
    public const string Json = "json";

    public const string Section = "section";

    /// <summary>値ではない行（<see cref="SpoitableToolKit.Abstractions.Config.SpoitableSettingsAction"/>）。保存も「適用」もしない。</summary>
    public const string Action = "action";
}
