using SpoitableToolKit.Abstractions.Events.Config;

namespace SpoitableToolKit.Abstractions.Config;

/// <summary>
/// <b>出す条件を式で書く設定。</b> 入/切のスイッチではなく、
/// 他の項目を見て決めたいときに使う。
/// <code>
/// sealed class ServerSettings(Config owner) : IConditionalSettings
/// {
///     public string Url = "";
///     public bool IsEnabled =&gt; owner.Mode == ResolveMode.Server;
/// }
/// </code>
/// <para>
/// <b>画面には何も足さない。</b> スイッチが要るなら <see cref="ITogglableSettings"/>。
/// </para>
/// <para>
/// <b>切の間は見出しも出ない</b>（上の例なら <c>Mode</c> を変えれば戻せる）。条件を決めるスイッチを入れ物の中に置くときは
/// <see cref="Attributes.SpoitableSettingsToggleAttribute"/> を付ける。付けた入れ物は、切の間も見出しとそのスイッチが残る。
/// </para>
/// <para>
/// <b>親へは constructor（か初期化子 <c>= new(this)</c>）で結ぶ。</b> 設定画面の下見と、変える前に listener へ見せる写し
/// （<see cref="ConfigChangingEventParams{TConfig}.Proposed"/>）は、ホストが <c>new</c> で作り直して値を写したもの。
/// あとから張った参照（OnInit で親を入れる、static の参照など）は写しに付かず、写しの <see cref="IsEnabled"/> が本物の親を見てしまう。
/// </para>
/// </summary>
public interface IConditionalSettings : ISpoitableSettings
{
    /// <summary>
    /// 中の項目を出すか。<b>設定画面を描くたびに読まれる</b>ので、
    /// 他のメンバーを見て決めてよい（重い処理は書かないこと）。
    /// </summary>
    bool IsEnabled { get; }
}
