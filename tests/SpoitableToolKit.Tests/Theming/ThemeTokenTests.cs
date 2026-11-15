using System.Text.RegularExpressions;
using Xunit;

namespace SpoitableToolKit.Tests.Theming;

/// <summary>見た目の値がトークンの外に漏れていないことを見る。ページに 16 進が 1 つでも残ると、その色だけテーマに追従しない（見た目は普通に出るので気付きにくい）。ソースを直接読む。出力に持ってくると、持ってきた分しか見ない試験になる。</summary>
public sealed class ThemeTokenTests
{
    private static readonly DirectoryInfo Root = FindRoot();

    private static DirectoryInfo FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpoitableToolKit.slnx")))
            directory = directory.Parent;

        return directory ?? throw new InvalidOperationException("リポジトリの根が見つからない");
    }

    private static string[] Themes() =>
    [
        .. Directory.EnumerateFiles(
            Path.Combine(Root.FullName, "src", "SpoitableToolKit.Host", "themes"), "theme.css",
            SearchOption.AllDirectories),
    ];

    /// <summary>画面のページ。オーバーレイ（<c>Hrs/overlays</c>）は入らない — OBS に貼る別物で、テーマ機構には乗せない。</summary>
    private static string[] Pages() =>
    [
        .. Directory.EnumerateFiles(
            Path.Combine(Root.FullName, "src", "SpoitableToolKit.Host", "web"), "*.html",
            SearchOption.AllDirectories),
        .. Directory.EnumerateDirectories(Path.Combine(Root.FullName, "src", "SpoitableToolKit.Modules"))
            .Select(d => Path.Combine(d, "web"))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.html", SearchOption.AllDirectories)),
    ];

    /// <summary>骨格の CSS（<c>shell.css</c> / <c>panel.css</c>）と、本体が配る部品の CSS（<see cref="HostUi"/>）。色を持たず、値はトークンから引く。</summary>
    private static string[] Styles() =>
    [
        .. Pages().Select(Path.GetDirectoryName).OfType<string>().Distinct()
            .SelectMany(d => Directory.EnumerateFiles(d, "*.css"))
            .Append(HostUi)
            .Distinct(),
    ];

    /// <summary>部品の CSS（<c>web/_host/ui.css</c>）。<c>_host/</c> には html が無いので、ページのあるフォルダの CSS を見るだけでは拾えない（名指しで足す）。</summary>
    private static string HostUi => Path.Combine(Root.FullName, "src", "SpoitableToolKit.Host", "web", "_host", "ui.css");

    /// <summary>部品の JS（<c>web/_host/*.js</c>）。一覧・暦・吹き出しは自分の CSS を JS の中に持って差し込むので、ページと CSS だけを見ていると、消したトークンを使い続けていても気付けない。</summary>
    private static string[] Scripts() =>
    [
        .. Directory.EnumerateFiles(
            Path.Combine(Root.FullName, "src", "SpoitableToolKit.Host", "web", "_host"), "*.js"),
    ];

    /// <summary><c>#fff</c> / <c>#3b7de0</c> のような色の直書き。</summary>
    private static readonly Regex Hex = new(@"#[0-9a-fA-F]{3,8}\b");

    /// <summary><c>var(--accent)</c> の中身。</summary>
    private static readonly Regex Used = new(@"var\(\s*(--[A-Za-z0-9-]+)");

    /// <summary>定義の側。<c>--accent: #3b7de0;</c>。</summary>
    private static readonly Regex Defined = new(@"^\s*(--[A-Za-z0-9-]+)\s*:", RegexOptions.Multiline);

    /// <summary>走査が空振りしていないか。絞り込む方向の試験は、置き場所が変わって 0 件になっても他の試験は何も見ずに緑になるので、見張りが要る。</summary>
    [Fact]
    public void TheScanItselfFindsSomething()
    {
        Assert.True(Pages().Length >= 3, $"ページが見つからない: {Pages().Length} 件");
        Assert.True(Themes().Length >= 2, $"同梱テーマが足りない: {Themes().Length} 枚");
        Assert.True(File.Exists(HostUi), $"部品の CSS が見つからない: {HostUi}");
        Assert.Contains(HostUi, Styles());

        foreach (var theme in Themes())
            Assert.True(TokensIn(theme).Count >= 10, $"{Name(theme)} のトークンが少なすぎる");
    }

    /// <summary>ページに色を直書きしない。色はトークンからしか来ない。JS の中で色が要るとき（グラフの線など）は <c>getComputedStyle</c> でトークンを読む。</summary>
    [Fact]
    public void NoColourIsHardCodedInThePagesOrTheSkeleton()
    {
        var offenders = Pages().Concat(Styles())
            .Select(p => (Page: Path.GetRelativePath(Root.FullName, p), Hits: Hex.Matches(File.ReadAllText(p))))
            .Where(x => x.Hits.Count > 0)
            .Select(x => $"{x.Page}: {string.Join(", ", x.Hits.Select(m => m.Value).Distinct())}")
            .ToArray();

        Assert.True(offenders.Length == 0,
            "色が直書きされている。テーマのトークンから引くこと:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>どのページもトークンを引いている。引き忘れても変数が未定義になるだけでエラーは出ず、色が全部無い真っ白なページが出る。</summary>
    [Fact]
    public void EveryPageLinksTheThemeTokens()
    {
        var missing = Pages()
            .Where(p => !File.ReadAllText(p).Contains(ThemeLink, StringComparison.Ordinal))
            .Select(p => Path.GetRelativePath(Root.FullName, p))
            .ToArray();

        Assert.True(missing.Length == 0,
            "theme.css を引いていないページがある:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    /// <summary>骨格はテーマより先に読む。CSS は詳細度が同じなら後から来た方が勝つので、逆順だとテーマは色を変えられても配置を変えようとすると骨格に負け、骨格が書いていない性質だけ通る半分だけ変わった画面が出る。逃げ道が「テーマ側に <c>!important</c>」になるのを避けるため。</summary>
    [Fact]
    public void TheSkeletonIsLoadedBeforeTheTheme()
    {
        var wrong = Pages()
            .Select(p => (Page: Path.GetRelativePath(Root.FullName, p), Text: File.ReadAllText(p)))
            .Where(x => x.Text.IndexOf(ThemeLink, StringComparison.Ordinal) < LastOtherStyle(x.Text))
            .Select(x => x.Page)
            .ToArray();

        Assert.True(wrong.Length == 0,
            "テーマより後に骨格を読んでいるページがある（テーマが配置を上書きできない）:"
            + Environment.NewLine + string.Join(Environment.NewLine, wrong));
    }

    private const string ThemeLink = "/_host/theme.css";

    private static int LastOtherStyle(string html) =>
        StyleHref.Matches(html)
            .Select(m => m.Groups[1].Value)
            .Where(href => href.EndsWith(".css", StringComparison.OrdinalIgnoreCase) && href != ThemeLink)
            .Select(href => html.IndexOf(href, StringComparison.Ordinal))
            .DefaultIfEmpty(-1)
            .Max();

    private static readonly Regex StyleHref = new(@"<link[^>]*href=""([^""]+)""[^>]*>");

    /// <summary>使っているトークンはどの同梱テーマにも定義されている（綴り違いも、片方のテーマだけ足し忘れたのもここで止まる）。</summary>
    [Fact]
    public void EveryTokenInUseIsDefinedInEveryBundledTheme()
    {
        var used = Pages().Concat(Styles()).Concat(Scripts())
            .SelectMany(p => Used.Matches(File.ReadAllText(p))
                .Select(m => (Page: Path.GetRelativePath(Root.FullName, p), Token: m.Groups[1].Value)))
            .Distinct()
            .ToArray();

        var missing = Themes()
            .SelectMany(theme => used
                .Where(u => !TokensIn(theme).Contains(u.Token))
                .Select(u => $"{Name(theme)} に {u.Token} が無い（{u.Page} が使っている）"))
            .Distinct()
            .ToArray();

        Assert.True(missing.Length == 0,
            "テーマに無いトークンを使っている:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    /// <summary>同梱テーマはどれも同じ名前を持つ。片方にしか無いトークンがあると、そのテーマを選んだ人にだけ色が消える。</summary>
    [Fact]
    public void TheBundledThemesAllDefineTheSameNames()
    {
        var themes = Themes().Select(t => (Name: Name(t), Tokens: TokensIn(t))).ToArray();
        var all = themes.SelectMany(t => t.Tokens).ToHashSet();

        var gaps = themes
            .SelectMany(t => all.Except(t.Tokens).Select(token => $"{t.Name} に {token} が無い"))
            .ToArray();

        Assert.True(gaps.Length == 0,
            "同梱テーマの間で名前が揃っていない:" + Environment.NewLine + string.Join(Environment.NewLine, gaps));
    }

    private static HashSet<string> TokensIn(string file) =>
        [.. Defined.Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value)];

    private static string Name(string file) => Path.GetFileName(Path.GetDirectoryName(file))!;
}
