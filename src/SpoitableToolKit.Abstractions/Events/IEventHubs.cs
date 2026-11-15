namespace SpoitableToolKit.Abstractions.Events;

/// <summary>
/// 出来事の口（<see cref="IEventHub"/>）を作る所。
/// <para>
/// <b>配る型は作るときに決まり、あとから増やせない。</b>受ける側がその口に足せるのも、その型だけ。
/// 持ち主（<c>owner</c> に渡したモジュール）が畳まれたら、Host がその口を閉じる（足されていた listener も全部外れる）。
/// </para>
/// </summary>
public interface IEventHubs
{
    /// <summary><typeparamref name="TListener"/> を配る口を作る。</summary>
    /// <param name="owner">口を作るモジュール自身（<c>this</c>）。</param>
    IEventHub Create<TListener>(ISpoitableModule owner) where TListener : class, IEventListener;

    /// <summary><typeparamref name="TListener1"/> と <typeparamref name="TListener2"/> を配る口を作る（同じ型を 2 つ書いたら例外）。</summary>
    /// <param name="owner">口を作るモジュール自身（<c>this</c>）。</param>
    IEventHub Create<TListener1, TListener2>(ISpoitableModule owner)
        where TListener1 : class, IEventListener
        where TListener2 : class, IEventListener;
}
