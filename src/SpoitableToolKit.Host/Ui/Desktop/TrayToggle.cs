namespace SpoitableToolKit.Host.Ui.Desktop;

/// <summary>
/// トレイのアイコンの左クリックは「出ていればしまう、そうでなければ開く」。窓を知らない形にしてある（試験はここを見る）。
/// 「出ている」は、窓があって、表示されていて、最小化されていないこと。ほかの窓の後ろに隠れていても出ている（しまう）。
/// 最小化しているときは開く（元の大きさに戻して前に出す。1 回のクリックで戻る）。
/// </summary>
internal static class TrayToggle
{
    public static bool OnScreen(bool exists, bool visible, bool minimized) => exists && visible && !minimized;

    public static TrayClick For(bool exists, bool visible, bool minimized) =>
        OnScreen(exists, visible, minimized) ? TrayClick.PutAway : TrayClick.Open;
}
