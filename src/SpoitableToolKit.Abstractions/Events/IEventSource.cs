namespace SpoitableToolKit.Abstractions.Events;

/// <summary>
/// 出来事を受け取る側に見せる口。<b>できるのは足す・外すだけ</b>（配れるのは、口を作った持ち主が持つ <see cref="IEventHub"/> だけ）。
/// <para>
/// 出来事を出す側は、自分の <see cref="IEventHub.Source"/> をこの型で見せる:
/// <code>
/// public IEventSource Listeners => _hub.Source;
/// </code>
/// 受ける側は、受けたい listener の型を<b>書いて</b>足す（省くと build が CS0411 で落ちる）:
/// <code>
/// host.Videos.AddListener&lt;IVideoEventListener&gt;(this, this);
/// </code>
/// </para>
/// <para>
/// <b>外すのは Host の仕事</b>: owner（モジュール）が畳まれたら、その owner が足した listener を全部外す。自分で外さなくてよい。
/// </para>
/// <para>
/// listener は、<b>出す側が配った thread の上で</b>呼ばれる（UI の thread とは限らない）。待たないこと — 重い処理は自分で投げる。
/// </para>
/// </summary>
public interface IEventSource
{
    /// <summary>
    /// <paramref name="listener"/> を <typeparamref name="TListener"/> として足す。同じ instance を同じ型で 2 回足しても 1 つ。
    /// <para>
    /// 断る（例外）もの: 型引数が interface でない・<see cref="IEventListener"/> そのもの・この口が配らない型・
    /// <paramref name="listener"/> が <typeparamref name="TListener"/> を実装していない・<paramref name="owner"/> が読み込まれたモジュールでない・
    /// 同じ instance をほかの owner がもう足している。黙って届かない形は残さない。
    /// </para>
    /// <para>
    /// 配っている最中に足したものは、その回には届かない（次の回から）。
    /// </para>
    /// </summary>
    /// <typeparam name="TListener">受けたい出来事の listener の interface。省けない。</typeparam>
    /// <param name="owner">足すモジュール自身（<c>this</c>）。畳まれたら Host が外す目印。</param>
    /// <param name="listener">受け取る instance。モジュール本体でも、別のクラスでもよい。</param>
    void AddListener<TListener>(ISpoitableModule owner, IEventListener listener) where TListener : class, IEventListener;

    /// <summary>
    /// 外す。足していなければ何もしない。<b>外せるのは足した owner だけ</b>（ほかの owner が足したものなら例外）。
    /// 配っている最中に外すと、その回でまだ呼んでいなければもう呼ばない（listener が自分を外してもよい）。
    /// </summary>
    void RemoveListener<TListener>(ISpoitableModule owner, IEventListener listener) where TListener : class, IEventListener;
}
