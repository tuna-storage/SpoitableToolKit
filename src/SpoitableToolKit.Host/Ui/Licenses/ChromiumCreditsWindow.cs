using CefSharp;
using CefSharp.WinForms;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Ui.Browser;
using SpoitableToolKit.Host.Ui.Desktop;

namespace SpoitableToolKit.Host.Ui.Licenses;

/// <summary>
/// chrome://credits を出す窓。chrome:// はシェルの origin からは iframe でも window.open でも開けないので、別の browser を立てる。
/// <c>stkHost</c> は bind しない（このページから本体を呼ばない）。
/// </summary>
internal sealed class ChromiumCreditsWindow : Form
{
    private readonly ChromiumWebBrowser _browser;

    public ChromiumCreditsWindow(string title, bool offscreen, ILogger logger)
    {
        Text = title;
        Icon = AppIcon.Window;
        ClientSize = new Size(900, 720);
        MinimumSize = new Size(480, 360);
        StartPosition = FormStartPosition.CenterScreen;

        if (offscreen)
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            ShowInTaskbar = false;
        }

        _browser = new ChromiumWebBrowser(CreditsLinks.Address) { Dock = DockStyle.Fill };
        void Open(Uri uri) => CreditsLinks.OpenInBrowser(uri, logger);
        _browser.RequestHandler = new CreditsRequestHandler(Open);
        _browser.LifeSpanHandler = new CreditsLifeSpanHandler(Open);
        _browser.MenuHandler = new ContextMenuBlocker();
        _browser.IsBrowserInitializedChanged += (_, _) => _ = CefTheme.ApplyAsync(_browser);

        Controls.Add(_browser);
    }
}
