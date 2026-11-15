using SpoitableToolKit.Abstractions.Notifications;

namespace SpoitableToolKit.Abstractions.Osc;

/// <summary>
/// VRChat への OSC 送信。<b>出し先はホストが決める</b>ので、モジュールは何を送るかだけを渡す
/// （<see cref="INotificationManager"/> と同じ理屈。ポートは VRChat 側の性質であって、
/// モジュールごとに違う値になるものではない）。
/// <para>
/// 送りっぱなし。UDP なので届いたかどうかは分からないし、VRChat が起動していなくても失敗しない。
/// <b>正となる状態は送る側が持つこと</b>（パネルへの push と同じ）。
/// </para>
/// <code>
/// host.Osc.Send("/avatar/parameters/HR", 72);
/// host.Osc.Send("/avatar/parameters/isHRBeat", true);
/// </code>
/// </summary>
public interface IOscBus
{
    /// <summary>
    /// 1 通送る。
    /// </summary>
    /// <param name="address">
    /// OSC アドレス。アバターのパラメータなら <c>/avatar/parameters/&lt;名前&gt;</c>。
    /// </param>
    /// <param name="args">
    /// <c>int</c> / <c>float</c> / <c>bool</c> / <c>string</c>。
    /// <b>それ以外は例外</b> — 黙って落とすと「送ったのに効かない」の原因が分からない。
    /// <para>
    /// <c>double</c> や <c>long</c> は受け取らない。OSC 1.0 の基本型に無く、
    /// VRChat も読まないので、暗黙に縮めるより書いた側で決めてもらう。
    /// </para>
    /// </param>
    void Send(string address, params object[] args);
}
