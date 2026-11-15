using SpoitableToolKit.Host.Ui.Licenses;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Licenses;

/// <summary>窓に入れる handler の判断はここだけなので、e2e ではリンクを押さない（押すと試験の機械でブラウザが開く）。</summary>
public sealed class CreditsLinksTests
{
    [Theory]
    [InlineData("chrome://credits/", true)]
    [InlineData("chrome://credits/#print", true)]
    [InlineData("chrome://settings/", false)]
    [InlineData("chrome://version/", false)]
    [InlineData("https://www.kurims.kyoto-u.ac.jp/~ooura/fft.html", false)]
    [InlineData("about:blank", false)]
    [InlineData("", false)]
    public void OnlyTheCreditsPageStaysInTheWindow(string url, bool stays) =>
        Assert.Equal(stays, CreditsLinks.StaysOnThePage(url));

    [Theory]
    [InlineData("https://github.com/cefsharp/CefSharp", true, "https://github.com/cefsharp/CefSharp")]
    [InlineData("http://www.kurims.kyoto-u.ac.jp/~ooura/fft.html", true, "http://www.kurims.kyoto-u.ac.jp/~ooura/fft.html")]
    [InlineData("https://github.com/cefsharp/CefSharp", false, null)]
    [InlineData("file:///C:/Windows/System32/calc.exe", true, null)]
    [InlineData("javascript:alert(1)", true, null)]
    [InlineData("chrome://settings/", true, null)]
    [InlineData("spoitable://ui/index.html", true, null)]
    public void OnlyAClickedWebLinkOpensInTheBrowser(string url, bool userGesture, string? expected) =>
        Assert.Equal(expected, CreditsLinks.OpensOutside(url, userGesture)?.AbsoluteUri);

    [Theory]
    [InlineData("chrome://credits/", true, false, null)]
    [InlineData("https://github.com/cefsharp/CefSharp", true, true, "https://github.com/cefsharp/CefSharp")]
    [InlineData("https://github.com/cefsharp/CefSharp", false, true, null)]
    [InlineData("chrome://settings/", true, true, null)]
    public void TheWindowStaysOnTheCreditsAndHandsClickedLinksToTheBrowser(string url, bool userGesture, bool cancels, string? opened)
    {
        List<string> outside = [];
        var handler = new CreditsRequestHandler(uri => outside.Add(uri.AbsoluteUri));

        Assert.Equal(cancels, handler.Cancels(url, userGesture));
        Assert.Equal(opened is null ? [] : [opened], outside);
    }

    [Theory]
    [InlineData("https://github.com/cefsharp/CefSharp", true, "https://github.com/cefsharp/CefSharp")]
    [InlineData("https://github.com/cefsharp/CefSharp", false, null)]
    [InlineData("chrome://credits/", true, null)]
    public void NoPopupOpensAndAClickedLinkGoesToTheBrowser(string url, bool userGesture, string? opened)
    {
        List<string> outside = [];
        var handler = new CreditsLifeSpanHandler(uri => outside.Add(uri.AbsoluteUri));

        Assert.True(handler.Cancels(url, userGesture));
        Assert.Equal(opened is null ? [] : [opened], outside);
    }
}
