using System.Numerics;

namespace SpoitableToolKit.Abstractions.Shortcuts;

/// <summary>
/// 手首のショートカットをドラッグしている所（<see cref="IDraggableWristShortcut.OnStartDragAsync"/>・<see cref="IDraggableWristShortcut.OnDraggingAsync"/>・
/// <see cref="IDraggableWristShortcut.OnEndDragAsync"/>）。
/// <para>
/// 位置は<b>タイルの中の割合</b>: 左上が (0, 0)、右下が (1, 1)（X は右、Y は下が正）。タイルの大きさや手首の板の大きさが変わっても同じ値になる。
/// <b>タイルの外へ引くと 0〜1 の外になる</b>（切り詰めない。スライダーなら自分で <c>Math.Clamp</c> する）。
/// </para>
/// </summary>
/// <param name="Position">いまの位置。</param>
/// <param name="StartPosition">押した位置（ドラッグの間は変わらない）。</param>
public sealed record ShortcutDragEventParams(Vector2 Position, Vector2 StartPosition);
