namespace SpoitableToolKit.Host.Config.Attributes;

/// <summary>
/// 設定画面で、この項目だけの編集のしかたを使う（汎用の欄では足りないもの）。画面へは <c>editor</c> として渡る。
/// 本体の設定だけが使う（internal）。編集のしかたはシェルが持っているので、モジュールが名乗っても描く相手がいない。
/// 保存の形は変わらない。
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
internal sealed class SettingsEditorAttribute(string name) : Attribute
{
    /// <summary>
    /// 手首のショートカット: 欄は「並べ替える…」のボタンだけで、押すとモーダル（左 = 手首に並ぶ順・真ん中 = 出どころが名乗るもの・右 = 出どころ）を開く。
    /// </summary>
    public const string WristShortcuts = "wristShortcuts";

    public string Name { get; } = name;
}
