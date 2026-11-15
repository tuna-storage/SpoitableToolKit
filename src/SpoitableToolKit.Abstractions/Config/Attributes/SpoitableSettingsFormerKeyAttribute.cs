namespace SpoitableToolKit.Abstractions.Config.Attributes;

/// <summary>
/// 昔使っていた保存名。移行のため。複数付けてよい。
/// <para>
/// <b>読むときだけ見る。書くときは常に今の名前。</b> つまり利用者が設定を触らなくても、
/// 次の保存で新しい名前へ移る。<b>移行期間 = この属性が付いている期間</b>で、
/// 作者が次のリリースで外せば終わり。ホストは期限を持たない（持つと状態が増えるだけで、
/// いつ切れたのかを誰も追えなくなる）。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
public sealed class SpoitableSettingsFormerKeyAttribute(string key) : Attribute
{
    public string Key { get; } = key;
}
