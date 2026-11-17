namespace SpoitableToolKit.Host.Ui.Desktop;

/// <summary>
/// アプリの見えない窓（トップレベル。画面にもタスクバーにも Alt+Tab にも出ない）。デスクトップの窓を畳んでいる間も、外からの「終わって」を受けて終了へ回す。
/// <list type="bullet">
///   <item><c>WM_CLOSE</c>: <c>taskkill</c>（<c>/F</c> なし）。<c>taskkill</c> はそのプロセスのトップレベルの窓のうち、重なりのいちばん上の 1 枚にだけ送る</item>
///   <item><c>WM_ENDSESSION</c>: ログオフ・シャットダウン。<c>WM_QUERYENDSESSION</c> には止めないと答える</item>
/// </list>
/// <b>重なりのいちばん上に置く（最前面の印）。</b> 置かないと、<c>taskkill</c> の 1 通は後から出来た別の見えない窓へ行く
/// （WinForms がタスクバーに出さない窓のために作る持ち主の窓・<c>SystemEvents</c> の窓）。そこで受けた窓が壊れるだけで、アプリは終わらない。
/// 見えない窓なので、最前面でも画面には何も出ない。
/// 無いと、しまっている間はこれを受ける窓が 1 つも無く、<c>taskkill</c> では終わらず、ログオフでは記録を書き切らずに落ちる。
/// UI のスレッドで作る（ここで受けたものは UI のスレッドで走る）。
/// </summary>
internal sealed class AppMessageWindow : NativeWindow, IDisposable
{
    /// <summary>窓の題名。デスクトップの窓（SpoitableToolKit）と分ける（題名で窓を探すものに、こちらを掴ませない）。</summary>
    public const string Caption = "SpoitableToolKit.Messages";

    private const int WmClose = 0x0010;
    private const int WmQueryEndSession = 0x0011;
    private const int WmEndSession = 0x0016;

    private const int WsExTopmost = 0x00000008;
    private const int WsExToolWindow = 0x00000080;

    private readonly Action _exit;

    public AppMessageWindow(Action exit)
    {
        _exit = exit;
        CreateHandle(new CreateParams { Caption = Caption, ExStyle = WsExTopmost | WsExToolWindow });
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            // 既定の処理へ流さない（流すとこの窓が壊れるだけで、アプリは残る）。
            case WmClose:
                _exit();
                return;

            case WmQueryEndSession:
                m.Result = 1;
                return;

            // 止められた（ほかのアプリが断った）ときは wParam が 0。そのときは続ける。
            case WmEndSession when m.WParam != IntPtr.Zero:
                _exit();
                break;
        }

        base.WndProc(ref m);
    }

    public void Dispose() => DestroyHandle();
}
