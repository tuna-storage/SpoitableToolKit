using SpoitableToolKit.Host.Ui.Browser;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Browser;

/// <summary>シェルの origin・入口の名前・メインフレームの 3 つが揃ったときだけ token を差し込む。<c>spoitable://</c> は fetch を許す登録をしていないので、e2e からは入口を取り寄せて中身を見られない。</summary>
public class ShellEntryTests
{
    private static bool Entry(string url, bool mainFrame = true)
    {
        var uri = new Uri(url);
        return SpoitableSchemeHandlerFactory.IsShellEntry(uri, uri.AbsolutePath.TrimStart('/'), mainFrame);
    }

    [Fact]
    public void TheShellGetsTheToken() => Assert.True(Entry("spoitable://ui/index.html"));

    /// <summary>入れ忘れると、手首の板はホストを 1 つも呼べないのに骨格だけが出て理由が分からない。</summary>
    [Fact]
    public void TheWristPageGetsTheToken() => Assert.True(Entry("spoitable://ui/wrist.html"));

    /// <summary>入口は名前で持つ（拡張子で決めない）。</summary>
    [Fact]
    public void OtherShellFilesDoNot() => Assert.False(Entry("spoitable://ui/other.html"));

    [Fact]
    public void AModuleOriginDoesNot() => Assert.False(Entry("spoitable://videourl/wrist.html"));

    /// <summary>モジュールが自分のフレームへ入口を引き込もうとしても、token は付かない。</summary>
    [Fact]
    public void AnInnerFrameDoesNot() => Assert.False(Entry("spoitable://ui/wrist.html", mainFrame: false));
}
