namespace SpoitableToolKit.Abstractions.Shortcuts;

/// <summary>
/// <b>押すだけ</b>の手首のショートカット（タイル 1 枚）。出す情報は <see cref="IWristShortcut"/>、登録は <see cref="IModuleShortcuts.Register(IClickableWristShortcut)"/>。
/// 押しは普通のボタンと同じで、<b>タイルの中で離したら押した</b>（押したまま外へ出て離せば、押さなかったことになる）。
/// 押した拍子に手が揺れても、タイルの中で離せば押したことになる（ドラッグに取られない）。
/// <para>
/// ドラッグを受けたいタイルは <see cref="IDraggableWristShortcut"/>（そちらは押しを受けない）。<b>両方は実装できない</b>（登録のときに例外）。
/// </para>
/// </summary>
public interface IClickableWristShortcut : IWristShortcut
{
    /// <summary>
    /// タイルが押された。
    /// <para>
    /// <b>UI のスレッドでも VR の周のスレッドでもない所で呼ばれる</b>（重い処理を書いてよい）。
    /// <b>返した Task が終わるまでに、もう一度押されても 2 回目は呼ばない</b>（解くのに数秒かかる間に、手応えが無くて押し直されることがある）。
    /// <b>例外は本体が受けて</b>、ログとエラーのトーストにする。結果を知らせたいときは <see cref="IModuleHost.Notifications"/> のトーストを出す。
    /// <paramref name="cancellationToken"/> はアプリを畳むときに切れる（切れて止まったものはエラーにしない）。
    /// </para>
    /// <para>
    /// 自分のパネルを VR の空間の窓で開くなら、ここから <see cref="Ui.IModuleUi.ToggleVrPanel"/> を呼ぶ（枠を付けるなら <see cref="IWristShortcut.MarkedPanel"/> も名乗る）。
    /// 押しても何もしないタイルにするなら、<c>=&gt; Task.CompletedTask</c> と書く（既定の本体は無い。書き忘れは build で分かる）。
    /// </para>
    /// </summary>
    Task OnTriggerAsync(CancellationToken cancellationToken);
}
