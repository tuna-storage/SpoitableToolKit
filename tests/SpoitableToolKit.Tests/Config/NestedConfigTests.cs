using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Config.Attributes;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Host.Config;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Config;

public sealed class NestedConfigTests
{
    private enum Mode
    {
        Local,
        Server,
    }

    /// <summary>入/切のスイッチが入れ物の中にある形。印は書かない（ホストが <c>ITogglableSettings.Enabled</c> をそう扱う）。</summary>
    private sealed class Auth : ITogglableSettings
    {
        public bool Enabled { get; set; }

        public string Token = "";

        public int RetryCount = 3;
    }

    /// <summary>スイッチが<b>親にある</b>形。入れ物の中は全部隠れてよい。</summary>
    private sealed class Server(Config owner) : IConditionalSettings
    {
        public string Url = "";

        public Auth Auth { get; } = new();

        public bool IsEnabled => owner.Mode == Mode.Server;
    }

    /// <summary>項目分けだけ。<b>出し入れをしないので中身は常に出る。</b></summary>
    private sealed class Advanced : ISpoitableSettings
    {
        public int Timeout = 30;
    }

    /// <summary>
    /// 名前も置き方も<b>型の側に書いた</b>形。置くたびに書かなくてよい。
    /// </summary>
    [SpoitableSettingsLabel("From the type")]
    [SpoitableSettingsDescription("Described by the type")]
    [SpoitableSettingsCategory(SpoitableCategory.Foldable)]
    private sealed class Named : ISpoitableSettings
    {
        public int Value = 1;
    }

    /// <summary>翻訳キーを型に書いた形。<b>直書きより優先される</b>のは平らな項目と同じ。</summary>
    [SpoitableSettingsLabel("Direct")]
    [SpoitableSettingsLabelLocalize("box.label")]
    private sealed class Translated : ISpoitableSettings
    {
        public int Value = 1;
    }

    /// <summary>型にもメンバーにもラベルを書かない形。<b>落とし先はメンバー名。</b></summary>
    private sealed class Anonymous : ISpoitableSettings
    {
        public int Value = 1;
    }

    /// <summary>入れ物ではないのに、型にラベルが付いている形。設定としては JSON の欄のまま。型のラベルを見るのは入れ物のときだけ。</summary>
    [SpoitableSettingsLabel("Leaked from the type")]
    private sealed class Payload
    {
        public int Amount { get; set; }
    }

    /// <summary>
    /// 名前の付け方だけを見るための設定。<b>同じ型を 2 か所に置く</b> —
    /// 片方はメンバーに名前を書き、片方は書かない。
    /// </summary>
    private sealed class Labelled : IModuleConfig
    {
        public Named Plain { get; } = new();

        [SpoitableSettingsLabel("From the member")]
        [SpoitableSettingsDescription("Described by the member")]
        public Named Overridden { get; } = new();

        public Translated Translated { get; } = new();

        /// <summary>メンバー名と型の名前をわざと違える（同じだと、落とし先がどちらでも通って試験が何も見ていない状態になる）。</summary>
        public Anonymous Unnamed { get; } = new();

        public Advanced Advanced { get; } = new();

        /// <summary>入れ物ではない。<b>型に付いた名前を引き継がない</b>ことを見るためだけの項目。</summary>
        public Payload Blob = new();
    }

    private sealed class Config : IModuleConfig
    {
        public bool Ask = true;

        public Mode Mode = Mode.Local;

        public Server Server { get; }

        public Advanced Advanced { get; } = new();

        public Config() => Server = new Server(this);
    }

    private static ModuleConfigBinding Bind(Config config) => new(config, NullLogger.Instance);

    private static JsonObject Json(string json) => (JsonObject)JsonNode.Parse(json)!;

    /// <summary>describe した結果を <c>キー → 出すか</c> で引く。</summary>
    private static Dictionary<string, bool> Shown(object[] described) =>
        described.ToDictionary(Key, item => Get(item, "shown") is true);

    private static string Key(object item) => (string)Get(item, "key")!;

    /// <summary>見出しに載っているスイッチ。無ければ null。</summary>
    private static (string Key, bool Value)? Switch(object[] described, string key)
    {
        var found = described.First(i => Key(i) == key);
        return Get(found, "toggle") is { } toggle
            ? ((string)Get(toggle, "key")!, (bool)Get(toggle, "value")!)
            : null;
    }

    private static object? Get(object item, string name) =>
        item.GetType().GetProperty(name)?.GetValue(item);

    /// <summary>並ぶ順は見ない（reflection が返す順に任せる）。見出しが自分の子より前に来ることだけが約束。</summary>
    [Fact]
    public void NestedMembersArePickedUpWithJoinedPaths()
    {
        var keys = Bind(new Config()).Describe(NullLocalizer.Instance).Select(Key).ToArray();

        Assert.Equal(
            // Server.Auth.Enabled は**行にならない**。見出し（Server.Auth）が持つ。
            ["Advanced", "Advanced.Timeout", "Ask", "Mode", "Server", "Server.Auth",
                "Server.Auth.RetryCount", "Server.Auth.Token", "Server.Url"],
            [.. keys.Order(StringComparer.Ordinal)]);

        // 見出しは自分の子より前。**後ろに来ると、画面で親の下に並べられない。**
        Assert.True(Array.IndexOf(keys, "Server") < Array.IndexOf(keys, "Server.Url"));
        Assert.True(Array.IndexOf(keys, "Server.Auth") < Array.IndexOf(keys, "Server.Auth.Token"));
    }

    /// <summary>切の間は中が出ない。入へ戻す手が外にある形では見出しも出さない（出すと中身の無い見出しだけが並ぶ）。</summary>
    [Fact]
    public void AnOffSectionWhoseSwitchIsOutsideHidesItsHeadingToo()
    {
        var shown = Shown(Bind(new Config()).Describe(NullLocalizer.Instance));

        Assert.True(shown["Ask"]);
        Assert.True(shown["Mode"]);           // 戻す手は外に出ている
        Assert.False(shown["Server"]);        // 見出し
        Assert.False(shown["Server.Url"]);    // 中身
        Assert.False(shown["Server.Auth"]);   // 中の入れ物ごと

        var on = Shown(Bind(new Config { Mode = Mode.Server }).Describe(NullLocalizer.Instance));
        Assert.True(on["Server"]);
        Assert.True(on["Server.Url"]);
    }

    /// <summary>出す条件を決めるスイッチが<b>入れ物の中にある</b>形（<see cref="SpoitableSettingsToggleAttribute"/>）。</summary>
    private sealed class Proxy : IConditionalSettings
    {
        [SpoitableSettingsToggle]
        public bool Use = false;

        public string Host = "";

        public bool IsEnabled => Use;
    }

    private sealed class ProxyConfig : IModuleConfig
    {
        public Proxy Proxy { get; } = new();
    }

    /// <summary>入へ戻すスイッチが中にあるなら、切の間も見出しは残す（消すとそのスイッチを押しに行けない）。</summary>
    [Fact]
    public void AnOffSectionKeepsItsHeadingWhenItsSwitchIsInside()
    {
        var shown = Shown(new ModuleConfigBinding(new ProxyConfig(), NullLogger.Instance).Describe(NullLocalizer.Instance));

        Assert.True(shown["Proxy"]);
        Assert.True(shown["Proxy.Use"]);
        Assert.False(shown["Proxy.Host"]);
    }

    /// <summary>出す条件のほかに、get だけのプロパティを持つ入れ物。</summary>
    private sealed class Gated : IConditionalSettings
    {
        public string Host = "";

        public int Fixed => 1;

        public bool IsEnabled => Host.Length > 0;
    }

    private sealed class GatedConfig : IModuleConfig
    {
        public Gated Gated { get; } = new();
    }

    /// <summary>出す条件（<see cref="IConditionalSettings.IsEnabled"/>）は書けない設定として知らせない（設定ではないので、知らせると起動のたびに意味の無い警告が出る）。</summary>
    [Fact]
    public void TheConditionIsNotReportedAsASettingThatCannotBeWritten()
    {
        var logs = new Logs();

        _ = new ModuleConfigBinding(new GatedConfig(), logs);

        Assert.DoesNotContain(logs.Warnings, w => w.Contains("IsEnabled"));
        Assert.Contains(logs.Warnings, w => w.Contains("Gated.Fixed") && w.Contains("is not writable"));
    }

    [Fact]
    public void TheOnOffSwitchRidesOnTheHeading()
    {
        var config = new Config { Mode = Mode.Server };
        config.Server.Auth.Enabled = true;

        var described = Bind(config).Describe(NullLocalizer.Instance);

        Assert.Equal(("Server.Auth.Enabled", true), Switch(described, "Server.Auth"));

        Assert.DoesNotContain("Server.Auth.Enabled", described.Select(Key));

        Assert.Null(Switch(described, "Advanced"));
        Assert.Null(Switch(described, "Server"));
    }

    /// <summary>切にしても見出しは残るので入に戻せる。見出しごと消すとスイッチも消え、二度と入に戻せない（config.json を手で書く以外に道が無い）。</summary>
    [Fact]
    public void TurningItOffKeepsTheHeadingAndItsSwitch()
    {
        var config = new Config { Mode = Mode.Server };   // Server は入、Auth は切
        var described = Bind(config).Describe(NullLocalizer.Instance);
        var shown = Shown(described);

        Assert.True(shown["Server.Url"]);
        Assert.True(shown["Server.Auth"]);            // 見出しは残る
        Assert.False(shown["Server.Auth.Token"]);     // 中身は出ない

        Assert.Equal(("Server.Auth.Enabled", false), Switch(described, "Server.Auth"));
    }

    /// <summary>項目分けだけの入れ物は中身が常に出る（切に倒すと、束ねただけの設定が丸ごと触れなくなる）。</summary>
    [Fact]
    public void APlainGroupingAlwaysShowsItsContents()
    {
        var shown = Shown(Bind(new Config()).Describe(NullLocalizer.Instance));

        Assert.True(shown["Advanced"]);
        Assert.True(shown["Advanced.Timeout"]);
    }

    /// <summary>親が切なら、その下の見出しごと出ない（押せる相手が無い）。見出しが残るのは自分が切のときだけ。</summary>
    [Fact]
    public void AnOffParentAlsoHidesTheHeadingsBelowIt()
    {
        var shown = Shown(Bind(new Config()).Describe(NullLocalizer.Instance));

        Assert.False(shown["Server.Auth"]);
    }

    /// <summary>子が入でも親が切なら出ない。親と子が両方切の形だけで確かめると、子の切が親の切を隠し、先祖まで見に行く仕掛けを外しても落ちない。</summary>
    [Fact]
    public void AnOnChildStaysHiddenUnderAnOffParent()
    {
        var config = new Config();          // Mode = Local なので Server は切
        config.Server.Auth.Enabled = true;  // 子だけ入

        var shown = Shown(Bind(config).Describe(NullLocalizer.Instance));

        Assert.False(shown["Server.Auth.Token"]);
        Assert.False(shown["Server.Auth"]);
    }

    [Fact]
    public void TheSavedShapeIsNested()
    {
        var config = new Config { Mode = Mode.Server };
        config.Server.Url = "https://example.test";
        config.Server.Auth.Token = "t";

        var saved = Bind(config).Save();

        Assert.Equal("Server", saved["Mode"]!.GetValue<string>());
        Assert.Equal("https://example.test", saved["Server"]!["Url"]!.GetValue<string>());
        Assert.Equal("t", saved["Server"]!["Auth"]!["Token"]!.GetValue<string>());
    }

    [Fact]
    public void NestedValuesAreReadBack()
    {
        var config = new Config();

        Bind(config).Load(
            Json("""{ "Ask": false, "Server": { "Url": "https://a.test", "Auth": { "Token": "z" } } }"""),
            NullLogger.Instance);

        Assert.False(config.Ask);
        Assert.Equal("https://a.test", config.Server.Url);
        Assert.Equal("z", config.Server.Auth.Token);
    }

    [Fact]
    public void AMissingSectionInTheFileDoesNotBlockTheRest()
    {
        var config = new Config();

        Bind(config).Load(Json("""{ "Ask": false }"""), NullLogger.Instance);

        Assert.False(config.Ask);
        Assert.Equal("", config.Server.Url);
    }

    /// <summary>隠れている項目にも適用できる（値は消さない約束なので、隠れる前に溜めた変更も通す）。</summary>
    [Fact]
    public void AHiddenItemCanStillBeApplied()
    {
        var config = new Config();   // Mode = Local なので Server の中は隠れている
        var binding = Bind(config);

        var changed = binding.ApplyNow(
            Json("""{ "Server.Url": "https://b.test" }"""), NullLocalizer.Instance);

        Assert.Equal("https://b.test", config.Server.Url);
        Assert.Equal(["Server.Url"], changed);
    }

    /// <summary>当てたつもりで描ける（スイッチを押した瞬間に中身が出ないと、入にした直後に打ち込めない）。描いたあとは元に戻っている。</summary>
    [Fact]
    public void PreviewingAppliesThenRollsBack()
    {
        var config = new Config();
        var binding = Bind(config);

        var shown = Shown(binding.DescribeWith(
            Json("""{ "Mode": "Server" }"""), NullLocalizer.Instance));

        Assert.True(shown["Server.Url"]);

        // 見せただけ。**書いてはいない。**
        Assert.Equal(Mode.Local, config.Mode);
        Assert.False(Shown(binding.Describe(NullLocalizer.Instance))["Server.Url"]);
    }

    /// <summary>
    /// 下見の試験の設定。書かれた回数を数え、候補は実行時の中身（写しには写らない）から出す。
    /// get だけの項目を 1 つ持つ（組み立てのたびに「書けない」と警告が出る — 写しの組み立てが黙っているかを見るため）。
    /// </summary>
    private sealed class Watched : IModuleConfig
    {
        public int Fixed => 1;

        private int _seconds = 30;

        public int Writes;

        public int Seconds
        {
            get => _seconds;
            set
            {
                _seconds = value;
                Writes++;
            }
        }

        public string Theme = "";

        /// <summary>実行時に本体が入れる一覧（設定ではない。<c>HostConfig.ThemeNames</c> と同じ形）。</summary>
        internal string[] Names = [];

        public IReadOnlyList<string>? SuggestValuesFor(string memberName) => memberName == nameof(Theme) ? Names : null;
    }

    /// <summary>
    /// <b>下見は共有の設定に 1 回も書かない</b>（打つたびに呼ばれ、書くとモジュールの裏の thread が打ちかけの値を読める）。
    /// 描いた中身は打った値で、候補は実行時の中身（本物の側にしか無い）から出る。
    /// </summary>
    [Fact]
    public void ThePreviewNeverWritesTheSharedSettingsAndStillOffersTheLiveSuggestions()
    {
        var logs = new Logs();
        var config = new Watched { Names = ["dark", "light"], Theme = "dark" };
        var binding = new ModuleConfigBinding(config, logs);
        Assert.Contains(logs.Warnings, w => w.Contains("Fixed") && w.Contains("is not writable"));
        var warnings = logs.Warnings.Count;
        config.Writes = 0;

        var described = binding.DescribeWith(Json("""{ "Seconds": -5 }"""), NullLocalizer.Instance);

        Assert.Equal(0, config.Writes);
        Assert.Equal(30, config.Seconds);
        Assert.Equal("-5", Get(described.First(i => Key(i) == "Seconds"), "value"));

        // 打っていない値は本物の値のまま（既定値ではない）。候補は本物から。写しの組み立ては警告を足さない。
        Assert.Equal("\"dark\"", Get(described.First(i => Key(i) == "Theme"), "value"));
        Assert.Equal(["dark", "light"], (string[])Get(described.First(i => Key(i) == "Theme"), "suggestions")!);
        Assert.Equal(warnings, logs.Warnings.Count);
    }

    /// <summary>
    /// 本物の本体の設定でも、下見は打った値のほかは本物の描画と同じ（写しに写らない実行時の中身 — テーマの一覧など — に、出す / 出さない・候補が頼っていない）。
    /// 写しを作っても警告は出ない（打つたびに作るので、出ればアプリログに積もる）。
    /// </summary>
    [Fact]
    public void ThePreviewOfTheRealHostSettingsMatchesTheLiveDescriptionExceptForTheTypedValue()
    {
        var logs = new Logs();
        var binding = new ModuleConfigBinding(new HostConfig { ThemeNames = () => ["dark", "light"] }, logs);
        var live = binding.Describe(NullLocalizer.Instance);
        var key = (string)Get(live.First(i => Get(i, "name") as string == nameof(HostConfig.AppLogKeepDays)), "key")!;
        var warnings = logs.Warnings.Count;

        var preview = binding.DescribeWith(Json($$"""{ "{{key}}": 9 }"""), NullLocalizer.Instance);

        Assert.Equal(warnings, logs.Warnings.Count);
        Assert.Equal(live.Select(Key), preview.Select(Key));
        for (var i = 0; i < live.Length; i++)
        {
            if (Key(live[i]) == key)
                Assert.Equal("9", Get(preview[i], "value"));
            else
                Assert.Equal(JsonSerializer.Serialize(live[i]), JsonSerializer.Serialize(preview[i]));
        }
    }

    /// <summary>読めない値は飛ばす（打ちかけで画面が組み直せなくなると、打ち終わる前に何も見えなくなる）。</summary>
    [Fact]
    public void AHalfTypedValueDoesNotStopThePreview()
    {
        var binding = Bind(new Config());

        var shown = Shown(binding.DescribeWith(
            Json("""{ "Mode": "Server", "Ask": "not a bool" }"""), NullLocalizer.Instance));

        Assert.True(shown["Server.Url"]);
    }

    private sealed class Loop : ISpoitableSettings
    {
        public Cyclic? Back;

        public string Name = "";

        public bool IsEnabled => true;
    }

    private sealed class Cyclic : IModuleConfig, ISpoitableSettings
    {
        public Loop Inner = new();

        public bool IsEnabled => true;

        public Cyclic() => Inner.Back = this;
    }

    /// <summary>親を指し返していても戻ってくる。<c>IsEnabled</c> で親を見る形だと子が親への参照を持つのが普通で、素直に潜ると 子 → 親 → 子 で終わらない。見張りを外すとこの試験は固まる。</summary>
    [Fact]
    public void ASectionPointingBackAtItsParentStillFinishesBinding()
    {
        var keys = new ModuleConfigBinding(new Cyclic(), NullLogger.Instance)
            .Describe(NullLocalizer.Instance).Select(Key).ToArray();

        // **数まで見る。** 「Inner がある」だけだと、見張りを外して
        // Inner.Back.Inner.Back… と潜り続けても通ってしまう。
        Assert.Equal(["Inner", "Inner.Name"], [.. keys.Order(StringComparer.Ordinal)]);
    }

    /// <summary>訳さない。ラベルのキーがそのまま出るだけで、ここで見たいものには関係ない。</summary>
    private sealed class NullLocalizer : ILocalizer
    {
        public static readonly NullLocalizer Instance = new();

        public string Language => "-";

        public string this[string key] => key;

        public string Format(string key, params object?[] arguments) => key;
    }

    [Fact]
    public void LabelDescriptionAndFoldingCanComeFromTheType()
    {
        var described = Bind2(new Labelled()).Describe(NullLocalizer.Instance);
        var box = described.First(i => Key(i) == "Plain");

        Assert.Equal("From the type", Get(box, "label"));
        Assert.Equal("Described by the type", Get(box, "description"));
        Assert.Equal(true, Get(box, "foldable"));
    }

    /// <summary>メンバーに書いたら、そちらが勝つ（同じ型を 2 か所に置いたときに、置いた場所ごとの名前を付けられないと困る）。</summary>
    [Fact]
    public void ALabelOnTheMemberBeatsOneOnTheType()
    {
        var described = Bind2(new Labelled()).Describe(NullLocalizer.Instance);
        var box = described.First(i => Key(i) == "Overridden");

        Assert.Equal("From the member", Get(box, "label"));
        Assert.Equal("Described by the member", Get(box, "description"));

        // 置き方は名前とは別の話なので、メンバーの名前で上書きされたりしない。
        Assert.Equal(true, Get(box, "foldable"));
    }

    [Fact]
    public void ALocalizationKeyOnTheTypeBeatsALiteralLabel()
    {
        var described = Bind2(new Labelled()).Describe(NullLocalizer.Instance);

        Assert.Equal("box.label", Get(described.First(i => Key(i) == "Translated"), "label"));
    }

    /// <summary>書かなければ畳めない（何も書いていない既存の入れ子は、今までどおり開いたまま並ぶ）。</summary>
    [Fact]
    public void FoldingIsOffUnlessItIsAskedFor()
    {
        var described = Bind2(new Labelled()).Describe(NullLocalizer.Instance);

        Assert.Equal(false, Get(described.First(i => Key(i) == "Advanced"), "foldable"));

        // 葉には端から付かない（型の属性を見に行くのは入れ物だけ）。
        Assert.Equal(false, Get(described.First(i => Key(i) == "Advanced.Timeout"), "foldable") ?? false);
    }

    /// <summary>モジュールへ知らせる名前は、入れ子だと繋がる。平らなままだと、モジュール側の <c>OnConfigChanged</c> が一致しないまま静かに動き続ける（入にしても何も起きない）。<c>nameof</c> は最後の名前しか見ないので、書く側も繋いで作る。</summary>
    [Fact]
    public void TheNamesReportedOnApplyAreJoinedWhenNested()
    {
        var config = new Config { Mode = Mode.Server };
        var binding = Bind(config);

        var changed = binding.ApplyNow(
            Json("""{ "Server.Auth.Enabled": true, "Server.Url": "https://example.test" }"""),
            NullLocalizer.Instance);

        Assert.Equal(
            ["Server.Auth.Enabled", "Server.Url"],
            [.. changed.Order(StringComparer.Ordinal)]);
    }

    private static ModuleConfigBinding Bind2(IModuleConfig config) => new(config, NullLogger.Instance);

    /// <summary>どこにも書かなければメンバー名が出る（画面に出したいのは、何という型かではなく、その設定が何と呼ばれているか）。</summary>
    [Fact]
    public void TheFallbackIsTheMemberNameNotTheTypeName()
    {
        var described = Bind2(new Labelled()).Describe(NullLocalizer.Instance);
        var box = described.First(i => Key(i) == "Unnamed");

        Assert.Equal("Unnamed", Get(box, "label"));
        Assert.Null(Get(box, "description"));
    }
    /// <summary>葉には型のラベルが漏れない。型の側を見に行くのは入れ物のときだけ。葉でも見に行くと、共有の型に名前を付けただけで、その型を使った項目が全部その名前になる（付けた本人からは見えない。別のモジュールの設定かもしれない）。</summary>
    [Fact]
    public void ATypeLabelDoesNotLeakOntoALeaf()
    {
        var described = Bind2(new Labelled()).Describe(NullLocalizer.Instance);

        Assert.Equal("Blob", Get(described.First(i => Key(i) == "Blob"), "label"));
    }
}
