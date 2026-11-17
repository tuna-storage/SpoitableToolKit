using Microsoft.Extensions.Logging;

namespace SpoitableToolKit.Host.Ui.Licenses;

/// <summary>
/// chrome://credits の窓を 1 枚だけ出す。開いていれば前に出す。UI のスレッドから呼ぶ。
/// <para>
/// デスクトップの窓があれば、それを持ち主にする（一緒に最小化され、しまうと一緒に閉じる）。
/// 無ければ（トレイにしまっている間に VR の窓から押した）単独で出す — VR にいる人のデスクトップに、頼んでいない本体の窓まで開かない。
/// 単独の窓は、アプリを終了するときに CEF より先に閉じる（<see cref="Close"/>。残したまま CEF を畳むと、browser が生きたまま止まる）。
/// </para>
/// </summary>
internal sealed class ChromiumCreditsSlot(bool offscreen, ILogger logger)
{
    private ChromiumCreditsWindow? _open;

    public void Open(string title, IWin32Window? owner)
    {
        if (_open is { IsDisposed: false } open)
        {
            if (open.WindowState == FormWindowState.Minimized) open.WindowState = FormWindowState.Normal;
            open.Activate();
            return;
        }

        var credits = new ChromiumCreditsWindow(title, offscreen, logger);
        credits.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_open, credits)) _open = null;
        };
        _open = credits;

        if (owner is null) credits.Show();
        else credits.Show(owner);
    }

    public void Close()
    {
        if (_open is { IsDisposed: false } open) open.Close();
    }
}
