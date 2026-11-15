namespace SpoitableToolKit.Abstractions.Events;

/// <summary>
/// 出来事を配る口。持つのは口を作った持ち主だけ（<see cref="IEventHubs.Create{TListener}"/>）。
/// 受ける側には <see cref="Source"/> だけを見せる — <see cref="Source"/> は別の object なので、cast しても配る口は出てこない。
/// <para>
/// これは<b>取り違えの防止</b>で、境界ではない（モジュールはどれも、このアプリと同じ権限で動く）。
/// よそのモジュールの出来事を、よそから配ってしまう間違いを起こせなくするためのもの。
/// </para>
/// <para>
/// 配り方: 全員へ（<see cref="Fire{TListener}"/>）・誰かが止めるまで（<see cref="FireCancellable{TListener}"/>）・答えを集める（<see cref="FireCollect{TListener, TResult}"/>・
/// <see cref="FireCollectMany{TListener, TResult}"/>）・当てはまる最初の答え（<see cref="FireWithResult{TListener, TResult}"/>）。
/// </para>
/// <para>
/// どの配り方も、<b>呼んだ thread の上で、配る順に 1 つずつ</b>呼ぶ（大きい <see cref="IEventListener.ListenerPriority"/> が先、同じなら足した順）。
/// listener が投げても、残りへは配る（投げたものは、持ち主と listener の型を付けて Host が記録する）。
/// 配っている最中に足す・外すが起きても壊れない（別の thread からでもよい）。
/// </para>
/// <code>
/// _hub = host.Events.Create&lt;ISomeListener&gt;(this);
/// public IEventSource Listeners => _hub.Source;
/// _hub.Fire&lt;ISomeListener&gt;(l => l.OnSomething(new SomethingEvent(...)));
/// </code>
/// </summary>
public interface IEventHub
{
    /// <summary>受ける側に見せる口（足す・外すだけ）。</summary>
    IEventSource Source { get; }

    /// <summary>全員に配る。配る型は、作ったときに決めたものだけ（ほかの型なら例外）。</summary>
    void Fire<TListener>(Action<TListener> invoke) where TListener : class, IEventListener;

    /// <summary>
    /// 順に聞き、<see cref="EventResultKind.Handled"/> か <see cref="EventResultKind.Stop"/> が返ったら、そこでやめてそれを返す。
    /// 全員が <see cref="EventResult.Continue"/> なら <see cref="EventResult.Continue"/>。
    /// 投げた listener と null を返した listener は、記録して <c>Continue</c> とみなす（壊れた listener のせいで止まらない）。
    /// </summary>
    EventResult FireCancellable<TListener>(Func<TListener, EventResult> invoke) where TListener : class, IEventListener;

    /// <summary>
    /// 全員から答えを集める（並びは配る順）。投げた listener の分と、null の答えは入れない。
    /// </summary>
    IReadOnlyList<TResult> FireCollect<TListener, TResult>(Func<TListener, TResult> invoke) where TListener : class, IEventListener;

    /// <summary>
    /// 全員から<b>答えの並び</b>を集めて、1 つにつなげて返す（並びは配る順。listener ごとに 0 個以上）。
    /// 投げた listener の分（並びを数えている途中で投げた分も）・null の並び・並びの中の null は入れない。
    /// <code>
    /// var maps = _hub.FireCollectMany&lt;IMapSource, string&gt;(l => l.MapsFor(group));
    /// </code>
    /// </summary>
    IReadOnlyList<TResult> FireCollectMany<TListener, TResult>(Func<TListener, IEnumerable<TResult>?> invoke) where TListener : class, IEventListener
    {
        ArgumentNullException.ThrowIfNull(invoke);

        // 並びは listener の中で数え切る（あとで数える並びが、listener の外で投げないように）。
        List<TResult> results = [];
        foreach (var batch in FireCollect<TListener, TResult[]?>(l => invoke(l)?.ToArray()))
        {
            if (batch is null) continue;
            foreach (var item in batch)
                if (item is not null) results.Add(item);
        }

        return results;
    }

    /// <summary>
    /// 順に聞いて、<paramref name="stop"/> が true になった<b>最初の答え</b>を返す（そこから先には聞かない）。
    /// 誰の答えでも止まらなければ <paramref name="whenNoneStops"/>。投げた listener は記録して飛ばす（<paramref name="stop"/> が投げたときも、その listener の分として飛ばす）。
    /// <code>
    /// var price = _hub.FireWithResult&lt;IPriceSource, decimal?&gt;(l => l.PriceOf(item), stop: p => p is not null, whenNoneStops: null);
    /// </code>
    /// </summary>
    TResult FireWithResult<TListener, TResult>(Func<TListener, TResult> invoke, Func<TResult, bool> stop, TResult whenNoneStops)
        where TListener : class, IEventListener
    {
        ArgumentNullException.ThrowIfNull(invoke);
        ArgumentNullException.ThrowIfNull(stop);

        var answer = whenNoneStops;
        FireCancellable<TListener>(l =>
        {
            var result = invoke(l);
            if (!stop(result)) return EventResult.Continue;

            answer = result;
            return EventResult.Handled;
        });

        return answer;
    }
}
