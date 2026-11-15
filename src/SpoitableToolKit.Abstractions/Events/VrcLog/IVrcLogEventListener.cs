using SpoitableToolKit.Abstractions.VrcLog;

namespace SpoitableToolKit.Abstractions.Events.VrcLog;

/// <summary>
/// VRChat のログを<b>1 行ずつ</b>受け取る。要るものだけ書けばよい（どちらも既定は何もしない）。
/// <para>
/// <b>実装しただけでは呼ばれない。</b> <see cref="IModuleHost.VrcLog"/> に、型を書いて足すこと:
/// <code>
/// host.VrcLog.AddListener&lt;IVrcLogEventListener&gt;(this, this);
/// </code>
/// 外すのはホストの仕事（モジュールが畳まれたら、足した listener は全部外れる）。
/// </para>
/// <para>
/// 1 行ごとに <see cref="OnLogLine"/>（全部の行。意味の分からない行も）→ 意味の分かった行だけ <see cref="OnLogEvent"/> の順に呼ぶ。
/// 足す前の行は届かない（アプリはログを末尾から読む。過去の分が要るなら <see cref="IModuleHost.VrcLogStore"/> を引く）。
/// </para>
/// <para>
/// 呼ぶのはログを読むスレッド（UI のスレッドではない）。<b>ここで待たないこと</b> — 重い処理は自分で投げる。
/// 待つと、そのあいだ次の行が読まれない（ほかのモジュールにも、アプリの記録にも届くのが遅れる）。
/// 投げた例外はホストが記録して、次の listener へ配る（読むのは止まらない）。
/// </para>
/// <code>
/// public void OnLogEvent(VrcLogEventParams e)
/// {
///     if (e is WorldJoined joined) _world = joined.WorldName;
/// }
/// </code>
/// </summary>
public interface IVrcLogEventListener : IEventListener
{
    /// <summary>ログの 1 行（続きの行は前の行の <see cref="VrcLogLine.Message"/> に繋がって 1 つで届く）。</summary>
    void OnLogLine(VrcLogLine line) { }

    /// <summary>意味の分かった行（ワールドに入った・動画の再生に失敗した など）。同じ行の <see cref="OnLogLine"/> のあとに呼ぶ。</summary>
    void OnLogEvent(VrcLogEventParams e) { }
}
