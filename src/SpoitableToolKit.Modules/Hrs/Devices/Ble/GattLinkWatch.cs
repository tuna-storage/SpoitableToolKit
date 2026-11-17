using SpoitableToolKit.Modules.Hrs.Devices.Models;

namespace SpoitableToolKit.Modules.Hrs.Devices.Ble;

/// <summary>
/// GATT で掴んでいる相手の線が、上がっているか落ちているか。
/// <para>
/// 線が落ちても<b>掴んだ物は手放さない</b>（相手が戻れば Windows が自分で繋ぎ直し、通知もそのまま戻る）。
/// ただし<b>落ちている間は「繋がっている」と言わない</b> — 言い続けると、電源を切った心拍計が画面では受信中のまま残る。
/// </para>
/// <para>
/// 自分ではロックしない。線の知らせは BLE のスレッドから来るので、呼ぶ側が後片付けと同じロックの中で呼ぶ。
/// </para>
/// </summary>
internal sealed class GattLinkWatch
{
    private bool _down;

    /// <summary>掴んでいる相手（線が落ちていても）。掴んでいなければ <c>null</c>。</summary>
    public MonitorIdentity? Held { get; private set; }

    /// <summary>いま繋がっている先。掴んでいても、線が落ちている間は <c>null</c>。</summary>
    public MonitorIdentity? Connected => _down ? null : Held;

    /// <summary>
    /// 掴んでいる相手の線が落ちた時刻（落ちたままの間だけ。戻った・手放した・掴み直したら null）。
    /// Windows の繋ぎ直しを待ちきれないときに、アプリから繋ぎ直すかの判断に使う（<see cref="LinkTakeover"/>）。
    /// </summary>
    public DateTimeOffset? DownSince { get; private set; }

    /// <summary>
    /// 通知を受け始めた。<b>ここから先の上がり下がりだけ</b>が繋がりを変える —
    /// 繋ぎに行っている途中にも「上がった」は来るが、まだ心拍を受ける用意ができていない。
    /// </summary>
    public void Hold(MonitorIdentity target)
    {
        Held = target;
        _down = false;
        DownSince = null;
    }

    /// <summary>手放した。このあと遅れて届いた知らせで、繋がりを生き返らせない。</summary>
    public void Drop()
    {
        Held = null;
        DownSince = null;
    }

    /// <param name="at">知らせを受けた時刻（落ちたなら、ここから数える）。</param>
    public LinkChange Feed(bool up, DateTimeOffset at)
    {
        if (Held is null || up != _down) return LinkChange.None;

        _down = !up;
        DownSince = up ? null : at;
        return up ? LinkChange.Back : LinkChange.Lost;
    }
}
