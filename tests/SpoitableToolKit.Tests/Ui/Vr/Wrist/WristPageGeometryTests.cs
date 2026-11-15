using System.Text.RegularExpressions;
using SpoitableToolKit.Host.Ui.Vr.Wrist;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Vr.Wrist;

/// <summary>
/// 手首の絵の寸法は、C#（<see cref="WristHost"/>）と <c>wrist.html</c> の 2 か所に書いてある。
/// C# は絵の大きさと軸（手首の真ん中）で板を手首に乗せ、ページは同じ軸を真ん中に板を描く。
/// 片方だけ直すと、板が手首からずれる・右の通知の板が絵からはみ出す（どちらも VR でしか見えない）。
/// </summary>
public sealed partial class WristPageGeometryTests
{
    private static readonly string Page = File.ReadAllText(Path.Combine(FindRoot().FullName, "src", "SpoitableToolKit.Host", "web", "wrist.html"));

    private static DirectoryInfo FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpoitableToolKit.slnx")))
            directory = directory.Parent;

        return directory ?? throw new InvalidOperationException("リポジトリの根が見つからない");
    }

    private static int Constant(string name)
    {
        var match = Regex.Match(Page, $@"const {name} = (\d+);");
        Assert.True(match.Success, $"wrist.html に const {name} が無い");
        return int.Parse(match.Groups[1].Value);
    }

    /// <summary>タイル 112・間 10・左右の余白と枠 12（wrist.html の boardWidth と同じ式）。</summary>
    private static int BoardWidth => 24 + 112 * Constant("MAX_COLS") + 10 * (Constant("MAX_COLS") - 1);

    [Fact]
    public void ThePageCentresTheBoardOnTheSameAxis() => Assert.Equal(WristHost.Axis, Constant("AXIS"));

    // 板は軸を真ん中に置くので、板の幅は軸の 2 倍。広いと絵の左から切れ、狭いと左に透けた所が残る（そこも指すと当たる）。
    [Fact]
    public void TheBoardForSixShortcutsSpansTwiceTheAxis()
    {
        Assert.Equal(6, Constant("MAX_COLS"));
        Assert.Equal(2 * WristHost.Axis, BoardWidth);
    }

    [Fact]
    public void TheNotesFitInsideThePicture()
    {
        var notes = NotesWidth().Match(Page);
        Assert.True(notes.Success, "wrist.html の .notes に幅が無い");

        var right = WristHost.Axis + BoardWidth / 2 + Constant("GAP") + int.Parse(notes.Groups[1].Value);

        Assert.InRange(right, WristHost.Width - 16, WristHost.Width);
    }

    [GeneratedRegex(@"\.notes \{[^}]*?width: (\d+)px;", RegexOptions.Singleline)]
    private static partial Regex NotesWidth();
}
