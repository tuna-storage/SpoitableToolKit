namespace SpoitableToolKit.Abstractions.Notifications;

/// <summary>
/// ユーザーへの通知を出す口。通知はここに全部集める。
/// <para>
/// <b>段はモジュールが選ぶ。面はホストが決める。</b>
/// 「どれくらい強く割り込むか」（＝段）は伝えたい内容の性質なのでモジュールしか知らない。
/// 一方「どこに出すか」（＝面。デスクトップ窓 / VR オーバーレイ）は見せ方なのでホストが決める。
/// 出し先は全サーフェスなので、VR オーバーレイが繋がっていればそこにも同じものが出る。
/// </para>
/// </summary>
public interface INotificationManager
{
    /// <summary>
    /// 答えを待たない通知。右下にしばらく出て、自分で消える
    /// （「変換を始めました」のような、気付いてほしいが答えは要らないもの）。
    /// <para>
    /// <b>戻り値は捨ててよい。</b> 長い仕事の進み具合を見せたいときだけ持っておく
    /// （<see cref="IToast"/>）。
    /// </para>
    /// </summary>
    /// <param name="kind">種類。角四角の色とアイコンになる（<see cref="NotifyKind"/>）。</param>
    /// <param name="title">1 行目。</param>
    /// <param name="body">2 行目。無ければ出ない。</param>
    /// <param name="actions">
    /// 押せるもの。<b>押されたらこの通知は閉じる。</b> 無ければ何も出ない。
    /// </param>
    IToast PushToast(NotifyKind kind, string title, string? body = null, params NotifyAction[] actions);

    /// <summary>種類を選ばない分。<see cref="NotifyKind.Info"/> で出る。</summary>
    IToast PushToast(string title, string? body = null, params NotifyAction[] actions) =>
        PushToast(NotifyKind.Info, title, body, actions);

    /// <summary>
    /// <b>消すまで出続ける</b>通知。ヘッダーと本文の間に帯として積む。
    /// <para>
    /// 重いので<b>「見逃されたら困ること」だけ</b>に使う
    /// （モジュールが読み込めなかった、必要なものが見つからない、など）。
    /// 済んだら消えていいものは <see cref="PushToast(NotifyKind, string, string?, NotifyAction[])"/>。
    /// </para>
    /// <para>
    /// <b>引っ込める口は無い。</b> 消すのは利用者で、プログラムからは消せない。
    /// 「出したものが自分で消える」を許すと、<b>見る前に消える</b>ことが起こりうるため。
    /// </para>
    /// <para>
    /// <b>UI がまだ無い間に出しても消えない。</b> モジュールの読み込みは画面より先に走るので、
    /// そこで出した分は画面が開いたときに追いつく。
    /// </para>
    /// </summary>
    void PushBanner(NotifyKind kind, string title, string? body = null, params NotifyAction[] actions);

    /// <summary>種類を選ばない分。<see cref="NotifyKind.Error"/> で出る（帯は「見逃されたら困ること」なので）。</summary>
    void PushBanner(string title, string? body = null, params NotifyAction[] actions) =>
        PushBanner(NotifyKind.Error, title, body, actions);

    /// <summary>
    /// 聞いてから動く。<b>一番強い段</b>。デスクトップではトーストの束のいちばん下に縁の強い札で出て、
    /// <b>画面は塞がない</b>（名前に Modal と付くが、モーダルの窓にはしない）。
    /// 承諾されたら <c>true</c>。閉じられた・時間切れ・アプリ終了なら <c>false</c>。
    /// <para>
    /// <b>重い処理は承諾されてから始めること。</b> 例えば動画の再生失敗は 1 本につき 4〜5 回
    /// 飛んでくるので、先回りして解決していると見てもいない動画で毎回 yt-dlp が走る。
    /// </para>
    /// </summary>
    Task<bool> PushModalAsync(NotifyRequest request, CancellationToken cancellationToken = default);
}
