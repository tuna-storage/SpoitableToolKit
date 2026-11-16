using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Config.Attributes;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Host.Config;
using SpoitableToolKit.Host.Config.Interfaces;
using SpoitableToolKit.Host.Config.Models;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Ui.Push;
using SpoitableToolKit.Tests.Fakes;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Config;

/// <summary>
/// 値ではない設定の行（<see cref="SpoitableSettingsAction"/>）。保存・読み込み・適用・写しのどれにも乗らず、画面には宣言した場所に出て、
/// 状態はいつも本物（モジュールが結んだもの）から読む。ボタンは押した瞬間に裏で走り、走っている間の押しは捨てる。
/// </summary>
public sealed class SettingsActionTests
{
    private sealed class Box : ISpoitableSettings
    {
        [SpoitableSettingsKey("port")]
        public int Port = 3476;

        [SpoitableSettingsLabel("ファイアウォール")]
        [SpoitableSettingsDescription("通れるか")]
        public readonly SpoitableSettingsAction Gate = new(dependsOn: [nameof(Port), nameof(Later)]);

        /// <summary>行より後ろに宣言した設定も前提に指せる。</summary>
        public bool Later = false;
    }

    private sealed class Switchable : ITogglableSettings
    {
        public bool Enabled { get; set; }

        public readonly SpoitableSettingsAction Gate = new(dependsOn: [nameof(Enabled)]);
    }

    private sealed class Config : IModuleConfig
    {
        public int Seconds = 15;

        /// <summary>readonly でない形。それでも値の側へ落ちない（JSON の欄にならない・保存されない・読み込みで上書きされない）。</summary>
        public SpoitableSettingsAction Plain = new(dependsOn: []);

        [SpoitableSettingsGroup("grp.net")]
        public readonly Box Net = new();

        public readonly Switchable Hds = new();
    }

    /// <summary>前提に、無い名前・入れ物・自分自身・別のクラスの設定（入れ物の中の Later）を書いた形。指せるのは同じクラスの設定だけ。</summary>
    private sealed class Broken : IModuleConfig
    {
        public int Port = 1;

        public readonly Box Inner = new();

        public readonly SpoitableSettingsAction Wrong = new(dependsOn: ["Missing", nameof(Inner), nameof(Wrong), nameof(Box.Later), nameof(Port)]);
    }

    private static readonly FakeLocalizer Text = new();

    private static SpoitableSettingsActionState State(string status = "s") =>
        new() { Tone = SpoitableSettingsTone.Warn, Status = status, Detail = "d", Button = "b", ButtonNote = "n", Copy = "c" };

    private static JsonObject Json(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static object Item(object[] described, string key) =>
        Assert.Single(described, item => Field(item, "key") as string == key);

    private static object? Field(object item, string name) => item.GetType().GetProperty(name)?.GetValue(item);

    private static JsonElement StateOf(object item) => JsonDocument.Parse((string)Field(item, "state")!).RootElement;

    private static string Temp() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "stk-action", Guid.NewGuid().ToString("N"))).FullName;

    private static ModuleConfigStore NewStore(Logs? logs = null) =>
        new(new EventHubs(_ => true, NullLoggerFactory.Instance), (Microsoft.Extensions.Logging.ILoggerFactory?)logs ?? NullLoggerFactory.Instance);

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(30);
    }

    /// <summary>「変わった」を数えるだけの係。</summary>
    private sealed class Calls : IConfigActionSink
    {
        public List<(string Module, string Key)> Seen { get; } = [];

        public int Count
        {
            get { lock (Seen) return Seen.Count; }
        }

        public void Changed(string moduleId, string key)
        {
            lock (Seen) Seen.Add((moduleId, key));
        }
    }

    /// <summary>本物の係（<see cref="ConfigActionPusher"/>）と同じく、言われたら読み直して姿を溜める。</summary>
    private sealed class Views(ModuleConfigStore store) : IConfigActionSink
    {
        public List<ConfigActionView> Seen { get; } = [];

        public ConfigActionView[] All()
        {
            lock (Seen) return [.. Seen];
        }

        public void Changed(string moduleId, string key)
        {
            if (store.DescribeAction(moduleId, key, Text) is { } view)
                lock (Seen) Seen.Add(view);
        }
    }

    private sealed class Words(string language, Dictionary<string, string> words) : ILocalizer
    {
        public string Language => language;

        public string this[string key] => words.TryGetValue(key, out var word) ? word : key;

        public string Format(string key, params object?[] arguments) => this[key];
    }

    [Fact]
    public void AnActionIsARowAndNotASetting()
    {
        var logs = new Logs();
        var binding = new ModuleConfigBinding(new Config(), logs);

        // 設定の一覧（保存・読み込み・適用・listener の名前）には入らない。readonly のものも「書けない」と言われない。
        Assert.DoesNotContain(binding.Members, m => m.IsAction);
        var rows = binding.Actions.Select(a => a.Path).ToArray();
        Assert.Equal(["Plain", "Net.Gate", "Hds.Gate"], rows);
        Assert.DoesNotContain(logs.Warnings, w => w.Contains("not writable"));
        Assert.DoesNotContain("Net.Gate", ConfigTesting.SettingsOf(new Config()));

        var described = binding.Describe(Text);
        Assert.Equal("action", Field(Item(described, "Plain"), "kind"));
        Assert.Equal("action", Field(Item(described, "Net.Gate"), "kind"));
    }

    /// <summary>宣言した場所に出る（入れ物の中なら、すぐ前の設定の直後）。</summary>
    [Fact]
    public void TheRowSitsWhereItIsDeclared()
    {
        var keys = new ModuleConfigBinding(new Config(), NullLogger.Instance).Describe(Text)
            .Select(item => (string)Field(item, "key")!)
            .ToArray();

        Assert.Equal(["Seconds", "Plain", "Net", "Net.port", "Net.Gate", "Net.Later", "Hds", "Hds.Gate"], keys);
    }

    /// <summary>
    /// config.json に書かれず、すでにある config.json の読み込みも乱さない（手で書いた・前の build が書いた同じ名前の値があっても、
    /// 結んだ本物は上書きされない）。
    /// </summary>
    [Fact]
    public void TheRowNeverReachesConfigJsonAndDoesNotDisturbLoadingIt()
    {
        var directory = Temp();
        var file = Path.Combine(directory, "config.json");
        File.WriteAllText(file, """{ "Seconds": 30, "Plain": { "DependsOn": ["x"] }, "Net": { "port": 9000, "Gate": null } }""");

        var config = new Config();
        var plain = config.Plain;
        var gate = config.Net.Gate;
        gate.Bind(_ => State(), () => Task.CompletedTask);

        var store = NewStore();
        store.Add("m", "M", null, config, directory);

        Assert.Equal(30, config.Seconds);
        Assert.Equal(9000, config.Net.Port);
        Assert.Same(plain, config.Plain);
        Assert.Same(gate, config.Net.Gate);
        Assert.True(gate.IsBound);

        store.Apply("m", Json("""{ "Seconds": 31 }"""), Text);

        var written = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        Assert.Equal(31, (int)written["Seconds"]!);
        Assert.False(written.ContainsKey("Plain"));
        Assert.False(written["Net"]!.AsObject().ContainsKey("Gate"));
        Assert.False(written["Hds"]!.AsObject().ContainsKey("Gate"));

        // 画面から行の宛先で値を送られても、設定としては扱わない。
        Assert.ThrowsAny<ArgumentException>(() => store.Apply("m", Json("""{ "Net.Gate": 1 }"""), Text));
    }

    /// <summary>下見の写しも listener に見せる写しも <c>new</c> で作り直したもので、結ばれていない。画面はどちらでも本物の行を読む。</summary>
    [Fact]
    public void ThePreviewAndTheProposedCopyReadTheRealRow()
    {
        var config = new Config();
        config.Net.Gate.Bind(_ => State("本物"), () => Task.CompletedTask);
        var binding = new ModuleConfigBinding(config, NullLogger.Instance);

        var preview = binding.DescribeWith(Json("""{ "Seconds": 99 }"""), Text);
        Assert.Equal("本物", StateOf(Item(preview, "Net.Gate")).GetProperty("status").GetString());

        var proposed = (Config)binding.Propose(binding.Changes(Json("""{ "Seconds": 99 }"""), Text));
        Assert.Equal(99, proposed.Seconds);
        Assert.NotSame(config.Net.Gate, proposed.Net.Gate);
        Assert.False(proposed.Net.Gate.IsBound);
    }

    [Fact]
    public void TheRowCarriesItsStateAndWhereItSits()
    {
        var config = new Config();
        config.Net.Gate.Bind(text => new SpoitableSettingsActionState
        {
            Tone = SpoitableSettingsTone.Warn, Status = text["s"], Detail = "d", Button = "b", ButtonNote = "n", Copy = "c",
        }, () => Task.CompletedTask);

        var row = Item(new ModuleConfigBinding(config, NullLogger.Instance).Describe(Text), "Net.Gate");

        Assert.Equal("Net.Gate", Field(row, "name"));
        Assert.Equal("ファイアウォール", Field(row, "label"));
        Assert.Equal("通れるか", Field(row, "description"));
        Assert.Equal(1, Field(row, "depth"));
        Assert.Equal(true, Field(row, "shown"));

        // 値を持たない（溜めにも「適用」にも乗らない）。
        Assert.Null(row.GetType().GetProperty("value"));

        var state = StateOf(row);
        Assert.Equal("warn", state.GetProperty("tone").GetString());
        Assert.Equal("s", state.GetProperty("status").GetString());
        Assert.Equal("d", state.GetProperty("detail").GetString());
        Assert.Equal("b", state.GetProperty("button").GetString());
        Assert.Equal("n", state.GetProperty("buttonNote").GetString());
        Assert.Equal("c", state.GetProperty("copy").GetString());
        Assert.False(state.GetProperty("running").GetBoolean());
        Assert.True(state.GetProperty("seq").GetInt64() > 0);
    }

    /// <summary>入れ物について出し入れする（ほかの項目と同じ）。前提にはスイッチ（Enabled）も指せる。</summary>
    [Fact]
    public void TheRowFollowsItsSection()
    {
        var config = new Config();
        var binding = new ModuleConfigBinding(config, NullLogger.Instance);

        Assert.Equal(false, Field(Item(binding.Describe(Text), "Hds.Gate"), "shown"));
        Assert.Equal(true, Field(Item(binding.DescribeWith(Json("""{ "Hds.Enabled": true }"""), Text), "Hds.Gate"), "shown"));

        var dependsOn = (string[])Field(Item(binding.Describe(Text), "Hds.Gate"), "dependsOn")!;
        Assert.Equal(["Hds.Enabled"], dependsOn);
    }

    /// <summary>前提は画面の宛先（保存名の道）で渡す — 画面の溜めはその名前で持っている。後ろに宣言した設定も指せる。</summary>
    [Fact]
    public void DependsOnIsGivenAsTheScreenKeysOfTheSettings()
    {
        var dependsOn = (string[])Field(Item(new ModuleConfigBinding(new Config(), NullLogger.Instance).Describe(Text), "Net.Gate"), "dependsOn")!;

        Assert.Equal(["Net.port", "Net.Later"], dependsOn);
    }

    /// <summary>指せない名前はエラーに残して外す。行そのものは出す。</summary>
    [Fact]
    public void DependsOnNamesOnlySettingsOfTheSameClass()
    {
        var logs = new Logs();
        var described = new ModuleConfigBinding(new Broken(), logs).Describe(Text);

        var dependsOn = (string[])Field(Item(described, "Wrong"), "dependsOn")!;
        Assert.Equal(["Port"], dependsOn);

        Assert.Equal(4, logs.Errors.Count(e => e.StartsWith("Broken.Wrong", StringComparison.Ordinal)));
        Assert.Contains(logs.Errors, e => e.Contains("'Missing'"));
        Assert.Contains(logs.Errors, e => e.Contains("'Inner'"));
        Assert.Contains(logs.Errors, e => e.Contains("'Later'"));
        Assert.Contains(logs.Errors, e => e.Contains("'Wrong'") && e.Contains("nameof(Port)") && e.Contains("Settings of Broken: Port"));
    }

    /// <summary>
    /// 姿の 6 つの欄は全部 required（書き忘れはモジュールの build で止まる。出さない欄も <c>= null</c> と書かせる）。
    /// 位置引数の constructor は持たない — あとで足す欄を required にしなければ、足しても SDK を壊さない。
    /// </summary>
    [Fact]
    public void EveryFieldOfTheStateHasToBeWritten()
    {
        var type = typeof(SpoitableSettingsActionState);

        var required = type.GetProperties()
            .Where(p => p.IsDefined(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute), inherit: false))
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Button", "ButtonNote", "Copy", "Detail", "Status", "Tone"], required);
        Assert.All(type.GetConstructors(), constructor => Assert.Empty(constructor.GetParameters()));
    }

    [Fact]
    public void TheConstructorAsksForDependsOn()
    {
        Assert.Throws<ArgumentNullException>(() => new SpoitableSettingsAction(dependsOn: null!));
        Assert.Throws<ArgumentException>(() => new SpoitableSettingsAction(dependsOn: [" "]));
    }

    [Fact]
    public void BindingTwiceIsRefusedWithWhatToDoInstead()
    {
        var row = new SpoitableSettingsAction(dependsOn: []);
        row.Bind(_ => State(), () => Task.CompletedTask);

        var refused = Assert.Throws<InvalidOperationException>(() => row.Bind(_ => State(), () => Task.CompletedTask));

        Assert.Contains("already bound", refused.Message);
        Assert.Contains("Changed()", refused.Message);
    }

    /// <summary>結ばなかった行は「使用できません」。警告は画面を何度描いても 1 回で、モジュールとメンバーを名指す。</summary>
    [Fact]
    public void AnUnboundRowSaysUnavailableAndWarnsOnceNamingTheModule()
    {
        var logs = new Logs();
        var store = NewStore(logs);
        store.Add("mine", "Mine", null, new Config(), Temp());

        var first = store.DescribeAction("mine", "Net.Gate", Text)!;
        store.Describe(Text);
        store.Describe(Text);

        Assert.Equal("host.settings.action.unavailable", first.Status);
        Assert.Equal("neutral", first.Tone);
        Assert.Null(first.Button);
        Assert.Single(logs.Warnings, w => w.Contains("mine: Box.Gate") && w.Contains("never bound") && w.Contains("Gate.Bind(describe, run)"));

        // 押しても何も起きない。
        Assert.False(store.RunAction("mine", "Net.Gate"));
    }

    /// <summary>結ぶ前の <c>Changed()</c> は何もしない。結んだら 1 回、その後は呼ぶたびに画面へ流す。</summary>
    [Fact]
    public async Task ChangedBeforeBindDoesNothingAndAfterwardsTellsTheScreen()
    {
        var calls = new Calls();
        var store = NewStore();
        store.ActionSink = calls;
        var config = new Config();
        store.Add("m", "M", null, config, Temp());

        config.Net.Gate.Changed();
        await Task.Delay(150);
        Assert.Equal(0, calls.Count);

        config.Net.Gate.Bind(_ => State(), () => Task.CompletedTask);
        await WaitFor(() => calls.Count == 1);
        config.Net.Gate.Changed();
        await WaitFor(() => calls.Count == 2);

        Assert.Equal(2, calls.Count);
        Assert.All(calls.Seen, call => Assert.Equal(("m", "Net.Gate"), call));
    }

    /// <summary>
    /// <c>Changed()</c> は呼んだ側の手元で describe を走らせない。モジュールが自分の lock を持ったまま呼んでも止まらない
    /// （describe が同じ lock を取りに行くので、手元で走らせると返ってこない）。
    /// </summary>
    [Fact]
    public async Task ChangedReturnsWithoutRunningTheDescribeOnTheCallersStack()
    {
        var store = NewStore();
        var views = new Views(store);
        store.ActionSink = views;
        var config = new Config();
        store.Add("m", "M", null, config, Temp());

        using var held = new SemaphoreSlim(1, 1);
        config.Net.Gate.Bind(_ =>
        {
            held.Wait();
            held.Release();
            return State("読んだ");
        }, () => Task.CompletedTask);
        await WaitFor(() => views.All().Length == 1);

        await held.WaitAsync();
        var changed = Task.Run(config.Net.Gate.Changed);
        var first = await Task.WhenAny(changed, Task.Delay(3000));
        held.Release();

        Assert.Same(changed, first);
        await WaitFor(() => views.All().Length == 2);
        Assert.Equal("読んだ", views.All()[^1].Status);
    }

    /// <summary>モジュールの describe が投げても設定画面は組み上がり、その行にはホストの文言が出る。札を空で返したときも同じ。</summary>
    [Fact]
    public void AThrowingDescribeShowsAHostErrorAndTheRestOfTheScreenStillBuilds()
    {
        var logs = new Logs();
        var config = new Config();
        config.Net.Gate.Bind(_ => throw new InvalidOperationException("壊れた"), () => Task.CompletedTask);
        config.Plain.Bind(_ => State(status: ""), () => Task.CompletedTask);

        var described = new ModuleConfigBinding(config, logs).Describe(Text);

        Assert.NotNull(Item(described, "Seconds"));
        foreach (var key in new[] { "Net.Gate", "Plain" })
        {
            var state = StateOf(Item(described, key));
            Assert.Equal("bad", state.GetProperty("tone").GetString());
            Assert.Equal("host.settings.action.failed", state.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, state.GetProperty("button").ValueKind);
        }

        Assert.Contains(logs.Warnings, w => w.Contains("could not read the state of Box.Gate"));
        Assert.Contains(logs.Warnings, w => w.Contains("Config.Plain returned no Status"));
    }

    /// <summary>注記はボタンのためのもの。ボタンが無ければ出さない。</summary>
    [Fact]
    public void ANoteWithoutAButtonIsDropped()
    {
        var config = new Config();
        config.Net.Gate.Bind(_ => new SpoitableSettingsActionState
        {
            Tone = SpoitableSettingsTone.Good, Status = "s", Detail = null, Button = null, ButtonNote = "n", Copy = null,
        }, () => Task.CompletedTask);

        var state = StateOf(Item(new ModuleConfigBinding(config, NullLogger.Instance).Describe(Text), "Net.Gate"));

        Assert.Equal("good", state.GetProperty("tone").GetString());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("buttonNote").ValueKind);
    }

    /// <summary>
    /// ボタンは押した瞬間に裏で走り、押した口は待たない。走っている間の押しは捨てて、その間は行も「走っている」と言う（全部の面でボタンを押せなくする）。
    /// 終わったら押せるに戻る。
    /// </summary>
    [Fact]
    public async Task APressWhileTheActionRunsIsIgnoredAndTheRowSaysItIsRunning()
    {
        var store = NewStore();
        var views = new Views(store);
        store.ActionSink = views;
        var config = new Config();
        store.Add("m", "M", null, config, Temp());

        var release = new TaskCompletionSource();
        var runs = 0;
        config.Net.Gate.Bind(_ => State(), () =>
        {
            Interlocked.Increment(ref runs);
            return release.Task;
        });

        Assert.True(store.RunAction("m", "Net.Gate"));
        Assert.False(store.RunAction("m", "Net.Gate"));
        Assert.True(store.DescribeAction("m", "Net.Gate", Text)!.Running);
        await WaitFor(() => Volatile.Read(ref runs) == 1);

        release.SetResult();
        await WaitFor(() => !store.DescribeAction("m", "Net.Gate", Text)!.Running);
        Assert.False(store.DescribeAction("m", "Net.Gate", Text)!.Running);

        Assert.True(store.RunAction("m", "Net.Gate"));
        await WaitFor(() => Volatile.Read(ref runs) == 2);
        Assert.Equal(2, Volatile.Read(ref runs));

        await WaitFor(() => views.All().Any(v => v.Running) && !views.All().MaxBy(v => v.Seq)!.Running);
        Assert.Contains(views.All(), v => v.Running);
        Assert.False(views.All().MaxBy(v => v.Seq)!.Running);

        // 無い行は何もしない。
        Assert.False(store.RunAction("m", "Nope"));
        Assert.False(store.RunAction("nobody", "Net.Gate"));
    }

    /// <summary>run が投げてもログに残すだけで、ボタンは押せるに戻る。</summary>
    [Fact]
    public async Task AThrowingRunIsLoggedAndTheButtonComesBack()
    {
        var logs = new Logs();
        var store = NewStore(logs);
        var config = new Config();
        store.Add("m", "M", null, config, Temp());
        config.Net.Gate.Bind(_ => State(), () => throw new InvalidOperationException("壊れた"));

        Assert.True(store.RunAction("m", "Net.Gate"));
        await WaitFor(() => logs.Warnings.Any(w => w.Contains("the action of Box.Gate failed")));

        Assert.Contains(logs.Warnings, w => w.Contains("m: the action of Box.Gate failed"));
        await WaitFor(() => !store.DescribeAction("m", "Net.Gate", Text)!.Running);
        Assert.True(store.RunAction("m", "Net.Gate"));
    }

    /// <summary>外したモジュールの行は、もう画面へ流さない。</summary>
    [Fact]
    public async Task ARemovedModuleNoLongerTellsTheScreen()
    {
        var calls = new Calls();
        var store = NewStore();
        store.ActionSink = calls;
        var config = new Config();
        store.Add("m", "M", null, config, Temp());
        config.Net.Gate.Bind(_ => State(), () => Task.CompletedTask);
        await WaitFor(() => calls.Count == 1);

        store.Remove("m");
        config.Net.Gate.Changed();
        await Task.Delay(150);

        Assert.Equal(1, calls.Count);
    }

    /// <summary>push はその 1 行だけで、姿は設定の材料（getConfigs の state）と同じ形。</summary>
    [Fact]
    public async Task ThePushCarriesThatRowInTheSameShapeAsTheScreenMaterial()
    {
        var broadcast = new Recording();
        var store = NewStore();
        store.ActionSink = new ConfigActionPusher(broadcast, store, Text);
        var config = new Config();
        store.Add("m", "M", null, config, Temp());

        config.Net.Gate.Bind(_ => State("押せます"), () => Task.CompletedTask);
        await WaitFor(() => broadcast.On(ConfigActionPusher.Channel).Length == 1);

        var push = JsonNode.Parse(UiBroadcast.Json(Assert.Single(broadcast.On(ConfigActionPusher.Channel)).Payload))!.AsObject();
        Assert.Equal("m", (string)push["module"]!);
        Assert.Equal("Net.Gate", (string)push["key"]!);
        Assert.Equal("押せます", (string)push["state"]!["status"]!);

        var material = JsonNode.Parse((string)Field(Item(store.Describe(Text).Select(group => Field(group, "items")).Cast<object[]>().Single(), "Net.Gate"), "state")!)!.AsObject();
        var pushed = push["state"]!.AsObject().Select(p => p.Key).ToArray();
        var described = material.Select(p => p.Key).ToArray();
        Assert.Equal(described, pushed);
    }

    /// <summary>文言は聞かれた言語で引き直す（言語を変えると、画面の読み直しで新しい言語になる）。</summary>
    [Fact]
    public void TheRowIsDescribedInTheLanguageItIsAskedIn()
    {
        var config = new Config();
        config.Net.Gate.Bind(text => State(text["greeting"]), () => Task.CompletedTask);
        var binding = new ModuleConfigBinding(config, NullLogger.Instance);

        var ja = StateOf(Item(binding.Describe(new Words("ja", new() { ["greeting"] = "見当たりません" })), "Net.Gate"));
        var en = StateOf(Item(binding.Describe(new Words("en", new() { ["greeting"] = "Not found" })), "Net.Gate"));

        Assert.Equal("見当たりません", ja.GetProperty("status").GetString());
        Assert.Equal("Not found", en.GetProperty("status").GetString());
        Assert.True(en.GetProperty("seq").GetInt64() > ja.GetProperty("seq").GetInt64());
    }
}
