namespace SpoitableToolKit.Abstractions.Config.Attributes;

/// <summary>
/// 設定画面に出す名前。未指定ならメンバー名がそのまま出る。
/// <para>
/// <b>入れ子の設定（<see cref="ISpoitableSettings"/>）のクラスにも付けられる。</b>
/// その型を置いたメンバーの見出しになるので、置くたびに書かなくてよい。
/// <b>メンバー側に書いてあればそちらが勝つ</b> — 同じ型を 2 か所に置いたときに、
/// 置いた場所ごとの名前を付けられる方が要るため。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class)]
public sealed class SpoitableSettingsLabelAttribute(string label) : Attribute
{
    public string Label { get; } = label;
}
