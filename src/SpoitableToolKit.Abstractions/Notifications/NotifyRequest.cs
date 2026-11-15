namespace SpoitableToolKit.Abstractions.Notifications;

/// <summary>ユーザーに一言聞くときの内容。</summary>
public sealed record NotifyRequest
{
    public required string Title { get; init; }

    /// <summary>本文。URL のように長いものが入る。</summary>
    public string? Body { get; init; }

    /// <summary>承諾ボタンの文言。<b>何が起きるかを書く</b>こと（「OK」だけだと分からない）。</summary>
    public string ConfirmLabel { get; init; } = "OK";

    /// <summary>
    /// これを過ぎたら「承諾されなかった」として閉じる。出しっぱなしにしない
    /// （VRChat を見ている間に溜まっていくため）。
    /// <para>
    /// <b><c>null</c> ならホストが決める</b>（設定で変えられる）。ここに秒数を書くのは、
    /// その通知に固有の理由があるときだけにすること。出し先と同じで、
    /// 「どれだけ出しておくか」は見せ方の話なのでホストの領分。
    /// </para>
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>角四角の色とアイコン。既定は <see cref="NotifyKind.Info"/>。</summary>
    public NotifyKind Kind { get; init; } = NotifyKind.Info;

    /// <summary>
    /// 「今後は聞かない」。<c>null</c> ならボタンを出さない。
    /// <para>
    /// <b>押されたら答えは <c>false</c>（承諾されなかった）で閉じ、そのあとでこれが走る。</b>
    /// 走らせる中身はモジュールが持つ（自分の設定の「毎回聞く」を切って保存する、など）。
    /// ホストが設定を勝手に書き換える形にしないのは、<b>どの設定が「聞く」に当たるかを知っているのはモジュールだけ</b>だから。
    /// </para>
    /// </summary>
    public NotifyAction? OptOut { get; init; }

    /// <summary>
    /// <b>時間切れで答えそびれたあと</b>、通知の履歴（タイトルバーのベル）から押されたら走るもの。
    /// <c>null</c> なら履歴は見るだけ。
    /// <para>
    /// <see cref="INotificationManager.PushModalAsync"/> はもう <c>false</c> を返し終わっているので、
    /// 承諾されたときと同じことをここに書く（動画の再生失敗なら「解決してコピー」）。<b>1 回だけ効く。</b>
    /// </para>
    /// </summary>
    public NotifyAction? Later { get; init; }
}
