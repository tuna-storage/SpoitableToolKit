namespace SpoitableToolKit.Abstractions.Events.VRChat;

/// <summary>
/// VRChat が<b>起動した・終わった</b>ことを受け取る。要るものだけ書けばよい（どちらも既定は何もしない）。
/// <para>
/// <b>実装しただけでは呼ばれない。</b> <see cref="IModuleHost.VRChat"/> に、型を書いて足すこと:
/// <code>
/// host.VRChat.AddListener&lt;IVRChatEventListener&gt;(this, this);
/// </code>
/// 外すのはホストの仕事（モジュールが畳まれたら、足した listener は全部外れる）。
/// </para>
/// <para>
/// <b>起動・終了が本当に起きたときだけ</b>呼ばれる。登録したとき・モジュールを読み込んだときに「いま動いている」を流すことはしない —
/// いまの状態が要るなら <see cref="IModuleHost.IsVRChatRunning"/> を読む（アプリが起動したときに一度確かめてあるので、
/// <see cref="ISpoitableModule.OnInit"/> の時点でもう正しい値が入っている）。
/// </para>
/// <para>
/// <b>呼ばれる時点で <see cref="IModuleHost.IsVRChatRunning"/> は新しい値になっている</b>（<see cref="OnVRChatShutdown"/> の中で読めば false）。
/// 呼ぶのはホストの見張りのスレッド（UI のスレッドではない）。<b>ここで待たないこと</b> — 重い処理は自分で投げる。
/// 待つと、次の起動・終了の知らせがその分遅れる（順番は崩れない）。
/// </para>
/// <para>
/// 見張りは 2 秒おきなので、知らせは起動・終了から最大 2 秒ほど遅れる。
/// 名前に VRC が入るのは、<see cref="ISpoitableModule.OnShutdown"/>（モジュール自身が畳まれる）と読み違えないため。
/// </para>
/// <code>
/// public void OnVRChatShutdown(VRChatShutdownEventParams e) => _lastWorld = null;
/// </code>
/// </summary>
public interface IVRChatEventListener : IEventListener
{
    /// <summary>VRChat が起動した。</summary>
    void OnVRChatStarted(VRChatStartedEventParams e) { }

    /// <summary>VRChat が終わった（閉じた・落ちた・止められた）。</summary>
    void OnVRChatShutdown(VRChatShutdownEventParams e) { }
}
