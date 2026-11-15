using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Localization;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Localization;

public sealed class LocalizerMergeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stk-merge-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Dictionary(string name, string ja, string? en = null)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(directory, "locales"));
        File.WriteAllText(Path.Combine(directory, "locales", "ja.json"), ja);
        if (en is not null) File.WriteAllText(Path.Combine(directory, "locales", "en.json"), en);
        return directory;
    }

    private LocaleSource Host() =>
        new(Dictionary("host", """{ "host.a": "本体", "common.ui.close": "閉じる" }""", """{ "host.a": "App", "common.ui.close": "Close" }"""), null);

    private LocaleSource Greedy() => new(Dictionary("hrs", """
        {
          "hrs.status.off": "未接続",
          "title": "題",
          "host.a": "乗っ取り",
          "common.ui.close": "とじる",
          "hrsx.b": "隣のモジュール",
          "hrs": "点が無い",
          "hrs.": "点で終わる"
        }
        """, """{ "hrs.status.off": "Not connected", "title": "Title" }"""), "hrs");

    private static void OnlyItsOwnKeyWentIn(Localizer text)
    {
        Assert.Equal("未接続", text["hrs.status.off"]);

        Assert.Equal("本体", text["host.a"]);
        Assert.Equal("閉じる", text["common.ui.close"]);

        // 持ち主でないキーは入らない（引くとキーのまま返る）。
        foreach (var key in (string[])["title", "hrsx.b", "hrs", "hrs."])
            Assert.Equal(key, text[key]);

        Assert.Equal(3, text.Snapshot().Count);
    }

    // モジュールは OnInit で状態の文言を引くが、モジュールの辞書をまとめて読むのは全部を読み込んだ後。足さないとキーのまま状態の口に残る。
    [Fact]
    public void AMergedDictionaryCanBeReadAtOnce()
    {
        var host = Host();
        var module = new LocaleSource(Dictionary("hrs", """{ "hrs.status.off": "未接続" }"""), "hrs");

        var text = new Localizer(NullLogger<Localizer>.Instance);
        text.Load("ja", [host]);
        Assert.Equal("hrs.status.off", text["hrs.status.off"]);

        text.Merge(module);

        Assert.Equal("未接続", text["hrs.status.off"]);

        Assert.Equal("本体", text["host.a"]);
    }

    [Fact]
    public void MergingAModulesDictionaryOnlyAddsKeysUnderItsOwnId()
    {
        var text = new Localizer(NullLogger<Localizer>.Instance);
        text.Load("ja", [Host()]);

        text.Merge(Greedy());

        OnlyItsOwnKeyWentIn(text);
    }

    // 足すときにだけ検査すると、読み直しで外したキーが戻る。本体より先に並んでいても、本体のキーは取れない。
    [Fact]
    public void ReloadingKeepsOutTheSameKeys()
    {
        var text = new Localizer(NullLogger<Localizer>.Instance);
        var (host, module) = (Host(), Greedy());

        text.Load("ja", [host, module]);
        OnlyItsOwnKeyWentIn(text);

        text.Load("ja", [module, host]);
        OnlyItsOwnKeyWentIn(text);

        text.Load("en", [host, module]);
        Assert.Equal("Not connected", text["hrs.status.off"]);
        Assert.Equal("title", text["title"]);
        Assert.Equal("Close", text["common.ui.close"]);
    }

    [Fact]
    public void AModuleWhoseIdHasDotsOwnsOnlyWhatIsUnderTheWholeId()
    {
        var source = new LocaleSource(Dictionary("extra", """{ "hrs.extra.title": "追加", "hrs.title": "親のふり" }"""), "hrs.extra");
        var text = new Localizer(NullLogger<Localizer>.Instance);
        text.Load("ja", [source]);

        Assert.Equal("追加", text["hrs.extra.title"]);
        Assert.Equal("hrs.title", text["hrs.title"]);
    }

    // 逆向き（hrs が hrs.extra.title を持つ）は hrs の下のキーなので止めない。hrs.extra と重なったら、先に読んだ方が残り、重なったことを知らせる。
    [Fact]
    public void WhenAParentAndAChildIdHoldTheSameKeyTheFirstOneReadStays()
    {
        var parent = new LocaleSource(Dictionary("parent", """{ "hrs.extra.title": "親" }"""), "hrs");
        var child = new LocaleSource(Dictionary("child", """{ "hrs.extra.title": "子" }"""), "hrs.extra");

        var logs = new Logs();
        var text = new Localizer(new Logger<Localizer>(logs));
        text.Load("ja", [parent, child]);

        Assert.Equal("親", text["hrs.extra.title"]);
        Assert.Contains("hrs.extra.title", Assert.Single(logs.Warnings));
        Assert.Contains("already exists", logs.Warnings[0]);

        text.Load("ja", [child, parent]);

        Assert.Equal("子", text["hrs.extra.title"]);
    }

    // 読み込みは、フォルダ名（hrs）と大文字小文字だけ違う Id（Hrs）を警告だけで通す。ここで区別すると、その辞書が 1 件も入らなくなる。
    [Fact]
    public void AnIdThatDiffersOnlyInCaseStillOwnsItsKeys()
    {
        var source = new LocaleSource(Dictionary("hrs", """{ "hrs.status.off": "未接続", "title": "題" }"""), "Hrs");
        var text = new Localizer(NullLogger<Localizer>.Instance);
        text.Load("ja", [source]);

        Assert.Equal("未接続", text["hrs.status.off"]);
        Assert.Equal("title", text["title"]);
    }

    // 同じ辞書を足すときと読み直すときの 2 回読むので、入れなかったキーは辞書ごとに 1 回だけ知らせる。別の言語の辞書は別に知らせる。
    [Fact]
    public void KeysLeftOutAreReportedOncePerFile()
    {
        var logs = new Logs();
        var text = new Localizer(new Logger<Localizer>(logs));
        var (host, module) = (Host(), Greedy());

        text.Load("ja", [host]);
        text.Merge(module);
        text.Load("ja", [host, module]);

        var warning = Assert.Single(logs.Warnings);
        Assert.Equal(
            Localizer.Foreign("hrs", Path.Combine(module.Directory, "locales", "ja.json"),
                ["title", "host.a", "common.ui.close", "hrsx.b", "hrs", "hrs."]),
            warning);

        text.Load("en", [host, module]);

        Assert.Equal(2, logs.Warnings.Count);
        Assert.Contains("en.json", logs.Warnings[1]);
        Assert.Contains("1 key(s)", logs.Warnings[1]);
    }

    // 知らせる文は、読んだ側がそのまま直せる形にする（モジュールを書くのは AI）: どのキーを・どの名前へ。
    // 本体のキー（host.…・common.…）は「改名」ではなく「消して、共通の文言をそのまま引く」。
    [Fact]
    public void TheWarningSaysHowToFixTheKeys()
    {
        const string path = @"C:\app\modules\hrs\locales\ja.json";

        Assert.Equal(
            @"1 key(s) in C:\app\modules\hrs\locales\ja.json were left out because they do not start with 'hrs.': title. " +
            "A module's translation keys start with the module's Id and a dot. " +
            "Rename 'title' to 'hrs.title' in every language file, and look it up under the new name.",
            Localizer.Foreign("hrs", path, ["title"]));

        Assert.Equal(
            @"2 key(s) in C:\app\modules\hrs\locales\ja.json were left out because they do not start with 'hrs.': common.ui.close, host.a. " +
            "A module's translation keys start with the module's Id and a dot. " +
            "Keys under 'host.' and 'common.' belong to the app, so a module can neither add nor replace them: " +
            "remove them from the module's files. The shared phrases (Close, Copy, Back…) are already there — " +
            "look them up as they are (CommonPhrases.Ui.Close in C#, 'common.ui.close' in a panel), " +
            "and keep any other wording under 'hrs.'.",
            Localizer.Foreign("hrs", path, ["common.ui.close", "host.a"]));

        // 両方あれば、両方の直し方を言う（改名の例には本体のキーを使わない）。
        var both = Localizer.Foreign("hrs", path, ["host.a", "panel.title"]);
        Assert.Contains("Rename 'panel.title' to 'hrs.panel.title'", both);
        Assert.Contains("Keys under 'host.' and 'common.' belong to the app", both);
    }
}
