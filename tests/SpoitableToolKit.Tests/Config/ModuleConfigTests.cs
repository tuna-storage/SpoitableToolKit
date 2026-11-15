using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Config.Attributes;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.Events.Config;
using SpoitableToolKit.Host.Config;
using SpoitableToolKit.Host.Config.Models;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Tests.Fakes;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Config;

public sealed class ModuleConfigTests
{
    private enum Backend
    {
        Avpro,
        Unity,
    }

    private sealed class Config : IModuleConfig
    {
        [SpoitableSettingsLabel("聞く")]
        public bool Ask = true;

        [SpoitableSettingsRange(3, 600)]
        public int Seconds = 15;

        [SpoitableSettingsKey("player")]
        [SpoitableSettingsFormerKey("backend")]
        public Backend Player = Backend.Avpro;

        public Dictionary<string, int> Counters = new() { ["a"] = 1 };

        public readonly string Fixed = "readonly";

        [SpoitableSettingsHidden]
        public string LastCheck = "";

        public string[] Names = [];

        public string Note = "";

        [SpoitableSettingsSecret]
        public string Token = "";

        /// <summary>候補は実行時にしか分からないものを出す口。ここでは決め打ちで返す。伏せる欄にも答える（答えても画面まで運ばれないことを見るため）。</summary>
        public IReadOnlyList<string>? SuggestValuesFor(string memberName) => memberName switch
        {
            nameof(Names) => ["alpha", "beta"],
            nameof(Token) => ["leaked"],
            _ => null,
        };
    }

    private static ModuleConfigBinding Bind(Config config) => new(config, NullLogger.Instance);

    private static JsonObject Json(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void AMemberThatCannotBeWrittenIsNotASetting()
    {
        var members = Bind(new Config()).Members.Select(m => m.Name).ToArray();

        Assert.DoesNotContain(nameof(Config.Fixed), members);
        Assert.Contains(nameof(Config.Ask), members);
    }

    [Fact]
    public void SavingThenReloadingKeepsTheValue()
    {
        var written = new Config { Ask = false, Seconds = 42, Player = Backend.Unity };
        written.Counters["b"] = 7;

        var read = new Config();
        Bind(read).Load(Bind(written).Save(), NullLogger.Instance);

        Assert.False(read.Ask);
        Assert.Equal(42, read.Seconds);
        Assert.Equal(Backend.Unity, read.Player);
        Assert.Equal(7, read.Counters["b"]);
    }

    /// <summary>enum は名前で保存する。数値だと、あとから値を足したときに保存済みの設定が別物になる。</summary>
    [Fact]
    public void EnumsAreStoredByName()
    {
        var saved = Bind(new Config { Player = Backend.Unity }).Save();

        Assert.Equal("Unity", saved["player"]!.GetValue<string>());
    }

    [Fact]
    public void TheStoredNameComesFromTheAttribute()
    {
        var saved = Bind(new Config()).Save();

        Assert.True(saved.ContainsKey("player"));
        Assert.False(saved.ContainsKey(nameof(Config.Player)));
    }

    [Fact]
    public void AFormerStoredNameCanStillBeRead()
    {
        var config = new Config();
        Bind(config).Load(Json("""{ "backend": "Unity" }"""), NullLogger.Instance);

        Assert.Equal(Backend.Unity, config.Player);
    }

    [Fact]
    public void TheFormerNameIsIgnoredWhenTheCurrentOneExists()
    {
        var config = new Config();
        Bind(config).Load(Json("""{ "player": "Avpro", "backend": "Unity" }"""), NullLogger.Instance);

        Assert.Equal(Backend.Avpro, config.Player);
    }

    /// <summary>設定から消した項目が保存に残っていても、ほかの値はそのまま読める。消した項目は次の保存で消える。</summary>
    [Fact]
    public void AnItemNoLongerInTheSettingsIsSkippedAndDroppedOnTheNextSave()
    {
        var config = new Config();
        var binding = Bind(config);
        binding.Load(Json("""{ "Seconds": 30, "WristShortcutDragMillimeters": 24, "Ask": false }"""), NullLogger.Instance);

        Assert.Equal(30, config.Seconds);
        Assert.False(config.Ask);
        Assert.False(binding.Save().ContainsKey("WristShortcutDragMillimeters"));
    }

    /// <summary>読むときだけ昔の名前を見る。書くのは常に今の名前なので、次の保存で移行が終わる。</summary>
    [Fact]
    public void ReadingByAFormerNameStillWritesTheCurrentOne()
    {
        var config = new Config();
        var binding = Bind(config);
        binding.Load(Json("""{ "backend": "Unity" }"""), NullLogger.Instance);

        var saved = binding.Save();

        Assert.Equal("Unity", saved["player"]!.GetValue<string>());
        Assert.False(saved.ContainsKey("backend"));
    }

    /// <summary>画面に専用のコントロールが無い型でも、保存は落とさない（落とすと、設定画面を開いただけで値が消える）。</summary>
    [Fact]
    public void ATypeTheScreenCannotShowIsStillStored()
    {
        var config = new Config();
        Bind(config).Load(Json("""{ "Counters": { "x": 9, "y": 3 } }"""), NullLogger.Instance);

        Assert.Equal(9, config.Counters["x"]);
        Assert.Equal(3, config.Counters["y"]);

        // 画面には JSON のテキスト欄として出る（消えはしない）。
        var described = Bind(config).Describe(new FakeLocalizer());
        Assert.Contains(described, item => Describe(item, "kind") == "json");
    }

    /// <summary>候補は設定画面の材料に混ざって出る。選択肢ではないので、値を絞る役目はない。</summary>
    [Fact]
    public void SuggestionsRideAlongWithTheScreenData()
    {
        var described = Bind(new Config()).Describe(new FakeLocalizer());

        var names = Assert.Single(described, item => Describe(item, "name") == nameof(Config.Names));
        Assert.Equal(["alpha", "beta"], Suggestions(names)!);

        // 答えなかった項目には候補が付かない（空配列ではなく null）。
        var ask = Assert.Single(described, item => Describe(item, "name") == nameof(Config.Ask));
        Assert.Null(Suggestions(ask));
    }

    /// <summary>伏せる欄も種別は文字列（別の種別にすると、値の形も保存も同じなのに扱いだけが二重になる）。画面が変えるのは打ち込む欄の見え方だけなので、印を 1 つ添える。</summary>
    [Fact]
    public void ASecretIsAStringWithAMarkOnIt()
    {
        var described = Bind(new Config()).Describe(new FakeLocalizer());

        var token = Assert.Single(described, item => Describe(item, "name") == nameof(Config.Token));
        Assert.Equal("string", Describe(token, "kind"));
        Assert.Equal("True", Describe(token, "secret"));

        // 書かなければ伏せない。 既にある文字列の欄は今までどおり出る。
        var note = Assert.Single(described, item => Describe(item, "name") == nameof(Config.Note));
        Assert.Equal("string", Describe(note, "kind"));
        Assert.Equal("False", Describe(note, "secret"));
    }

    /// <summary>伏せる欄には候補を運ばない（伏せた字の下に平文で並べたら、伏せた意味が消える）。モジュールが答えたかどうかは見ず、画面の側に同じ判断を置かない。</summary>
    [Fact]
    public void ASecretNeverCarriesSuggestions()
    {
        var described = Bind(new Config()).Describe(new FakeLocalizer());

        var token = Assert.Single(described, item => Describe(item, "name") == nameof(Config.Token));
        Assert.Null(Suggestions(token));

        // 伏せていない欄には今までどおり運ぶ。
        var names = Assert.Single(described, item => Describe(item, "name") == nameof(Config.Names));
        Assert.Equal(["alpha", "beta"], Suggestions(names)!);
    }

    /// <summary>伏せているのは画面だけ。保存は生のままで、設定ファイルを開けば読める。</summary>
    [Fact]
    public void ASecretIsStoredInThePlain()
    {
        var saved = Bind(new Config { Token = "s3cret" }).Save();

        Assert.Equal("s3cret", saved[nameof(Config.Token)]!.GetValue<string>());
    }

    private sealed class ThrowingSuggest : IModuleConfig
    {
        public string[] Names = [];

        public IReadOnlyList<string>? SuggestValuesFor(string memberName) =>
            throw new InvalidOperationException("壊れた候補");
    }

    /// <summary>候補はモジュールのコードを UI のスレッドで呼ぶ。ここで抜けると設定画面が丸ごと開かなくなるので、握り潰して「候補なし」にする。</summary>
    [Fact]
    public void AThrowingSuggestionStillLetsTheScreenBuild()
    {
        var described = new ModuleConfigBinding(new ThrowingSuggest(), NullLogger.Instance).Describe(new FakeLocalizer());

        Assert.Null(Suggestions(Assert.Single(described)));
    }

    /// <summary>同じ内容の配列を送り直しただけでは知らせない（参照比較のままだと、押すたびに設定の listener が走る）。</summary>
    [Fact]
    public void AnArrayWithTheSameContentsIsNotAChange()
    {
        var config = new Config { Names = ["alpha", "beta"] };

        var changed = Bind(config).ApplyNow(Json(@"{ ""Names"": [""alpha"", ""beta""] }"), new FakeLocalizer());

        Assert.Empty(changed);
    }

    [Fact]
    public void AnArrayWithDifferentContentsIsAChange()
    {
        var config = new Config { Names = ["alpha"] };

        var changed = Bind(config).ApplyNow(Json(@"{ ""Names"": [""alpha"", ""beta""] }"), new FakeLocalizer());

        Assert.Equal([nameof(Config.Names)], changed);
        Assert.Equal(["alpha", "beta"], config.Names);
    }

    [Fact]
    public void AHiddenItemIsStoredButNotShown()
    {
        var config = new Config { LastCheck = "2026-08-28" };
        var binding = Bind(config);

        Assert.DoesNotContain(binding.Describe(new FakeLocalizer()), item => Describe(item, "name") == nameof(Config.LastCheck));
        Assert.Equal("2026-08-28", binding.Save()[nameof(Config.LastCheck)]!.GetValue<string>());
    }

    /// <summary>設定ファイルは直接書き換えられるので、画面の入力制限は当てにできない。</summary>
    [Fact]
    public void AnOutOfRangeValueIsClampedOnLoad()
    {
        var config = new Config();
        Bind(config).Load(Json("""{ "Seconds": 9999 }"""), NullLogger.Instance);

        Assert.Equal(600, config.Seconds);
    }

    [Fact]
    public void ApplyingReturnsTheNamesThatChanged()
    {
        var changed = Bind(new Config()).ApplyNow(Json(@"{ ""Ask"": false }"), new FakeLocalizer());

        Assert.Equal([nameof(Config.Ask)], changed);
    }

    [Fact]
    public void AHiddenItemCannotBeChangedFromTheScreen()
    {
        Assert.Throws<ArgumentException>(() =>
            Bind(new Config()).ApplyNow(Json(@"{ ""LastCheck"": ""x"" }"), new FakeLocalizer()));
    }

    [Fact]
    public void ABrokenValueLeavesTheDefaultInPlace()
    {
        var config = new Config();
        Bind(config).Load(Json("""{ "Seconds": "数字じゃない" }"""), NullLogger.Instance);

        Assert.Equal(15, config.Seconds);
    }

    // 繋がっていないと、設定は「画面では変わるのに次の起動で戻る」ことになる。

    /// <summary>聞かれた・知らされたことを溜める listener（答えは <see cref="Answer"/> が決める。既定は何も断らない）。</summary>
    private sealed class Listener : IConfigEventListener<Config>
    {
        public List<string[]> Asked { get; } = [];

        public List<string[]> Told { get; } = [];

        /// <summary>聞かれたときに見えた値（写しと共有の設定の両方）。</summary>
        public List<(int ProposedSeconds, bool ProposedAsk, bool SharedAsk, bool SameInstance)> Seen { get; } = [];

        public Config? Shared { get; init; }

        public Func<ConfigChangingEventParams<Config>, EventResult> Answer { get; init; } = _ => EventResult.Continue;

        public EventResult OnConfigChanging(ConfigChangingEventParams<Config> e)
        {
            Asked.Add([.. e.Changes.Select(c => c.Name)]);
            if (Shared is { } shared)
                Seen.Add((e.Proposed.Seconds, e.Proposed.Ask, shared.Ask, ReferenceEquals(e.Proposed, shared)));
            return Answer(e);
        }

        public void OnConfigChanged(ConfigChangedEventParams<Config> e) => Told.Add([.. e.Changes.Select(c => c.Name)]);
    }

    /// <summary>本物の置き場。口はアプリの出来事の口の置き場から作るので、試験からも、モジュールが足すのと同じ口を通す。</summary>
    private static ModuleConfigStore NewStore(out EventHubs hubs, Func<ISpoitableModule, bool>? loaded = null)
    {
        hubs = new EventHubs(loaded ?? (_ => true), NullLoggerFactory.Instance);
        return new ModuleConfigStore(hubs, NullLoggerFactory.Instance);
    }

    private static ModuleConfigStore NewStore() => NewStore(out _);

    private static string TempDirectory() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "stk-config", Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public void StoredValuesAreInPlaceAsSoonAsTheModuleIsTakenIn()
    {
        var directory = TempDirectory();
        File.WriteAllText(Path.Combine(directory, "config.json"), @"{ ""Seconds"": 30, ""Ask"": false }");

        var config = new Config();
        NewStore().Add("p", "P", null, config, directory);

        // ModuleManager はこれを OnInit より前に呼ぶ。モジュールは OnInit で読んでよい。
        Assert.Equal(30, config.Seconds);
        Assert.False(config.Ask);
    }

    [Fact]
    public void ApplyingWritesToTheFile()
    {
        var directory = TempDirectory();
        var store = NewStore();
        store.Add("p", "P", null, new Config(), directory);

        store.Apply("p", Json(@"{ ""Seconds"": 30 }"), new FakeLocalizer());

        var saved = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "config.json")))!;
        Assert.Equal(30, saved[nameof(Config.Seconds)]!.GetValue<int>());
    }

    /// <summary>画面からでなく、ホスト自身のコードが書いた値も保存できる。</summary>
    [Fact]
    public void AValueChangedFromCodeCanAlsoBeSaved()
    {
        var directory = TempDirectory();
        var store = NewStore();
        var config = new Config();

        store.Add("p", "P", null, config, directory);

        // 共有インスタンスを直接書き換えるのが「コードが書く」側の形。
        config.LastCheck = "2026-08-28";
        config.Counters["b"] = 7;
        store.Save("p");

        var saved = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "config.json")))!;
        Assert.Equal("2026-08-28", saved[nameof(Config.LastCheck)]!.GetValue<string>());
        Assert.Equal(7, saved[nameof(Config.Counters)]!["b"]!.GetValue<int>());
    }

    [Fact]
    public void AnUnknownModuleCannotBeSaved()
    {
        var store = NewStore();

        // 畳んだあとに書きに来たら気付けるように。黙って何もしないと、
        // 保存されていないことに誰も気付かない。
        Assert.Throws<ArgumentException>(() => store.Save("p"));
    }

    /// <summary>設定の口は設定ごと。ほかのモジュールの設定の変更は届かない（listener はモジュール本体と別のクラスでもよい）。</summary>
    [Fact]
    public void ChangesReachOnlyTheListenersOfThatConfig()
    {
        var store = NewStore();
        var mine = new Listener();
        var other = new Listener();

        store.Add("mine", "Mine", null, new Config(), TempDirectory()).AddListener<IConfigEventListener<Config>>(new FakeModule("mine"), mine);
        store.Add("other", "Other", null, new Config(), TempDirectory()).AddListener<IConfigEventListener<Config>>(new FakeModule("other"), other);

        store.Apply("mine", Json(@"{ ""Ask"": false }"), new FakeLocalizer());

        Assert.Equal([nameof(Config.Ask)], Assert.Single(mine.Asked));
        Assert.Equal([nameof(Config.Ask)], Assert.Single(mine.Told));
        Assert.Empty(other.Asked);
        Assert.Empty(other.Told);
    }

    [Fact]
    public void AddingTheSameListenerTwiceStillCallsOnce()
    {
        var store = NewStore();
        var listener = new Listener();
        var owner = new FakeModule("p");
        var config = store.Add("p", "P", null, new Config(), TempDirectory());

        config.AddListener<IConfigEventListener<Config>>(owner, listener);
        config.AddListener<IConfigEventListener<Config>>(owner, listener);
        store.Apply("p", Json(@"{ ""Seconds"": 30 }"), new FakeLocalizer());

        Assert.Single(listener.Asked);
        Assert.Single(listener.Told);
    }

    /// <summary>違う設定の型の listener は、足すときに断る（型を書いて足すので、取り違えたら黙って届かない、にしない）。</summary>
    private sealed class OtherConfig : IModuleConfig
    {
        public int Value = 1;
    }

    private sealed class OtherListener : IConfigEventListener<OtherConfig>;

    [Fact]
    public void AListenerForAnotherSettingsTypeIsRefusedWhenAdded()
    {
        var config = NewStore().Add("p", "P", null, new Config(), TempDirectory());

        var refused = Assert.ThrowsAny<Exception>(() => config.AddListener<IConfigEventListener<OtherConfig>>(new FakeModule("p"), new OtherListener()));

        Assert.Contains("IConfigEventListener<Config>", refused.Message);
        Assert.Contains("not IConfigEventListener<OtherConfig>", refused.Message);
    }

    /// <summary>畳まれたモジュールの listener は外れる（失敗したモジュールの設定は画面に残るので、値は書けるが、誰にも聞かない・知らせない）。設定を外すと口も閉じる。</summary>
    [Fact]
    public void AFailedOrStoppedModuleIsNeitherAskedNorTold()
    {
        var store = NewStore(out var hubs);
        var listener = new Listener();
        var owner = new FakeModule("p");
        var config = new Config();
        var hubsBefore = hubs.HubCount;

        store.Add("p", "P", null, config, TempDirectory()).AddListener<IConfigEventListener<Config>>(owner, listener);
        Assert.Equal(hubsBefore + 1, hubs.HubCount);

        // 失敗したとき（ModuleManager.Fail → HostEvents.Retire）と同じ。
        hubs.Forget(owner);
        store.Apply("p", Json(@"{ ""Seconds"": 30 }"), new FakeLocalizer());

        Assert.Equal(30, config.Seconds);
        Assert.Empty(listener.Asked);
        Assert.Equal(0, store.ListenerCount("p"));

        // 終わったとき（ModuleManager.Shutdown → configs.Remove）。
        store.Remove("p");

        Assert.Throws<ArgumentException>(() => store.Apply("p", Json(@"{ ""Ask"": false }"), new FakeLocalizer()));
        Assert.Equal(hubsBefore, hubs.HubCount);
        Assert.Null(store.ListenerCount("p"));
    }

    /// <summary>
    /// 聞いている間、共有の設定は前の値のまま（裏の thread が断られる値を読まないように）。新しい値は写し（<c>e.Proposed</c>）で見せる —
    /// 写しには変わっていない値も今の値で入っている（既定値ではない。組み合わせを確かめるのに要る）。
    /// </summary>
    [Fact]
    public void TheListenerSeesTheNewValuesInACopyWhileTheSharedConfigKeepsTheOld()
    {
        var store = NewStore();
        var config = new Config { Seconds = 30 };
        var listener = new Listener { Shared = config };
        store.Add("p", "P", null, config, TempDirectory()).AddListener<IConfigEventListener<Config>>(new FakeModule("p"), listener);

        store.Apply("p", Json(@"{ ""Ask"": false }"), new FakeLocalizer());

        // 写し: 変えた Ask は新しい値、変えていない Seconds は今の値（既定の 15 ではない）。共有の設定の Ask はまだ前の値。
        var seen = Assert.Single(listener.Seen);
        Assert.Equal((30, false, true, false), seen);
        Assert.False(config.Ask);
    }

    /// <summary>
    /// 値ごとに断れる: 断った値は前の値のまま（書かない・保存しない）、ほかの値は入る。
    /// 画面へは断った欄の key（保存名の道。C# の名前ではない）と理由、入った欄の key を返す。知らせるのは入った値だけ。
    /// </summary>
    [Fact]
    public void ARejectedValueStaysWhileTheOthersGoIn()
    {
        var store = NewStore();
        var config = new Config();
        var directory = TempDirectory();
        var listener = new Listener
        {
            Answer = e =>
            {
                e.Reject(c => c.Player, "その再生方式は使えません");
                return EventResult.Continue;
            },
        };
        store.Add("p", "P", null, config, directory).AddListener<IConfigEventListener<Config>>(new FakeModule("p"), listener);

        var rejected = Assert.Throws<ConfigRejectedException>(() =>
            store.Apply("p", Json(@"{ ""player"": ""Unity"", ""Seconds"": 30 }"), new FakeLocalizer()));

        Assert.Equal(new ConfigNote("player", "その再生方式は使えません"), Assert.Single(rejected.Notes));
        Assert.Equal([nameof(Config.Seconds)], rejected.Applied);
        Assert.Equal(Backend.Avpro, config.Player);
        Assert.Equal(30, config.Seconds);
        Assert.Equal([nameof(Config.Seconds)], Assert.Single(listener.Told));

        var saved = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "config.json")))!;
        Assert.Equal(30, saved[nameof(Config.Seconds)]!.GetValue<int>());
        Assert.Equal(nameof(Backend.Avpro), saved["player"]!.GetValue<string>());
    }

    /// <summary>
    /// 全部を断る（<c>EventResult.Stop</c>）と、何も書かず、保存もせず、誰にも知らせない。宛先の欄は無い（まとまりへの答え）。
    /// 前の listener が値ごとに断っていても、Stop の理由で返す。
    /// </summary>
    [Fact]
    public void StoppingWritesNothingAndTellsNoOne()
    {
        var store = NewStore();
        var config = new Config();
        var directory = TempDirectory();
        var first = new Listener
        {
            Answer = e =>
            {
                e.Reject(c => c.Seconds, "秒が駄目");
                return EventResult.Continue;
            },
        };
        var stopping = new Listener { Answer = _ => EventResult.Stop("その組み合わせでは動きません") };
        var source = store.Add("p", "P", null, config, directory);
        source.AddListener<IConfigEventListener<Config>>(new FakeModule("p"), first);
        source.AddListener<IConfigEventListener<Config>>(new FakeModule("p"), stopping);

        var rejected = Assert.Throws<ConfigRejectedException>(() =>
            store.Apply("p", Json(@"{ ""Seconds"": 30, ""Ask"": false }"), new FakeLocalizer()));

        Assert.Equal(new ConfigNote(null, "その組み合わせでは動きません"), Assert.Single(rejected.Notes));
        Assert.Empty(rejected.Applied);
        Assert.Equal(15, config.Seconds);
        Assert.True(config.Ask);
        Assert.Empty(first.Told);
        Assert.Empty(stopping.Told);
        Assert.False(File.Exists(Path.Combine(directory, "config.json")));
    }

    /// <summary>
    /// 入れ物（section）を書いて断ると、その中の変わった値を全部断る（組み合わせで駄目なとき、関わる値をまとめて断る形）。
    /// 変わっていない値を断っても何も起きない。
    /// </summary>
    private sealed class Boxed : IModuleConfig
    {
        public int Top = 1;

        /// <summary>名前が <see cref="Top"/> で始まるだけの別の値（<c>Top</c> を断っても巻き込まない）。</summary>
        public int TopLimit = 5;

        public readonly BandSettings Band = new();
    }

    private sealed class BandSettings : ISpoitableSettings
    {
        public int Low = 10;

        public int High = 20;
    }

    private sealed class BandGuard : IConfigEventListener<Boxed>
    {
        public List<string[]> Told { get; } = [];

        public EventResult OnConfigChanging(ConfigChangingEventParams<Boxed> e)
        {
            if (e.Proposed.Band.Low >= e.Proposed.Band.High) e.Reject(c => c.Band, "下が上以上");
            e.Reject(c => c.Top, "変わっていないので何も起きない");
            return EventResult.Continue;
        }

        public void OnConfigChanged(ConfigChangedEventParams<Boxed> e) => Told.Add([.. e.Changes.Select(c => c.Name)]);
    }

    [Fact]
    public void RejectingASectionRejectsEveryChangedValueInIt()
    {
        var store = NewStore();
        var config = new Boxed();
        var guard = new BandGuard();
        store.Add("p", "P", null, config, TempDirectory()).AddListener<IConfigEventListener<Boxed>>(new FakeModule("p"), guard);

        // 下だけ変えて上を超えた → 下が断られる（上は変わっていないので断る物が無い）。
        var one = Assert.Throws<ConfigRejectedException>(() => store.Apply("p", Json(@"{ ""Band.Low"": 25 }"), new FakeLocalizer()));
        Assert.Equal([new ConfigNote("Band.Low", "下が上以上")], one.Notes);
        Assert.Equal(10, config.Band.Low);

        // 両方変えて崩れた → 両方断られる。
        var both = Assert.Throws<ConfigRejectedException>(() => store.Apply("p", Json(@"{ ""Band.Low"": 30, ""Band.High"": 5 }"), new FakeLocalizer()));
        Assert.Equal(["Band.Low", "Band.High"], both.Notes.Select(n => n.Key));
        Assert.Equal((10, 20), (config.Band.Low, config.Band.High));

        // 崩れなければ入る。Top を断っても、名前が Top で始まるだけの TopLimit は巻き込まない。
        store.Apply("p", Json(@"{ ""Band.High"": 40, ""TopLimit"": 7 }"), new FakeLocalizer());
        Assert.Equal((40, 7), (config.Band.High, config.TopLimit));
        Assert.Equal(["Band.High", "TopLimit"], Assert.Single(guard.Told));
    }

    /// <summary>
    /// 設定の項目でない式は、分かる文で断る（黙って false・何もしない、にしない）。計算・別の値・設定画面に出ない要素はどれも駄目。
    /// 変わった値の確かめ（<c>change.Is</c>）は、値も一緒に受け取れる。
    /// </summary>
    [Fact]
    public void AnExpressionThatIsNotASettingIsRefusedWithAClearMessage()
    {
        var store = NewStore();
        Exception? calculation = null, fixedMember = null;
        var secondsSeen = 0;
        var listener = new Listener
        {
            Answer = e =>
            {
                calculation = Record.Exception(() => e.Reject(c => c.Seconds + 1, "x"));
                fixedMember = Record.Exception(() => e.Changed(c => c.Fixed));
                foreach (var change in e.Changes)
                    if (change.Is(c => c.Seconds, out var seconds)) secondsSeen = seconds;
                return EventResult.Continue;
            },
        };
        store.Add("p", "P", null, new Config(), TempDirectory()).AddListener<IConfigEventListener<Config>>(new FakeModule("p"), listener);

        store.Apply("p", Json(@"{ ""Seconds"": 45 }"), new FakeLocalizer());

        Assert.Contains("does not name a setting of Config", Assert.IsType<ArgumentException>(calculation).Message);
        Assert.Contains("'Fixed' is not a setting of Config", Assert.IsType<ArgumentException>(fixedMember).Message);
        Assert.Equal(45, secondsSeen);
    }

    /// <summary>
    /// 落ちた listener は断らなかったものとして扱う（落ちた listener のせいで設定を変えられなくなる方が困る）。
    /// ただし落ちる前に断った値は断ったまま（はっきり言ったことは残す）。
    /// </summary>
    [Fact]
    public void AThrowingListenerCountsAsAcceptingButKeepsWhatItRejectedFirst()
    {
        var store = NewStore();
        var config = new Config();
        var listener = new Listener
        {
            Answer = e =>
            {
                e.Reject(c => c.Ask, "聞くのは切れません");
                throw new InvalidOperationException("壊れた listener");
            },
        };
        store.Add("p", "P", null, config, TempDirectory()).AddListener<IConfigEventListener<Config>>(new FakeModule("p"), listener);

        var rejected = Assert.Throws<ConfigRejectedException>(() =>
            store.Apply("p", Json(@"{ ""Seconds"": 30, ""Ask"": false }"), new FakeLocalizer()));

        Assert.Equal(30, config.Seconds);
        Assert.True(config.Ask);
        Assert.Equal([nameof(Config.Ask)], rejected.Notes.Select(n => n.Key));
    }

    /// <summary><c>Handled</c> は <c>Continue</c> と同じ（値は入る）。そのあとの listener には聞かない（口の約束どおり）。</summary>
    [Fact]
    public void HandledLetsTheValuesInAndAsksNoOneElse()
    {
        var store = NewStore();
        var config = new Config();
        var handling = new Listener { Answer = _ => EventResult.Handled };
        var after = new Listener();
        var source = store.Add("p", "P", null, config, TempDirectory());
        source.AddListener<IConfigEventListener<Config>>(new FakeModule("p"), handling);
        source.AddListener<IConfigEventListener<Config>>(new FakeModule("p"), after);

        store.Apply("p", Json(@"{ ""Seconds"": 30 }"), new FakeLocalizer());

        Assert.Equal(30, config.Seconds);
        Assert.Empty(after.Asked);
        Assert.Single(after.Told);
    }

    /// <summary>「適用」は 1 回の操作なので、聞くのも知らせるのも 1 回。1 つずつだと繋ぎ直しが何度も走る。</summary>
    [Fact]
    public void ChangingSeveralAtOnceAsksAndTellsOnce()
    {
        var store = NewStore();
        var listener = new Listener();
        store.Add("p", "P", null, new Config(), TempDirectory()).AddListener<IConfigEventListener<Config>>(new FakeModule("p"), listener);

        store.Apply("p", Json(@"{ ""Seconds"": 30, ""Ask"": false }"), new FakeLocalizer());

        Assert.Equal([nameof(Config.Ask), nameof(Config.Seconds)], Assert.Single(listener.Asked).Order());
        Assert.Equal([nameof(Config.Ask), nameof(Config.Seconds)], Assert.Single(listener.Told).Order());
    }

    /// <summary>値が変わっていないなら聞かないし知らせない。押しただけで作り直しが走らないように。</summary>
    [Fact]
    public void NothingIsAskedOrToldWhenNothingActuallyChanged()
    {
        var store = NewStore();
        var listener = new Listener();
        store.Add("p", "P", null, new Config(), TempDirectory()).AddListener<IConfigEventListener<Config>>(new FakeModule("p"), listener);

        store.Apply("p", Json(@"{ ""Seconds"": 15 }"), new FakeLocalizer());

        Assert.Empty(listener.Asked);
        Assert.Empty(listener.Told);
    }

    /// <summary>
    /// 設定のクラスは引数なしで作れること（聞いている間の写しを作るため）。作れなければ、迎え入れるときに分かる文で断る。
    /// </summary>
    private sealed class NeedsAnArgument(int seconds) : IModuleConfig
    {
        public int Seconds = seconds;
    }

    [Fact]
    public void ASettingsClassThatCannotBeMadeWithoutArgumentsIsRefused()
    {
        var refused = Assert.Throws<InvalidOperationException>(() =>
            NewStore().Add("p", "P", null, new NeedsAnArgument(3), TempDirectory()));

        Assert.Contains("has no public constructor without parameters", refused.Message);
        Assert.Contains("new NeedsAnArgument()", refused.Message);
    }

    /// <summary>
    /// 本物の 3 つの設定は、何も変えずに写すと今の値と同じになる（設定画面に出る値が全部写る。引数なしで作れることの見張りも兼ねる）。
    /// 写しは別のインスタンスで、List などを共有しない。
    /// </summary>
    [Fact]
    public void ACopyOfEachRealSettingsWithNothingChangedEqualsTheLiveValues()
    {
        IModuleConfig[] configs =
        [
            new HostConfig { AppLogKeepDays = 9, HiddenSidebarStatus = ["hrs.bpm"] },
            new SpoitableToolKit.Modules.Hrs.Config.HrsConfig { Device = "Polar H10 [A1:B2:C3:D4:E5:F6]", Zones = { LightBpm = 90 } },
            new SpoitableToolKit.Modules.VideoUrl.Config.VideoUrlConfig(),
        ];

        foreach (var config in configs)
        {
            var binding = new ModuleConfigBinding(config, NullLogger.Instance);
            var copy = binding.Propose([]);

            Assert.NotSame(config, copy);
            Assert.Equal(binding.Save().ToJsonString(), new ModuleConfigBinding(copy, NullLogger.Instance).Save().ToJsonString());
        }
    }

    [Fact]
    public void OneUnreadableValueChangesNothing()
    {
        var config = new Config();
        var binding = Bind(config);

        Assert.Throws<ConfigValueException>(() =>
            binding.ApplyNow(Json(@"{ ""Ask"": false, ""Seconds"": ""数字じゃない"" }"), new FakeLocalizer()));

        Assert.True(config.Ask);
        Assert.Equal(15, config.Seconds);
    }

    /// <summary>範囲外は丸めずに弾く（丸めると、30 のつもりの 300 も 3000 も同じ 30 になって、直った結果が正しいか判断できない）。モジュールが Reject したときと画面の動きも揃う。</summary>
    [Fact]
    public void AnOutOfRangeValueIsRejectedOnApply()
    {
        var config = new Config();

        var failed = Assert.Throws<ConfigValueException>(() =>
            Bind(config).ApplyNow(Json(@"{ ""Seconds"": 9999 }"), new FakeLocalizer()));

        Assert.Equal(nameof(Config.Seconds), failed.Key);
        Assert.Contains("host.config.outOfRange", failed.Message);

        Assert.Equal(15, config.Seconds);
    }

    /// <summary>
    /// 範囲外は読めない値と同じ扱いなので、同じまとまりの他の項目も変わらない。
    /// 利用者から見て「適用」は 1 回の操作なので、結果も全部か何も無しかにする。
    /// </summary>
    [Fact]
    public void AnOutOfRangeValueHoldsBackItsWholeGroup()
    {
        var config = new Config();

        Assert.Throws<ConfigValueException>(() =>
            Bind(config).ApplyNow(Json(@"{ ""Ask"": false, ""Seconds"": 9999 }"), new FakeLocalizer()));

        Assert.True(config.Ask);
        Assert.Equal(15, config.Seconds);
    }

    [Fact]
    public void AValueInsideTheRangeIsAccepted()
    {
        var config = new Config();
        Bind(config).ApplyNow(Json(@"{ ""Seconds"": 20 }"), new FakeLocalizer());

        Assert.Equal(20, config.Seconds);
    }

    /// <summary>どの項目が駄目だったかを持って返す。画面でその項目を赤枠にするのに要る。</summary>
    [Fact]
    public void AnUnreadableValueSaysWhichItemItWas()
    {
        var failed = Assert.Throws<ConfigValueException>(() =>
            Bind(new Config()).ApplyNow(Json(@"{ ""Seconds"": ""数字じゃない"" }"), new FakeLocalizer()));

        Assert.Equal(nameof(Config.Seconds), failed.Key);
    }

    private sealed class Inner : ISpoitableSettings
    {
        /// <summary>入れ子の中。束ねは段の一番上の並びの話なので、付けても出ない。</summary>
        [SpoitableSettingsGroup("grp.inner")]
        [SpoitableSettingsRestart]
        public int Depth = 1;
    }

    private sealed class Grouped : IModuleConfig
    {
        public int Loose = 1;

        [SpoitableSettingsGroup("grp.display")]
        public bool Show = true;

        [SpoitableSettingsGroup("grp.display")]
        [SpoitableSettingsRestart]
        public int Port = 8080;

        [SpoitableSettingsGroup("grp.notify")]
        public string Prefix { get; set; } = "";

        [SpoitableSettingsRestart]
        public bool Warm = false;

        [SpoitableSettingsGroup("grp.nested")]
        public Inner Nested { get; } = new();

        [SpoitableSettingsSwitches]
        public string[] Hidden = [];

        public string[] Typed = [];
    }

    private static object Item(object[] described, string name) =>
        Assert.Single(described, item => Describe(item, "name") == name);

    /// <summary>設定画面は、この見出しの文字で項目を囲みごとに分ける。キーが翻訳を通って渡る。</summary>
    [Fact]
    public void AGroupedMemberCarriesItsLocalizedGroupHeading()
    {
        var described = new ModuleConfigBinding(new Grouped(), NullLogger.Instance).Describe(new FakeLocalizer());

        Assert.Equal("grp.display", Describe(Item(described, nameof(Grouped.Show)), "group"));
        Assert.Equal("grp.display", Describe(Item(described, nameof(Grouped.Port)), "group"));
        Assert.Equal("grp.notify", Describe(Item(described, nameof(Grouped.Prefix)), "group"));
    }

    /// <summary>印の無い項目は、どの囲みにも入らない。</summary>
    [Fact]
    public void AMemberWithoutTheGroupMarkHasNoGroup()
    {
        var described = new ModuleConfigBinding(new Grouped(), NullLogger.Instance).Describe(new FakeLocalizer());

        Assert.Null(Describe(Item(described, nameof(Grouped.Loose)), "group"));
        Assert.Null(Describe(Item(described, nameof(Grouped.Warm)), "group"));
    }

    /// <summary>入れ物そのものは束ねに入れるが、中の項目には付けない（親と同じ囲みに入る）。</summary>
    [Fact]
    public void ASectionCanJoinAGroupButItsContentsDoNot()
    {
        var described = new ModuleConfigBinding(new Grouped(), NullLogger.Instance).Describe(new FakeLocalizer());

        Assert.Equal("grp.nested", Describe(Item(described, nameof(Grouped.Nested)), "group"));
        Assert.Null(Describe(Item(described, "Nested.Depth"), "group"));

        // 翻訳する前のキーも同じ（本体の設定は、このキーでページを分ける）。
        Assert.Equal("grp.nested", Describe(Item(described, nameof(Grouped.Nested)), "groupKey"));
        Assert.Null(Describe(Item(described, "Nested.Depth"), "groupKey"));
    }

    /// <summary>「次の起動から」の札は、印を付けた項目にだけ付く。付け忘れると適用しても効かないのが不具合に見える。</summary>
    [Fact]
    public void OnlyMembersMarkedRestartAskForARestart()
    {
        var described = new ModuleConfigBinding(new Grouped(), NullLogger.Instance).Describe(new FakeLocalizer());

        Assert.Equal("True", Describe(Item(described, nameof(Grouped.Port)), "restart"));
        Assert.Equal("True", Describe(Item(described, nameof(Grouped.Warm)), "restart"));
        Assert.Equal("False", Describe(Item(described, nameof(Grouped.Show)), "restart"));
        Assert.Equal("False", Describe(Item(described, nameof(Grouped.Loose)), "restart"));
    }

    /// <summary>
    /// 候補ごとのスイッチで見せる一覧は、印を付けた一覧にだけ <c>switches</c> が立つ。
    /// 印の無い一覧はタグの欄のまま（読み込まないモジュールのように、打って足す一覧もある）。
    /// </summary>
    [Fact]
    public void OnlyListsMarkedAsSwitchesAreShownAsSwitches()
    {
        var described = new ModuleConfigBinding(new Grouped(), NullLogger.Instance).Describe(new FakeLocalizer());

        Assert.Equal("True", Describe(Item(described, nameof(Grouped.Hidden)), "switches"));
        Assert.Equal("False", Describe(Item(described, nameof(Grouped.Typed)), "switches"));
    }

    /// <summary>手首のショートカットだけが専用の編集（「並べ替える…」のボタンとモーダル）で出る。ほかの一覧は今までどおりの欄。保存の形は文字列の一覧のまま。</summary>
    [Fact]
    public void OnlyTheWristShortcutsUseTheirOwnEditor()
    {
        var described = new ModuleConfigBinding(new HostConfig(), NullLogger.Instance).Describe(new FakeLocalizer());

        Assert.Equal("wristShortcuts", Describe(Item(described, nameof(HostConfig.WristShortcuts)), "editor"));
        Assert.Equal("list", Describe(Item(described, nameof(HostConfig.WristShortcuts)), "kind"));
        Assert.Null(Describe(Item(described, nameof(HostConfig.HiddenWristNotes)), "editor"));
    }

    /// <summary>入れ子の中の項目の札は、入れ物ではなくその項目自身の印で決まる。</summary>
    [Fact]
    public void ARestartMarkInsideASectionStillShows()
    {
        var described = new ModuleConfigBinding(new Grouped(), NullLogger.Instance).Describe(new FakeLocalizer());

        Assert.Equal("True", Describe(Item(described, "Nested.Depth"), "restart"));
    }

    /// <summary>束ね・札は見せ方だけ。保存の形（キー）は変えない。</summary>
    [Fact]
    public void GroupAndRestartMarksDoNotChangeTheSavedShape()
    {
        var saved = new ModuleConfigBinding(new Grouped(), NullLogger.Instance).Save();

        Assert.True(saved.ContainsKey(nameof(Grouped.Port)));
        Assert.True(saved.ContainsKey(nameof(Grouped.Show)));
        Assert.False(saved.ContainsKey("grp.display"));
    }

    /// <summary>Describe が返す匿名型を、テストから名前で引くための小道具。</summary>
    /// <summary>候補は string[] のまま乗るので、文字列化せずに取り出す。</summary>
    private static string[]? Suggestions(object item) =>
        (string[]?)item.GetType().GetProperty("suggestions")?.GetValue(item);

    private static string? Describe(object item, string property) =>
        item.GetType().GetProperty(property)?.GetValue(item)?.ToString();
}
