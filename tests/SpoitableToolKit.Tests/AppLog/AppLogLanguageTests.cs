using System.Text.RegularExpressions;
using Xunit;

namespace SpoitableToolKit.Tests.AppLog;

/// <summary>
/// アプリ自身のログは、画面の言語に関わらず英語（解析のために人へ送るもので、送り先が日本語を読むとは限らない）。
/// 新しい機能をその場の言語で書いてしまいやすいので、ここで見張る。
/// </summary>
public sealed partial class AppLogLanguageTests
{
    private static readonly DirectoryInfo Root = FindRoot();

    private static DirectoryInfo FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpoitableToolKit.slnx")))
            directory = directory.Parent;

        return directory ?? throw new InvalidOperationException("リポジトリの根が見つからない");
    }

    [GeneratedRegex("[\u3040-\u30ff\u4e00-\u9fff]")]
    private static partial Regex Japanese();

    [GeneratedRegex("\\$?@?\"(?:[^\"\\\\]|\\\\.)*\"")]
    private static partial Regex Literal();

    /// <summary>見るのは文字列リテラルだけ。コメントと XML ドキュメントはログに出ない。</summary>
    private static IEnumerable<(string File, int Line, string Literal)> Literals()
    {
        var source = Path.Combine(Root.FullName, "src");

        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;

                foreach (Match match in Literal().Matches(lines[i]))
                    yield return (Path.GetRelativePath(Root.FullName, file), i + 1, match.Value);
            }
        }
    }

    /// <summary>ここに載っているものだけが日本語のままでよい。<c>HdsFirewall</c> のものは Windows のファイアウォール設定に出る規則の説明で、アプリのログではない。</summary>
    private static readonly string[] Allowed =
    [
        "SpoitableToolKit の hrs モジュール",
        "LAN から受け取るための受信口",
    ];

    [Fact]
    public void AppLogMessagesAreNotJapanese()
    {
        var offenders = Literals()
            .Where(entry => Japanese().IsMatch(entry.Literal))
            .Where(entry => !Allowed.Any(allowed => entry.Literal.Contains(allowed, StringComparison.Ordinal)))
            .Select(entry => $"{entry.File}:{entry.Line}  {entry.Literal}")
            .ToArray();

        Assert.Empty(offenders);
    }

    /// <summary>数え忘れの保険。0 件なら走査の条件が間違っていて、この試験は何も見ていない。</summary>
    [Fact]
    public void TheScanItselfFindsSomething()
    {
        var literals = Literals().ToArray();

        Assert.True(literals.Length > 500, $"文字列リテラルを {literals.Length} 個しか拾えていない");

        // 許した分は実際に残っている（消えたらこの一覧も畳む）。
        Assert.All(Allowed, allowed =>
            Assert.Contains(literals, entry => entry.Literal.Contains(allowed, StringComparison.Ordinal)));
    }
}
