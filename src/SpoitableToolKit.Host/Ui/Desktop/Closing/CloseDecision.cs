using SpoitableToolKit.Host.Settings;

namespace SpoitableToolKit.Host.Ui.Desktop.Closing;

/// <summary>
/// 窓が閉じられかけたときに、何をするかを決める。WinForms の窓を知らない形にしてある（試験はここを見る）。
/// <para>
/// 設定（<see cref="HostConfig.CloseButton"/>）に従うのは、利用者が閉じたとき（<see cref="CloseReason.UserClosing"/>）だけ。
/// × は <c>Form.Close</c>、Alt+F4 とタスクバーの「ウィンドウを閉じる」は <c>SC_CLOSE</c> で、どれもこれになる。
/// </para>
/// <para>
/// ほかの理由は聞かずに終了する。ログオフ・シャットダウン（<see cref="CloseReason.WindowsShutDown"/>）を止めない。
/// タスクマネージャーや <c>taskkill</c>（<c>/F</c> なし）の <c>WM_CLOSE</c> は <see cref="CloseReason.TaskManagerClosing"/> で、終わらせたい人がしていること。
/// </para>
/// </summary>
internal static class CloseDecision
{
    public static CloseStep For(CloseReason reason, CloseButtonAction setting)
    {
        if (reason != CloseReason.UserClosing) return CloseStep.Exit;

        return setting switch
        {
            CloseButtonAction.Tray => CloseStep.Tray,
            CloseButtonAction.Exit => CloseStep.Exit,
            _ => CloseStep.Ask,
        };
    }

    /// <summary>
    /// 聞きたいのに、シェルのページがまだ答えられない（読み込み中・壊れている）とき。しまう — 終了すると、押した人が思っていたより強いことをしてしまう。
    /// 設定は聞くのまま（次に閉じるときにまた聞く）。
    /// </summary>
    public const CloseStep WhenTheShellCannotAsk = CloseStep.Tray;
}
