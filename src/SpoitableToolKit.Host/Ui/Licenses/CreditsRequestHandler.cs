using CefSharp;
using CefSharp.Handler;

namespace SpoitableToolKit.Host.Ui.Licenses;

internal sealed class CreditsRequestHandler(Action<Uri> openOutside) : RequestHandler
{
    protected override bool OnBeforeBrowse(
        IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool userGesture, bool isRedirect)
        => Cancels(request.Url, userGesture);

    internal bool Cancels(string url, bool userGesture)
    {
        if (CreditsLinks.StaysOnThePage(url)) return false;

        if (CreditsLinks.OpensOutside(url, userGesture) is { } outside) openOutside(outside);
        return true;
    }
}
