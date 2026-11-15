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
    /// 通知を受け始めた。<b>ここから先の上がり下がりだけ</b>が繋がりを変える —
    /// 繋ぎに行っている途中にも「上がった」は来るが、まだ心拍を受ける用意ができていない。
    /// </summary>
    public void Hold(MonitorIdentity target)
    {
        Held = target;
        _down = false;
    }

    /// <summary>手放した。このあと遅れて届いた知らせで、繋がりを生き返らせない。</summary>
    public void Drop() => Held = null;

    public LinkChange Feed(bool up)
    {
        if (Held is null || up != _down) return LinkChange.None;

        _down = !up;
        return up ? LinkChange.Back : LinkChange.Lost;
    }
}
