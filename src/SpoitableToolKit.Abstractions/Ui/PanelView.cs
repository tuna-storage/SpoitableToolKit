namespace SpoitableToolKit.Abstractions.Ui;

/// <summary>
/// 自分のパネルの、空間の窓に出す画面（<see cref="Shortcuts.IWristShortcut.MarkedPanel"/>）。
/// <see cref="IModuleUi.ToggleVrPanel"/> に渡す <c>place</c> と同じものを指す。
/// <code>
/// new PanelView()                     // パネルの頭（ToggleVrPanel() で開く画面）
/// new PanelView { Place = "stats" }   // パネルの中の stats（ToggleVrPanel("stats") で開く画面）
/// </code>
/// <para>
/// <b>引数の並びで作る形にしていない</b>のは、あとから項目を足しても SDK の形を壊さないため（位置引数の record に引数を足すと、前の SDK で作ったモジュールが読めなくなる）。
/// </para>
/// </summary>
public sealed record PanelView
{
    /// <summary>
    /// パネルの中の場所（パネルの URL の <c>#</c> の後ろ）。<c>null</c> ならパネルの頭。
    /// 使える字は <see cref="IModuleUi.ToggleVrPanel"/> と同じ（英数字・<c>-</c>・<c>_</c>、64 字まで）。外れていたら登録のときに例外。
    /// </summary>
    public string? Place { get; init; }
}
