using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SpoitableToolKit.Host.Ui.Licenses;

internal static class CreditsLinks
{
    public const string Address = "chrome://credits/";

    public static bool StaysOnThePage(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "chrome" && uri.Host == "credits";

    public static Uri? OpensOutside(string url, bool userGesture) =>
        userGesture && Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : null;

    // CEF のスレッドから呼ばれる。ここで投げるとアプリごと落ちるので、開けなくても握る（関連付けが壊れているなど）。
    public static void OpenInBrowser(Uri uri, ILogger logger)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "Could not open {Url} in the browser", uri.AbsoluteUri);
        }
    }
}
