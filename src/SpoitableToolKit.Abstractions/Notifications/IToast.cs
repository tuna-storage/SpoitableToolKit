namespace SpoitableToolKit.Abstractions.Notifications;

/// <summary>
/// 出したトーストの取っ手。<b>簡単な通知は受け取らずに捨ててよい</b>
/// （出しっぱなしで、しばらくすると自分で消える）。
/// <para>
/// 長い仕事に付き合わせたいときだけ持っておく。
/// <code>
/// using var toast = host.Notifications.PushToast("変換中");
/// toast.Progress = 0.4;
/// toast.Progress = 1;   // ここで「終わった」を少し見せてから、自分で消える
/// </code>
/// </para>
/// </summary>
public interface IToast : IDisposable
{
    /// <summary>見出し。書き換えると画面もその場で変わる。</summary>
    string Title { get; set; }

    /// <summary>補足。<c>null</c> なら出ない。</summary>
    string? Body { get; set; }

    /// <summary>
    /// 0〜1 の進み具合。範囲外は丸められる。
    /// <para>
    /// <b>入れると、自分で消える時計が止まる</b>（<c>0</c> でも止まる）。
    /// 終わりが定義されたものは、終わるまで消えない方が正しいため。
    /// <b>長さが分からない仕事は <c>0</c> を一度入れて</b>、進み具合の代わりに
    /// <see cref="Body"/> へ現在地（「12.4 MB」など）を書くとよい。
    /// </para>
    /// <para>
    /// <c>1</c> に達したら、しばらく見せてから自分で消える。
    /// </para>
    /// </summary>
    double? Progress { get; set; }

    /// <summary>
    /// 引っ込める。<b>二重に呼んでも、利用者が先に閉じていても安全</b>。
    /// <para>
    /// <b><see cref="Progress"/> が 1 に達したあとは何もしない。</b>
    /// <c>using</c> で囲うと、完了を入れた直後にスコープを抜けることになるので、
    /// ここで消してしまうと<b>「終わりました」が誰にも見えない</b>。
    /// 失敗・中断のときは 1 に達していないので、ふつうに消える。
    /// </para>
    /// </summary>
    new void Dispose();
}
