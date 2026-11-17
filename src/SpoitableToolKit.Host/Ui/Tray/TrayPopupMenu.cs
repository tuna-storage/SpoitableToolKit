using System.Runtime.InteropServices;

namespace SpoitableToolKit.Host.Ui.Tray;

/// <summary>
/// トレイのメニューを、Windows 標準のメニュー（HMENU と <c>TrackPopupMenuEx</c>）で出す。中身は <see cref="TrayMenuItem"/> から毎回作り、閉じたら捨てる。
/// 手順は Windows の通知領域のメニューの決まりどおり:
/// 持ち主の窓を前に出す（出さないと、メニューの外を押しても閉じない）→ <c>TPM_RETURNCMD</c> で出して押された番号を受け取る
/// → 持ち主へ何もしない知らせ（<c>WM_NULL</c>）を送る（出さないと、次にメニューを出したときにすぐ閉じることがある）→ メニューを捨てる。
/// UI のスレッドで呼ぶ。閉じるまで戻らない（その間もメッセージは回る）。
/// </summary>
internal static class TrayPopupMenu
{
    /// <summary>出して、押された項目の名前を返す。何も押さなかった（外を押した・Esc）・出せなかったら null。</summary>
    /// <param name="owner">持ち主の窓（メニューの知らせが届く先）。アプリの見えない窓。</param>
    /// <param name="at">出す位置（画面の座標。ふつうは右クリックしたときのマウスの位置）。</param>
    public static string? Show(IReadOnlyList<TrayMenuItem> items, IntPtr owner, Point at)
    {
        var commands = TrayCommands.For(items);
        var menu = Build(items, commands);
        if (menu == IntPtr.Zero) return null;

        try
        {
            SetForegroundWindow(owner);

            // 右から左へ開く地域の設定なら右に揃える（Windows の通知領域のメニューの例と同じ）。
            var align = GetSystemMetrics(SmMenuDropAlignment) != 0 ? TpmRightAlign : TpmLeftAlign;
            var chosen = TrackPopupMenuEx(menu, TpmReturnCmd | TpmRightButton | align, at.X, at.Y, owner, IntPtr.Zero);

            PostMessage(owner, WmNull, IntPtr.Zero, IntPtr.Zero);
            return commands.IdOf(chosen);
        }
        finally
        {
            // サブメニューも一緒に捨てられる。
            DestroyMenu(menu);
        }
    }

    /// <summary>開いているメニューを閉じる（押さなかったことになる）。メニューを出している UI のスレッドから呼ぶ。開いていなければ何もしない。</summary>
    public static void Close() => EndMenu();

    private static IntPtr Build(IReadOnlyList<TrayMenuItem> items, TrayCommands commands)
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return IntPtr.Zero;

        foreach (var item in items)
        {
            if (item.Separator)
            {
                AppendMenuW(menu, MfSeparator, UIntPtr.Zero, null);
                continue;
            }

            var flags = MfString | (item.Checked ? MfChecked : 0) | (item.Enabled ? 0 : MfGrayed);

            // & は Windows のメニューでは下線の印なので、字のまま出すには重ねる。
            var label = item.Label.Replace("&", "&&", StringComparison.Ordinal);

            if (item.Children.Count > 0)
            {
                var sub = Build(item.Children, commands);
                AppendMenuW(menu, flags | MfPopup, (UIntPtr)(ulong)sub, label);
                continue;
            }

            var number = commands.NumberOf(item);
            AppendMenuW(menu, flags, (UIntPtr)(uint)number, label);

            // 左クリックと同じ項目を太字にする（Windows の決まり）。
            if (item.Default) SetMenuDefaultItem(menu, (uint)number, 0);
        }

        return menu;
    }

    private const uint MfString = 0x0000;
    private const uint MfGrayed = 0x0001;
    private const uint MfChecked = 0x0008;
    private const uint MfPopup = 0x0010;
    private const uint MfSeparator = 0x0800;

    private const uint TpmLeftAlign = 0x0000;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmRightAlign = 0x0008;
    private const uint TpmReturnCmd = 0x0100;

    private const int SmMenuDropAlignment = 40;
    private const int WmNull = 0x0000;

    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr newItem, string? text);

    [DllImport("user32.dll")] private static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPosition);

    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr parameters);

    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")] private static extern bool EndMenu();

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}
