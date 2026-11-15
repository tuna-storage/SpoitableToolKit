using SpoitableToolKit.Abstractions.Config;

namespace SpoitableToolKit.Abstractions.Events.Config;

/// <summary>
/// 自分の設定（<see cref="ISpoitableModule.Config"/>）が変わることを受け取る。<b>要るのは、変更を契機に何かしたいときと、値を確かめたいときだけ</b>。
/// <para>
/// ふつうは要らない。設定のインスタンスはホストと共有しているので、値は使うときに読めばよい。
/// 要るのは「デバイスの指定が変わったから繋ぎ直す」（<see cref="OnConfigChanged"/>）や、
/// 「その組み合わせは成り立たない」（<see cref="OnConfigChanging"/>）のように、<b>読み直すだけでは足りない</b>場合。
/// </para>
/// <para>
/// <b>実装しただけでは呼ばれない。</b> <see cref="IModuleHost.Config"/> に、自分の設定の型を書いて足すこと:
/// <code>
/// host.Config.AddListener&lt;IConfigEventListener&lt;HrsConfig&gt;&gt;(this, this);
/// </code>
/// 型は <see cref="ISpoitableModule.Config"/> が返すものそのもの（違う型で足すと、足すときに断られる）。外すのはホストの仕事。
/// </para>
/// <para>
/// 利用者が設定画面の「適用」を押すと、<b>1 回の適用につき 1 回ずつ</b>、次の順に呼ぶ（入力のたびには呼ばない —
/// 関係する設定を 2 つ変えただけで繋ぎ直しが 2 回走らないように）:
/// </para>
/// <code>
/// OnConfigChanging(e)   聞く。共有の設定はまだ前の値のまま。新しい値は e.Proposed（写し）と e.Changes で見る
///   ↓                  値ごとに断る: e.Reject(c =&gt; c.Device, 理由)。全部を断る: return EventResult.Stop(理由)
/// （ホストが、断られなかった値だけを共有の設定に書いて保存する）
///   ↓
/// OnConfigChanged(e)    知らせる。実際に入った値だけ（1 つも入らなければ呼ばない）
/// </code>
/// <para>
/// どちらも UI のスレッドから呼ぶので、<b>ここで待たないこと</b>。時間のかかることは投げるだけにする
/// （つまり「繋いでみて駄目なら断る」はできない。断るのは<b>手元で分かることだけ</b>）。
/// 投げた例外はホストが記録して、断らなかったものとして扱う（落ちた listener のせいで利用者が設定を変えられなくなる方が困るため）。
/// 投げる前に <c>e.Reject</c> した値は断ったままになる。
/// </para>
/// </summary>
/// <typeparam name="TConfig">自分の設定の型（<see cref="ISpoitableModule.Config"/> が返すもの）。</typeparam>
public interface IConfigEventListener<TConfig> : IEventListener where TConfig : class, IModuleConfig
{
    /// <summary>
    /// 変えてよいかを聞く。<b>共有の設定（自分の <see cref="ISpoitableModule.Config"/>）はまだ前の値</b>なので、新しい値は <c>e.Proposed</c> から読む。
    /// <code>
    /// public EventResult OnConfigChanging(ConfigChangingEventParams&lt;HrsConfig&gt; e)
    /// {
    ///     foreach (var change in e.Changes)                       // 変わった値だけ
    ///         if (change.Is(c =&gt; c.Device, out var device) &amp;&amp; !Known(device))
    ///             e.Reject(c =&gt; c.Device, "見つからない心拍計");
    ///
    ///     var z = e.Proposed.Zones;                               // 入ったらこうなる（変わっていない値も入っている）
    ///     if (!(z.LightBpm &lt; z.ModerateBpm &amp;&amp; z.ModerateBpm &lt; z.HardBpm))
    ///         e.Reject(c =&gt; c.Zones, "帯の順番が逆");             // 入れ物ごと: 変わった帯の値を全部断る
    ///
    ///     return EventResult.Continue;
    /// }
    /// </code>
    /// 組み合わせで駄目なときは、関わる値を全部断る（1 つだけ断ると、変わったほかの値が入って組み合わせが崩れる）。
    /// 理由はそのまま設定画面のその欄の下に出るので、利用者が何を直せばいいか分かる文にする。
    /// <c>EventResult.Handled</c> は <c>Continue</c> と同じに扱う（そのあとの listener には聞かない）。
    /// </summary>
    EventResult OnConfigChanging(ConfigChangingEventParams<TConfig> e) => EventResult.Continue;

    /// <summary>
    /// 変わった（保存も済んでいる）。<b>実際に入った値だけ</b>が <c>e.Changes</c> に並ぶ（断られた値は入らない）。
    /// <code>
    /// public void OnConfigChanged(ConfigChangedEventParams&lt;HrsConfig&gt; e)
    /// {
    ///     if (e.Changed(c =&gt; c.Device)) Reconnect();
    /// }
    /// </code>
    /// </summary>
    void OnConfigChanged(ConfigChangedEventParams<TConfig> e) { }
}
