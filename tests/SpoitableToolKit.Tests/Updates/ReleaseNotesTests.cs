using SpoitableToolKit.Host.Updates;
using SpoitableToolKit.Host.Updates.Models;
using Xunit;

namespace SpoitableToolKit.Tests.Updates;

public sealed class ReleaseNotesTests
{
    private const string Both = """
        <!-- lang: ja -->
        # SpoitableToolKit 2026.11.0

        最初のリリースです。

        ## 心拍
        - 心拍計から受け取ります

        <!-- lang: en -->
        # SpoitableToolKit 2026.11.0

        The first release.

        ## Heart rate
        - Reads your heart rate
        """;

    [Fact]
    public void TheScreensLanguageIsPicked()
    {
        var blocks = ReleaseNotes.Read(Both, "ja");

        ReleaseNoteBlock[] expected =
        [
            new("heading", "SpoitableToolKit 2026.11.0", 1),
            new("text", "最初のリリースです。", 0),
            new("heading", "心拍", 2),
            new("item", "心拍計から受け取ります", 0),
        ];
        Assert.Equal(expected, blocks);
    }

    [Fact]
    public void EnglishIsPickedForEnglish()
    {
        var blocks = ReleaseNotes.Read(Both, "en");

        Assert.Equal("The first release.", blocks[1].Text);
        Assert.DoesNotContain(blocks, b => b.Text.Contains("心拍", StringComparison.Ordinal));
    }

    [Fact]
    public void ALanguageWithNoNotesFallsBackToEnglish()
    {
        Assert.Equal("The first release.", ReleaseNotes.Read(Both, "fr")[1].Text);
    }

    [Fact]
    public void NotesWithoutMarksAreShownWhole()
    {
        var blocks = ReleaseNotes.Read("- fix: one thing (abc1234)\n- feat: another (def5678)\n", "ja");

        Assert.Equal(["fix: one thing (abc1234)", "feat: another (def5678)"], blocks.Select(b => b.Text));
        Assert.All(blocks, b => Assert.Equal("item", b.Kind));
    }

    [Fact]
    public void MarkdownDecorationsAreTakenOff()
    {
        var blocks = ReleaseNotes.Read("- **太字**と `コード` と [リンク](https://example.com)\n", "ja");

        Assert.Equal("太字と コード と リンク", Assert.Single(blocks).Text);
    }

    [Fact]
    public void LinesOfOneParagraphAreJoined()
    {
        var blocks = ReleaseNotes.Read("one line\nand the next\n\nanother", "en");

        Assert.Equal(["one line and the next", "another"], blocks.Select(b => b.Text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void NoNotesAreNoBlocks(string? markdown)
    {
        Assert.Empty(ReleaseNotes.Read(markdown, "ja"));
    }
}
