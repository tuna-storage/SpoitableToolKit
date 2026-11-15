namespace SpoitableToolKit.Abstractions.Shortcuts;

/// <summary>
/// <b>ドラッグだけ</b>の手首のショートカット（タイル 1 枚。スライダーやつまみなど）。出す情報は <see cref="IWristShortcut"/>、
/// 登録は <see cref="IModuleShortcuts.Register(IDraggableWristShortcut)"/>。
/// <b>押しは受けない</b> — タイルを押した瞬間から離すまでが 1 回のドラッグで、触って離すだけでも <see cref="OnStartDragAsync"/> と <see cref="OnEndDragAsync"/> が来る
/// （押したかドラッグかを見分けないので、押した拍子の揺れで取り違えることも無い）。
/// <para>
/// 押して何かをさせたいタイルは <see cref="IClickableWristShortcut"/>。<b>両方は実装できない</b>（登録のときに例外）。
/// 3 つとも既定の本体は無い（書き忘れは build で分かる）。何もしないものは <c>=&gt; Task.CompletedTask</c> と書く。
/// </para>
/// <para>
/// 1 回のドラッグの中では<b>呼ぶのは 1 つずつ、この順</b>: <see cref="OnStartDragAsync"/> → <see cref="OnDraggingAsync"/>（0 回以上）→ <see cref="OnEndDragAsync"/>（1 回）。
/// 前の呼び出しが返るまで次は呼ばない。<b><see cref="OnStartDragAsync"/> を呼んだら <see cref="OnEndDragAsync"/> は必ず 1 回呼ぶ</b>（途中で例外を投げても、ドラッグが途切れても）。
/// 呼ばないのはアプリを畳むときだけ（<c>cancellationToken</c> が切れる）。
/// </para>
/// <para>
/// <b>UI のスレッドでも VR の周のスレッドでもない所で呼ばれる</b>。<b>例外は本体が受けて</b>、ログとエラーのトーストにする（トーストは 1 回のドラッグで 1 回まで）。
/// 呼び出しが返り切るまでに押された分は捨てる（2 本目のドラッグは走らせない）。
/// </para>
/// </summary>
public interface IDraggableWristShortcut : IWristShortcut
{
    /// <summary>タイルを押した（ドラッグの始まり）。<paramref name="e"/> の位置は押した所（<see cref="ShortcutDragEventParams.StartPosition"/> と同じ）。</summary>
    Task OnStartDragAsync(ShortcutDragEventParams e, CancellationToken cancellationToken);

    /// <summary>
    /// 押したまま動いた。<b>前の呼び出しが返るまでに動いた分は、最後の位置 1 つにまとめる</b>（溜めない。重い処理でも遅れて追いかけない）。
    /// 止めていれば呼ばない。
    /// </summary>
    Task OnDraggingAsync(ShortcutDragEventParams e, CancellationToken cancellationToken);

    /// <summary>
    /// ドラッグが終わった（離した・タイルを指せなくなった・手首が閉じた）。<paramref name="e"/> は最後の位置。
    /// <b>最後の <see cref="OnDraggingAsync"/> が返ってから 1 回だけ</b>呼ぶ（まとめない）。
    /// </summary>
    Task OnEndDragAsync(ShortcutDragEventParams e, CancellationToken cancellationToken);
}
