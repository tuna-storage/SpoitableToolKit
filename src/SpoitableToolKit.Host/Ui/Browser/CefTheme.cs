using CefSharp;
using CefSharp.DevTools.Emulation;

namespace SpoitableToolKit.Host.Ui.Browser;

/// <summary>
/// ページ側の <c>prefers-color-scheme</c> を Windows のテーマに合わせる。
/// CEF は Windows のテーマを勝手には見ず、何もしないと Windows がダークでもページには常に <c>light</c> と伝わる（<c>--force-dark-mode</c> を渡しても変わらない）。
/// DevTools のメディア機能の上書きで指定する。frame ごとではなくブラウザ全体に効くので、モジュールのページ（別 origin の iframe）にも同じテーマが伝わる。
/// </summary>
internal static class CefTheme
{
    public static async Task ApplyAsync(IWebBrowser browser)
    {
        var scheme = WindowsPrefersDark() ? "dark" : "light";

        try
        {
            using var devTools = browser.GetDevToolsClient();
            await devTools.Emulation.SetEmulatedMediaAsync(
                features: [new MediaFeature { Name = "prefers-color-scheme", Value = scheme }])
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // 窓が閉じかけている。テーマが合わないだけなので放っておく。
        }
    }

    /// <summary>Windows のアプリ用テーマが暗いか。読めなければ Windows の既定であるライトに倒す。</summary>
    private static bool WindowsPrefersDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
