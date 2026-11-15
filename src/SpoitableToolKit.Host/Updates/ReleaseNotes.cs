using System.Text.RegularExpressions;
using SpoitableToolKit.Host.Updates.Models;

namespace SpoitableToolKit.Host.Updates;

/// <summary>
/// stable のノートは日本語と英語を <c>&lt;!-- lang: ja --&gt;</c> のような印の行で区切ってある（<c>.github/scripts/release-notes.ps1</c>）。
/// 印の無いノート（nightly の commit の一覧）はそのまま出す。
/// </summary>
internal static partial class ReleaseNotes
{
    public static IReadOnlyList<ReleaseNoteBlock> Read(string? markdown, string language)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];

        var sections = Sections(markdown);
        var lines = Pick(sections, language) ?? Pick(sections, "en") ?? sections[0].Lines;
        return Blocks(lines);
    }

    private sealed record Section(string? Language, List<string> Lines);

    private static List<Section> Sections(string markdown)
    {
        List<Section> sections = [new Section(null, [])];
        foreach (var line in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            if (LanguageMarker().Match(line) is { Success: true } marker)
                sections.Add(new Section(marker.Groups[1].Value.ToLowerInvariant(), []));
            else
                sections[^1].Lines.Add(line);
        }

        if (sections.Count > 1 && sections[0].Lines.All(string.IsNullOrWhiteSpace)) sections.RemoveAt(0);
        return sections;
    }

    private static List<string>? Pick(List<Section> sections, string language) =>
        sections.FirstOrDefault(s => s.Language is { } name
            && (name == language.ToLowerInvariant() || name == language.Split('-')[0].ToLowerInvariant()))?.Lines;

    private static List<ReleaseNoteBlock> Blocks(List<string> lines)
    {
        List<ReleaseNoteBlock> blocks = [];
        List<string> paragraph = [];

        void EndParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new ReleaseNoteBlock("text", string.Join(" ", paragraph), 0));
            paragraph.Clear();
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("<!--", StringComparison.Ordinal))
            {
                EndParagraph();
                continue;
            }

            if (Heading().Match(line) is { Success: true } heading)
            {
                EndParagraph();
                blocks.Add(new ReleaseNoteBlock("heading", Plain(heading.Groups[2].Value), heading.Groups[1].Length));
            }
            else if (Item().Match(line) is { Success: true } item)
            {
                EndParagraph();
                blocks.Add(new ReleaseNoteBlock("item", Plain(item.Groups[1].Value), 0));
            }
            else
            {
                paragraph.Add(Plain(line));
            }
        }

        EndParagraph();
        return blocks;
    }

    /// <summary>画面は字として入れるので、Markdown の飾りはここで外す。</summary>
    private static string Plain(string text)
    {
        text = Link().Replace(text, "$1");
        return text.Replace("**", string.Empty).Replace("__", string.Empty).Replace("`", string.Empty).Trim();
    }

    [GeneratedRegex(@"^\s*<!--\s*lang:\s*([A-Za-z-]+)\s*-->\s*$")]
    private static partial Regex LanguageMarker();

    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*#*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^[-*+]\s+(.+)$")]
    private static partial Regex Item();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();
}
