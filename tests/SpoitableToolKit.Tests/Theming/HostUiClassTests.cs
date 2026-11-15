using System.Text.RegularExpressions;
using Xunit;

namespace SpoitableToolKit.Tests.Theming;

/// <summary>
/// 本体が配る部品の CSS（<c>web/_host/ui.css</c>）は <c>spoitable-</c> で始まる class にしか当たらない。
/// モジュールのページも同じ 1 枚を読むので、素の要素（<c>button</c>）や頭の無い class（<c>.tile</c>）に当てると、モジュール自身の見た目を上書きする。
/// </summary>
public sealed class HostUiClassTests
{
    private static readonly DirectoryInfo Root = FindRoot();

    private static DirectoryInfo FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpoitableToolKit.slnx")))
            directory = directory.Parent;

        return directory ?? throw new InvalidOperationException("リポジトリの根が見つからない");
    }

    private static string UiCss => Path.Combine(Root.FullName, "src", "SpoitableToolKit.Host", "web", "_host", "ui.css");

    private static readonly Regex Comment = new(@"/\*.*?\*/", RegexOptions.Singleline);

    /// <summary><c>{</c> の前の選択子（<c>@media</c> の中の規則も拾う。入れ子は 1 段まで）。</summary>
    private static readonly Regex Prelude = new(@"([^{}]+)\{");

    /// <summary><c>@keyframes</c> の中の段（<c>from</c>・<c>to</c>・<c>50%</c>）。選択子ではない。</summary>
    private static readonly Regex KeyframeStep = new(@"^(from|to|[\d.]+%)$");

    private static readonly Regex ClassName = new(@"\.(-?[A-Za-z_][\w-]*)");

    private static string[] Selectors() =>
    [
        .. Prelude.Matches(Comment.Replace(File.ReadAllText(UiCss), ""))
            .Select(m => m.Groups[1].Value.Trim())
            .Where(prelude => !prelude.StartsWith('@'))
            .SelectMany(prelude => prelude.Split(','))
            .Select(selector => selector.Trim())
            .Where(selector => selector.Length > 0 && !KeyframeStep.IsMatch(selector)),
    ];

    /// <summary>走査が空振りしていないか（置き場が変わって 0 件になると、下の 2 本は何も見ずに緑になる）。</summary>
    [Fact]
    public void TheScanFindsTheParts()
    {
        var selectors = Selectors();

        Assert.True(selectors.Length >= 40, $"ui.css の選択子が少なすぎる: {selectors.Length} 個");
        Assert.Contains(".spoitable-btn", selectors);
        Assert.Contains(".spoitable-tab", selectors);
    }

    /// <summary>
    /// どの選択子にも <c>spoitable-</c> の class が入っている。<c>button</c>・<c>[data-state="ok"]</c>・<c>:root</c> だけの選択子は、
    /// 部品を使っていないモジュールのページにも当たる。
    /// </summary>
    [Fact]
    public void EverySelectorNeedsASpoitableClass()
    {
        var offenders = Selectors().Where(selector => !selector.Contains(".spoitable-", StringComparison.Ordinal)).ToArray();

        Assert.True(offenders.Length == 0,
            "spoitable- の class を持たない選択子がある（部品を使っていないページにも当たる）:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// 選択子の中の class は全部 <c>spoitable-</c> で始まる。<c>.spoitable-tab.on</c> の <c>.on</c> のような頭の無い修飾は、
    /// モジュールの <c>.on</c> と混ざる（<c>spoitable-tab-on</c> と書く）。
    /// </summary>
    [Fact]
    public void EveryClassStartsWithSpoitable()
    {
        var offenders = Selectors()
            .SelectMany(selector => ClassName.Matches(selector).Select(m => (Selector: selector, Name: m.Groups[1].Value)))
            .Where(x => !x.Name.StartsWith("spoitable-", StringComparison.Ordinal))
            .Select(x => $"{x.Selector} の .{x.Name}")
            .Distinct()
            .ToArray();

        Assert.True(offenders.Length == 0,
            "spoitable- で始まらない class がある:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>画面のページ（シェル・手首・モジュールのパネル）。<c>Hrs/overlays</c> は OBS に貼る別物なので入らない。</summary>
    private static string[] Pages() =>
    [
        .. Directory.EnumerateFiles(
            Path.Combine(Root.FullName, "src", "SpoitableToolKit.Host", "web"), "*.html", SearchOption.AllDirectories),
        .. Directory.EnumerateDirectories(Path.Combine(Root.FullName, "src", "SpoitableToolKit.Modules"))
            .Select(d => Path.Combine(d, "web"))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.html", SearchOption.AllDirectories)),
    ];

    private const string UiLink = "/_host/ui.css";

    private static readonly Regex StyleHref = new(@"<link[^>]*href=""([^""]+\.css)""[^>]*>");

    /// <summary>
    /// 部品を使うページ（<c>spoitable-</c> の class か、<c>pick.js</c>・<c>when.js</c> を持つもの）は ui.css を<b>いちばん先に</b>読む。
    /// 読み忘れると部品が素のまま出る。後に読むと、ページの CSS が部品に掛けた上書き（hrs のタブの余白など）が同じ詳細度で負ける。
    /// </summary>
    [Fact]
    public void PagesThatUseThePartsLoadUiCssFirst()
    {
        var pages = Pages()
            .Select(p => (Page: Path.GetRelativePath(Root.FullName, p), Text: File.ReadAllText(p)))
            .Where(x => x.Text.Contains("spoitable-", StringComparison.Ordinal)
                        || x.Text.Contains("/_host/pick.js", StringComparison.Ordinal)
                        || x.Text.Contains("/_host/when.js", StringComparison.Ordinal))
            .ToArray();

        Assert.True(pages.Length >= 3, $"部品を使うページが少なすぎる: {pages.Length} 枚");

        var wrong = pages
            .Where(x => StyleHref.Matches(x.Text).Select(m => m.Groups[1].Value).FirstOrDefault() != UiLink)
            .Select(x => x.Page)
            .ToArray();

        Assert.True(wrong.Length == 0,
            $"部品を使うのに {UiLink} をいちばん先に読んでいないページがある:" + Environment.NewLine + string.Join(Environment.NewLine, wrong));
    }
}
