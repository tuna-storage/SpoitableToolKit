namespace SpoitableToolKit.Abstractions.Events;

/// <summary><see cref="EventResult"/> の種類。</summary>
public enum EventResultKind
{
    /// <summary>何もしない・続けてよい。残りの listener へも配る。</summary>
    Continue,

    /// <summary>引き受けた。<b>残りの listener へは配らない。</b></summary>
    Handled,

    /// <summary>やめさせる。<b>残りの listener へは配らない。</b>理由（<see cref="EventResult.Reason"/>）が必ず付く。</summary>
    Stop,
}
