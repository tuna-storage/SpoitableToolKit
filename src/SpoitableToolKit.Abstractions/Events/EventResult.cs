namespace SpoitableToolKit.Abstractions.Events;

/// <summary>
/// キャンセルできる出来事（<see cref="IEventHub.FireCancellable{TListener}"/>）への、listener の答え。
/// <para>
/// <b>作れるのは 3 つだけ</b>: <see cref="Continue"/>・<see cref="Handled"/>・<see cref="Stop(string)"/>。
/// 理由の無い <c>Stop</c> は作れない（出す側は、誰が・なぜ止めたかを記録に残す）。
/// </para>
/// <para>
/// <c>Handled</c> と <c>Stop</c> は、どちらもそこで配るのをやめる。違うのは出す側への意味で、
/// <c>Handled</c> は「誰かが引き受けた」、<c>Stop</c> は「やめさせた」。どう扱うかは出す側が決めて、その出来事の説明に書く。
/// </para>
/// <code>
/// public EventResult OnSomethingHappening(SomethingHappeningEvent e) =>
///     e.Name.Length == 0 ? EventResult.Stop("名前が空です") : EventResult.Continue;
/// </code>
/// </summary>
public sealed record EventResult
{
    private EventResult(EventResultKind kind, string? reason)
    {
        Kind = kind;
        Reason = reason;
    }

    /// <summary>何もしない・続けてよい。残りの listener へも配る。</summary>
    public static EventResult Continue { get; } = new(EventResultKind.Continue, null);

    /// <summary>引き受けた。残りの listener へは配らない。</summary>
    public static EventResult Handled { get; } = new(EventResultKind.Handled, null);

    /// <summary>
    /// やめさせる。残りの listener へは配らない。
    /// </summary>
    /// <param name="reason">なぜ止めたか。<b>必須</b>（空・空白だけなら例外）。出す側が記録に残し、画面に出すこともある。</param>
    public static EventResult Stop(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new EventResult(EventResultKind.Stop, reason);
    }

    public EventResultKind Kind { get; }

    /// <summary>止めた理由。<see cref="EventResultKind.Stop"/> のときだけ入る。</summary>
    public string? Reason { get; }
}
