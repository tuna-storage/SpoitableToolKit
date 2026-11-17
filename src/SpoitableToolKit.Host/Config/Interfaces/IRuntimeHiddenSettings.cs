namespace SpoitableToolKit.Host.Config.Interfaces;

/// <summary>
/// 設定の行を、実行時に決まる印で画面から外す。本体の設定だけが使う（SDK には出さない）。
/// <para>
/// 属性（<see cref="SpoitableToolKit.Abstractions.Config.Attributes.SpoitableSettingsHiddenAttribute"/>）は型で決まる。
/// 入れ物の条件（<see cref="SpoitableToolKit.Abstractions.Config.IConditionalSettings"/>）は写しの上でも読まれ、写しには実行時に入れた印が付かないので、
/// 下見のたびに行が消える。こちらは候補（<see cref="SpoitableToolKit.Abstractions.Config.IModuleConfig.SuggestValuesFor"/>）と同じく、本物の設定に聞く。
/// </para>
/// <para>
/// 外した行は画面へ渡さない。起動している間に変わらない印にだけ使う（変わるなら、溜めてある変更が画面から消える）。保存と適用は今までどおり通る。
/// </para>
/// </summary>
internal interface IRuntimeHiddenSettings
{
    /// <param name="memberName">C# 側の名前（入れ子なら <c>.</c> で繋いだもの）。</param>
    bool IsHidden(string memberName);
}
