using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using SpoitableToolKit.Abstractions.Localization;
using Xunit;

namespace SpoitableToolKit.Tests.Localization;

/// <summary>ソースを直接読む（出力に持ってくると、持ってきた分しか見ない試験になる）。訳の抜けは画面にキーが出るだけで落ちないので、ここで見る。</summary>
public sealed class LocalizationTests
{
    private static readonly (string Owner, string Locales, string[] Sources)[] Owners =
    [
        ("host", "src/SpoitableToolKit.Host/locales",
            [
                "src/SpoitableToolKit.Host/web",
                "src/SpoitableToolKit.Host/Settings/HostConfig.cs",

                // メニューの文言は C# がキーで返す。走査の外に置くと、辞書に無いキーを返しても誰も気付かない。
                "src/SpoitableToolKit.Host/Ui/ContextMenu/ContextMenus.cs",
            ]),
        ("hrs", "src/SpoitableToolKit.Modules/Hrs/locales",
            ["src/SpoitableToolKit.Modules/Hrs/web", "src/SpoitableToolKit.Modules/Hrs/Config"]),
        ("videourl", "src/SpoitableToolKit.Modules/VideoUrl/locales",
            ["src/SpoitableToolKit.Modules/VideoUrl/web", "src/SpoitableToolKit.Modules/VideoUrl/Config"]),
    ];

    private const string Common = "common";

    private const string HostLocales = "src/SpoitableToolKit.Host/locales";

    private static readonly Regex Japanese = new(@"[぀-ヿ一-鿿]");

    /// <summary>改行。エスケープを書かずに済ませる（この試験は自分自身も走査の対象になる）。</summary>
    private const char NewLine = (char)10;

    /// <summary>引用符でくくられた文字列。キーは必ずリテラルとして書かれる（<c>t('host.log.file.none')</c>・<c>data-i18n="common.ui.close"</c> など）。</summary>
    private static readonly Regex Quoted = new(@"'([^'\\" + NewLine + @"]*)'|""([^""\\" + NewLine + @"]*)""");

    /// <summary>文字列の全体がこれに一致したときだけキーとみなす（<c>host.getStatus()</c> のようなプロパティアクセスをキーと見間違えない）。</summary>
    private static readonly Regex KeyLike = new(
        $@"^({string.Join('|', Owners.Select(o => o.Owner).Append(Common))})(\.[A-Za-z0-9]+)+$");

    /// <summary>この文で使われている翻訳キー。コメントは見ない（コメントに書いたキーは画面に出ない）。</summary>
    internal static IEnumerable<string> KeysIn(string content)
    {
        foreach (Match quoted in Quoted.Matches(WithoutComments(content)))
        {
            var value = quoted.Groups[1].Success ? quoted.Groups[1].Value : quoted.Groups[2].Value;

            if (KeyLike.IsMatch(value)) yield return value;
        }
    }

    private static readonly DirectoryInfo Root = FindRoot();

    private static DirectoryInfo FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpoitableToolKit.slnx")))
            directory = directory.Parent;

        return directory ?? throw new InvalidOperationException("リポジトリの根が見つからない");
    }

    private static Dictionary<string, string> Dictionary(string locales, string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(Root.FullName, locales, $"{language}.json")))!;

    /// <summary>画面のもの（HTML と設定の属性）。ログや C# のコメントは対象外。</summary>
    private static IEnumerable<(string Path, string Text)> Screens(string[] sources)
    {
        foreach (var source in sources)
        {
            var full = Path.Combine(Root.FullName, source);

            if (File.Exists(full))
            {
                yield return (source, File.ReadAllText(full));
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                yield return (Path.GetRelativePath(Root.FullName, file), File.ReadAllText(file));
        }
    }

    [Fact]
    public void NoJapaneseIsHardCodedInThePages()
    {
        List<string> found = [];

        foreach (var (_, _, sources) in Owners)
            foreach (var (path, content) in Screens(sources))
                foreach (var (number, line) in Lines(WithoutComments(content)))
                {
                    // console.* は画面ではなく開発者向け（ログと同じ扱いで対象外）。
                    if (line.Contains("console.", StringComparison.Ordinal)) continue;

                    if (Japanese.IsMatch(line)) found.Add($"{path}:{number} {line.Trim()}");
                }

        Assert.Empty(found);
    }

    [Fact]
    public void EveryKeyInUseExistsInTheDictionaries()
    {
        List<string> missing = [];

        var common = Dictionary(HostLocales, "ja");
        var commonSeen = 0;

        foreach (var (owner, locales, sources) in Owners)
        {
            var dictionary = Dictionary(locales, "ja");
            var seen = 0;

            foreach (var (path, content) in Screens(sources))
                foreach (var key in KeysIn(content))
                {
                    // 共通の文言は、どの持ち主の画面からでも本体の辞書で引く。
                    if (key.StartsWith($"{Common}.", StringComparison.Ordinal))
                    {
                        commonSeen++;
                        if (!common.ContainsKey(key)) missing.Add($"{path}: {key}");
                        continue;
                    }

                    // 自分の持ち分だけ見る（シェルの画面には他の持ち主のキーは出てこない）。
                    if (!key.StartsWith($"{owner}.", StringComparison.Ordinal)) continue;

                    seen++;
                    if (!dictionary.ContainsKey(key)) missing.Add($"{path}: {key}");
                }

            // 走査が空振りしていないこと（抽出を壊すと、この試験は黙って「何も見ていないから緑」になる）。
            Assert.True(seen >= 10, $"{owner}: キーが {seen} 件しか見つからない。走査が壊れている");
        }

        Assert.True(commonSeen >= 10, $"共通の文言のキーが {commonSeen} 件しか見つからない。走査が壊れている");
        Assert.Empty(missing);
    }

    /// <summary>定数だけ足すとモジュールが引いたときにキーのまま出て、辞書だけ足すと C# から名前で引けない。定数の値は名前から決まる（<c>CommonPhrases.Ui.Close</c> → <c>common.ui.close</c>）。パネルの HTML はキーを字で書くので、名前から読み替えられる形にそろえる。</summary>
    [Fact]
    public void EveryCommonPhraseHasItsTranslations()
    {
        var constants = typeof(CommonPhrases).GetNestedTypes()
            .SelectMany(group => group.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
                .Select(field => (Group: group.Name, field.Name, Key: (string)field.GetRawConstantValue()!)))
            .ToArray();

        Assert.True(constants.Length >= 20, $"定数が {constants.Length} 個しか見つからない");

        foreach (var (group, name, key) in constants)
            Assert.Equal($"{Common}.{group.ToLowerInvariant()}.{char.ToLowerInvariant(name[0])}{name[1..]}", key);

        foreach (var language in (string[])["ja", "en"])
        {
            var inDictionary = Dictionary(HostLocales, language).Keys
                .Where(key => key.StartsWith($"{Common}.", StringComparison.Ordinal)).Order().ToArray();

            Assert.Equal(constants.Select(c => c.Key).Order().ToArray(), inDictionary);
        }
    }

    /// <summary>持つと、本体の訳を直してもそのモジュールだけ古いままになる。どの言語でも共通の文言と同じときだけ同じとみなす（英語だけ同じ「Copied」でも、日本語が「コピーしました」なら別の文）。</summary>
    [Fact]
    public void TheModulesDoNotRepeatACommonPhrase()
    {
        string[] languages = ["ja", "en"];
        var host = languages.ToDictionary(language => language, language => Dictionary(HostLocales, language));
        var common = host["ja"].Keys.Where(key => key.StartsWith($"{Common}.", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(common);

        List<string> repeated = [];

        foreach (var (owner, locales, _) in Owners.Where(o => o.Owner != "host"))
        {
            var own = languages.ToDictionary(language => language, language => Dictionary(locales, language));

            foreach (var key in own["ja"].Keys)
                if (common.FirstOrDefault(c => languages.All(l => own[l][key] == host[l][c])) is { } same)
                    repeated.Add($"{owner}: {key} → {same}");
        }

        Assert.Empty(repeated);
    }

    [Fact]
    public void APropertyAccessInCodeIsNotAKey()
    {
        Assert.Empty(KeysIn("const x = await host.getStatus(); hrs.foo.bar; videourl.a.b.c;"));

        Assert.Empty(KeysIn("// 'host.log.file.none' を使う"));
        Assert.Empty(KeysIn("/* \"hrs.panel.title\" */"));

        // 文字列の全体がキーであること（文の中に紛れているものは引かれない）。
        Assert.Empty(KeysIn("console.log('host.log.file.none is missing')"));
    }

    [Fact]
    public void KeysInsideQuotesArePickedUp()
    {
        Assert.Equal(["host.log.file.none"], KeysIn("el.x.textContent = t('host.log.file.none');"));
        Assert.Equal(["common.ui.close"], KeysIn("<button data-i18n=\"common.ui.close\"></button>"));
        Assert.Equal(["hrs.settings.device.label"],
            KeysIn("[SpoitableSettingsLabelLocalize(\"hrs.settings.device.label\")]"));

        // 三項の両側など、1 行に 2 つあっても両方。
        Assert.Equal(["common.ui.copied", "videourl.panel.failed"],
            KeysIn("t(ok ? 'common.ui.copied' : 'videourl.panel.failed')"));
    }

    [Fact]
    public void TheJapaneseAndEnglishKeySetsMatch()
    {
        foreach (var (owner, locales, _) in Owners)
        {
            var ja = Dictionary(locales, "ja").Keys.Order().ToArray();
            var en = Dictionary(locales, "en").Keys.Order().ToArray();

            Assert.Equal(ja, en);

            // 持ち主で始まっていないキーは、本体が入れない（モジュール）。本体の辞書には、共通の文言も入っている。
            Assert.All(ja, key => Assert.True(
                key.StartsWith($"{owner}.", StringComparison.Ordinal)
                || (owner == "host" && key.StartsWith($"{Common}.", StringComparison.Ordinal)),
                $"{owner}: {key}"));
        }
    }

    /// <summary>差し込みの数が言語で食い違っていない（<c>{0}</c> を落とすと材料が消える）。</summary>
    [Fact]
    public void ThePlaceholderCountsMatchAcrossLanguages()
    {
        var placeholder = new Regex(@"\{\d+\}");

        foreach (var (_, locales, _) in Owners)
        {
            var ja = Dictionary(locales, "ja");
            var en = Dictionary(locales, "en");

            foreach (var (key, text) in ja)
                Assert.Equal(
                    placeholder.Matches(text).Select(m => m.Value).Distinct().Order(),
                    placeholder.Matches(en[key]).Select(m => m.Value).Distinct().Order());
        }
    }

    private static IEnumerable<(int Number, string Line)> Lines(string content) =>
        content.ReplaceLineEndings("\n").Split('\n').Select((line, index) => (index + 1, line));

    /// <summary>コメントを空白に潰す。行数は保つ。<c>/* */</c> と <c>&lt;!-- --&gt;</c> は複数行にまたがるので、開始行だけ見て弾くと続きの行が「直書きの日本語」に見える。</summary>
    private static string WithoutComments(string content)
    {
        var text = new System.Text.StringBuilder(content.Length);
        var i = 0;

        while (i < content.Length)
        {
            var rest = content.AsSpan(i);

            if (rest.StartsWith("/*")) { i = Skip(content, i, "*/", text); continue; }
            if (rest.StartsWith("<!--")) { i = Skip(content, i, "-->", text); continue; }

            // 行コメント。URL の "//" を巻き込まないよう、直前が ":" のときは見送る。
            if (rest.StartsWith("//") && (i == 0 || content[i - 1] != ':'))
            {
                i = Skip(content, i, NewLine.ToString(), text);
                continue;
            }

            text.Append(content[i]);
            i++;
        }

        return text.ToString();
    }

    /// <summary>閉じるまで飛ばす。改行だけは残して行数を合わせる。</summary>
    private static int Skip(string content, int from, string until, System.Text.StringBuilder text)
    {
        var end = content.IndexOf(until, from, StringComparison.Ordinal);
        end = end < 0 ? content.Length : end + until.Length;

        for (var i = from; i < end; i++)
            if (content[i] == NewLine) text.Append(NewLine);

        return end;
    }
}
