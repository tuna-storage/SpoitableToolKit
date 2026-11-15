using CefSharp;
using CefSharp.Handler;

namespace SpoitableToolKit.Host.Ui.Licenses;

internal sealed class CreditsLifeSpanHandler(Action<Uri> openOutside) : LifeSpanHandler
{
    protected override bool OnBeforePopup(
        IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string targetUrl, string targetFrameName,
        WindowOpenDisposition targetDisposition, bool userGesture, IPopupFeatures popupFeatures, IWindowInfo windowInfo,
        IBrowserSettings browserSettings, ref bool noJavascriptAccess, out IWebBrowser? newBrowser)
    {
        newBrowser = null;
        return Cancels(targetUrl, userGesture);
    }

    internal bool Cancels(string targetUrl, bool userGesture)
    {
        if (CreditsLinks.OpensOutside(targetUrl, userGesture) is { } outside) openOutside(outside);
        return true;
    }
}
