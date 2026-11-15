namespace SpoitableToolKit.Abstractions.Events;

/// <summary>
/// 出来事を受け取る listener の土台。受け取る出来事ごとの interface がこれを継ぐ（どのメソッドも既定は何もしない形にする）。
/// <para>
/// <b>実装しただけでは呼ばれない。</b> 出来事を出す側が見せている口（<see cref="IEventSource"/>）に、<b>型を書いて</b>足す:
/// <code>
/// host.VRChat.AddListener&lt;IVRChatEventListener&gt;(this, _presence);   // ホストの出来事
/// heartRate.Listeners.AddListener&lt;IHeartRateEventListener&gt;(this, _overlay);   // ほかのモジュールが見せている出来事
/// </code>
/// 型（<c>&lt;ISomeListener&gt;</c>）は省けない。省くと build が CS0411 で落ちる（わざとそうしてある）。
/// 1 つの instance が listener の interface を 2 つ実装していても、届くのは足した型の分だけ。
/// </para>
/// <para>
/// 引数は出来事ごとに record 1 つにする（あとから項目を足しても、受け取る側の書き方が変わらない）。
/// </para>
/// </summary>
public interface IEventListener
{
    /// <summary>
    /// 配る順。<b>大きいほど先</b>。同じなら足した順。既定は 0。
    /// <para><b>読むのは足したときの 1 回だけ</b>（あとで値を変えても並び直さない）。</para>
    /// </summary>
    int ListenerPriority => 0;
}
