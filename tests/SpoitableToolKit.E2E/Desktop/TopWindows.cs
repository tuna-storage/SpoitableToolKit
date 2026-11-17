using System.Runtime.InteropServices;
using System.Text;

namespace SpoitableToolKit.E2E.Desktop;

/// <summary>アプリのトップレベルの窓を探して、Windows の操作（Alt+F4 など）と同じ知らせを送る。Playwright はページの中しか触れないので。</summary>
internal static class TopWindows
{
    private const int WmClose = 0x0010;
    private const int WmSysCommand = 0x0112;
    private const int ScClose = 0xF060;

    private const uint GwOwner = 4;

    /// <summary>タスクマネージャーの「タスクの終了」と同じ（窓へ WM_CLOSE だけを送る）。WinForms は TaskManagerClosing として受ける。</summary>
    public static void Close(IntPtr window) => PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero);

    /// <summary>持ち主の窓（無ければ 0）。タスクバーに出さない WinForms の窓は、見えない持ち主の窓にぶら下がっている。</summary>
    public static IntPtr OwnerOf(IntPtr window) => GetWindow(window, GwOwner);

    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);

    /// <summary>そのプロセスの、見えているトップレベルの窓のうち、題名が合うもの（画面の外に置いた窓も「見えている」に入る）。</summary>
    public static IntPtr[] Of(int processId, string title)
    {
        List<IntPtr> found = [];
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner == processId && IsWindowVisible(window) && TitleOf(window) == title) found.Add(window);
            return true;
        }, IntPtr.Zero);
        return [.. found];
    }

    /// <summary>Alt+F4 とタスクバーの「ウィンドウを閉じる」と同じ（システムメニューの「閉じる」）。WinForms は利用者が閉じたもの（UserClosing）として受ける。</summary>
    public static void SystemClose(IntPtr window) => PostMessage(window, WmSysCommand, ScClose, IntPtr.Zero);

    private static string TitleOf(IntPtr window)
    {
        var text = new StringBuilder(256);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);

    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
