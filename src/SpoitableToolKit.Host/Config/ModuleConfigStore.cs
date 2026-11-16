using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.Events.Config;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Host.Config.Actions;
using SpoitableToolKit.Host.Config.Interfaces;
using SpoitableToolKit.Host.Config.Models;
using SpoitableToolKit.Host.Events;

namespace SpoitableToolKit.Host.Config;

/// <summary>
/// 保存先はモジュールごとの <c>module-data/&lt;Id&gt;/config.json</c>。モジュール同士で設定を壊せないように、データディレクトリと同じ単位で分ける。
/// 値の持ち主はモジュールが渡した <see cref="IModuleConfig"/> のインスタンスそのもの。写しは持たない（持つと、どちらが正か分からなくなる）。
/// </summary>
internal sealed class ModuleConfigStore(EventHubs hubs, ILoggerFactory loggerFactory)
{
    private readonly ILogger<ModuleConfigStore> _logger = loggerFactory.CreateLogger<ModuleConfigStore>();
    private readonly Lock _gate = new();
    private readonly List<Entry> _entries = [];

    private sealed class Entry
    {
        public required string ModuleId { get; init; }
        public required string DisplayName { get; init; }

        public string? DisplayNameKey { get; init; }
        public required string? Icon { get; init; }
        public required ModuleConfigBinding Binding { get; init; }
        public required string File { get; init; }

        public required IConfigChannel Channel { get; init; }

        /// <summary>項目の名前の全部（C# の名前を <c>.</c> で繋いだもの）。listener の式が項目を指しているかを確かめる。</summary>
        public required IReadOnlyCollection<string> Settings { get; init; }
    }

    /// <summary>
    /// 設定を持つモジュールを迎え入れ、保存済みの値を流し込む。
    /// <see cref="ISpoitableModule.OnInit"/> より前に呼ぶこと（OnInit の時点で値が入っている、と約束しているため）。
    /// 設定のクラスは引数なしで作れること。聞いている間は写し（<see cref="ConfigChangingEventParams{TConfig}.Proposed"/>）を見せるので、作れなければ分かる文で断る。
    /// </summary>
    /// <param name="displayNameKey">ホスト本体のように、こちらで文言を持っているものだけ渡す。モジュールの題名は <c>ISpoitableModule.DisplayName</c> をそのまま出す。</param>
    public IEventSource Add(string moduleId, string displayName, string? icon, IModuleConfig config, string dataDirectory,
        string? displayNameKey = null)
    {
        var type = config.GetType();
        if (type.GetConstructor(Type.EmptyTypes) is null)
            throw new InvalidOperationException(
                $"{type.FullName} (the settings of '{moduleId}') has no public constructor without parameters. " +
                $"The app shows listeners the new values in a copy before it writes them (ConfigChangingEventParams.Proposed), " +
                $"so it must be able to make one with new {type.Name}(). Give the settings class a public constructor without parameters.");

        var file = Path.Combine(dataDirectory, "config.json");
        var binding = new ModuleConfigBinding(config, _logger);

        if (Read(file) is { } saved) binding.Load(saved, _logger);

        // 動きの行の「変わった」を受ける。モジュールが結ぶのは OnInit（このあと）なので、先に張っておく。
        foreach (var action in binding.Actions)
        {
            action.ModuleId = moduleId;
            action.Row.Listen(() => Notify(moduleId, action));
        }

        var channel = ConfigChannel.Create(hubs, type);
        lock (_gate)
        {
            _entries.Add(new Entry
            {
                ModuleId = moduleId,
                DisplayName = displayName,
                DisplayNameKey = displayNameKey,
                Icon = icon,
                Binding = binding,
                File = file,
                Channel = channel,
                Settings = [.. binding.Members.Select(m => m.NamePath)],
            });
        }

        // 設定そのものが listener を兼ねていることはない（データのクラスなので）。足していないので届かない、と言う。
        if (type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConfigEventListener<>)))
            _logger.LogWarning(
                "{Id}: the settings class {Type} implements IConfigEventListener, which has no effect by itself. " +
                "Add the listener with host.Config.AddListener<IConfigEventListener<{Name}>>(this, listener)", moduleId, type.FullName, type.Name);

        return channel.Source;
    }

    /// <summary>
    /// いまの値をそのまま保存する。コードが書き換えたときの口。
    /// ホスト自身が書く値（ログ画面のチップ <c>HiddenKinds</c>・外部ツールの確認時刻など）用で、丸めも通知もしない —
    /// 丸めは画面とファイルからの入力を守るためのもので、ホスト自身のコードは信用してよい。
    /// </summary>
    public void Save(string moduleId)
    {
        // 書き出しもロックの中でやる。画面からの「適用」とホスト自身の保存が同時に走ると、
        // 同じファイルを 2 つが開いて片方が落ちる（握り潰される警告になって、保存が 1 回消える）。
        lock (_gate)
        {
            // 画面には出ない（コードから呼ぶ口で、Id が登録済みなのは呼ぶ側の責任）。翻訳しない。
            Write(Find(moduleId) ?? throw new ArgumentException($"Module has no settings: {moduleId}"));
        }
    }

    public void Remove(string moduleId)
    {
        List<Entry> removed;
        lock (_gate)
        {
            removed = [.. _entries.Where(e => string.Equals(e.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase))];
            _entries.RemoveAll(removed.Contains);
        }

        foreach (var entry in removed)
        {
            entry.Channel.Close();

            // 外したモジュールの Changed() が、画面に残っていない行を描きに来ないように。
            foreach (var action in entry.Binding.Actions) action.Row.Listen(null);
        }
    }

    /// <summary>
    /// 動きの行が変わったことを画面へ流す係。窓口と同じ頃（設定より後）に出来るので後から挿す。挿す前の「変わった」は捨てる
    /// （画面は開いたときに <see cref="Describe"/> で読み直す）。
    /// </summary>
    internal IConfigActionSink? ActionSink { get; set; }

    /// <summary>1 つの行のいまの姿（push の材料）。無い行なら <c>null</c>。モジュールの describe は lock の外で呼ぶ。</summary>
    public ConfigActionView? DescribeAction(string moduleId, string key, ILocalizer text) =>
        FindAction(moduleId, key)?.View(text);

    /// <summary>
    /// 動きの行のボタン。<b>待たない</b> — 走らせるのは裏で、走り始めと終わりに画面へ流す。
    /// 走っている間の押し・結んでいない行・無い行は何もしない（false）。
    /// </summary>
    public bool RunAction(string moduleId, string key)
    {
        if (FindAction(moduleId, key) is not { } action)
        {
            _logger.LogWarning("The screen pressed an action that does not exist: {Module} {Key}", moduleId, key);
            return false;
        }

        return action.TryStart(() => Notify(moduleId, action));
    }

    private ConfigAction? FindAction(string moduleId, string key)
    {
        lock (_gate) return Find(moduleId)?.Binding.Actions.FirstOrDefault(a => a.Path == key);
    }

    /// <summary>
    /// 画面へ流す。<b>呼んだ側の手元では describe を走らせない</b> — モジュールが自分の lock を持ったまま <c>Changed()</c> を呼ぶと、
    /// その手元で describe（同じ lock を取りに行く）を呼べば止まる。
    /// </summary>
    private void Notify(string moduleId, ConfigAction action)
    {
        if (ActionSink is not { } sink) return;

        _ = Task.Run(() =>
        {
            try
            {
                sink.Changed(moduleId, action.Path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not show the new state of {Module} {Key}", moduleId, action.Path);
            }
        });
    }

    internal int? ListenerCount(string moduleId)
    {
        lock (_gate) return Find(moduleId)?.Channel.ListenerCount;
    }

    public object[] Describe(ILocalizer text)
    {
        lock (_gate)
        {
            return
            [
                .. _entries.Select(e => new
                {
                    module = e.ModuleId,
                    name = e.DisplayNameKey is { } key ? text[key] : e.DisplayName,
                    icon = e.Icon,
                    items = e.Binding.Describe(text),
                }),
            ];
        }
    }

    /// <summary>
    /// 溜めてある変更を当てたつもりで画面の材料を作る。入れ子の出し入れは <c>IsEnabled</c>（モジュールが書いたふつうの式）が決めるので画面側では評価できず、
    /// スイッチを入れた瞬間に中身が出ないと入にした直後に打ち込めない。保存はしないし、知らせもしない。
    /// </summary>
    public object[] DescribeWith(JsonObject pending, ILocalizer text)
    {
        lock (_gate)
        {
            return
            [
                .. _entries.Select(e => new
                {
                    module = e.ModuleId,
                    name = e.DisplayNameKey is { } key ? text[key] : e.DisplayName,
                    icon = e.Icon,
                    items = pending[e.ModuleId] is JsonObject changes && changes.Count > 0
                        ? e.Binding.DescribeWith(changes, text)
                        : e.Binding.Describe(text),
                }),
            ];
        }
    }

    /// <summary>
    /// 画面の「適用」で、1 つのモジュールの設定をまとめて書き換える。
    /// <code>
    /// 読んで確かめる（読めない・範囲外は、その欄の一言で全部やめる）
    ///   → 聞く（OnConfigChanging。共有の設定はまだ前の値。新しい値は写しで見せる）
    ///   → 断られなかった値だけ書いて保存する
    ///   → 知らせる（OnConfigChanged。入った値だけ。1 つも入らなければ知らせない）
    /// </code>
    /// 入力のたびに知らせない（関係する設定を 2 つ変えただけで繋ぎ直しが 2 回走るのを避ける）。
    /// 聞いている間、共有の設定には書かない（書いてから聞くと、その間に裏の thread が断られる値を読める）。
    /// 断るのは値ごと（断られた値だけ前の値に残り、ほかは入る）。全部を断られたら何も書かない。
    /// 断られた・読めなかったときは <see cref="ConfigRejectedException"/>（欄ごとの理由と、入った欄）。
    /// </summary>
    public void Apply(string moduleId, JsonObject changes, ILocalizer text)
    {
        Entry entry;
        lock (_gate)
        {
            entry = Find(moduleId) ?? throw new ArgumentException(text.Format("host.config.unknownModule", moduleId));
        }

        IReadOnlyList<(ConfigMember Member, JsonNode? Node)> changed;
        try
        {
            changed = entry.Binding.Changes(changes, text);
        }
        catch (ConfigValueException ex)
        {
            throw new ConfigRejectedException([new ConfigNote(ex.Key, ex.Message)], applied: []);
        }

        if (changed.Count == 0) return;

        string[] names = [.. changed.Select(c => c.Member.NamePath)];
        var answer = entry.Channel.Ask(entry.Binding.Propose(changed), names, entry.Settings);

        if (answer.Result.Kind == EventResultKind.Stop)
        {
            // 全部を断られた。何も書かず、保存もしない。宛先の欄は無い（変更のまとまりに対する答え）ので、全体への一言にする。
            _logger.LogInformation("{Id} refused the whole config change: {Reason}", moduleId, answer.Result.Reason);
            throw new ConfigRejectedException([new ConfigNote(null, answer.Result.Reason ?? text["host.config.rejected"])], applied: []);
        }

        var rejected = answer.Rejections.ToDictionary(r => r.Name, r => r.Reason, StringComparer.Ordinal);
        var entered = changed.Where(c => !rejected.ContainsKey(c.Member.NamePath)).ToArray();

        if (entered.Length > 0)
        {
            foreach (var (member, node) in entered) entry.Binding.Write(member, node);
            lock (_gate) Write(entry);
            entry.Channel.Tell(entry.Binding.Instance, [.. entered.Select(c => c.Member.NamePath)], entry.Settings);
        }

        if (rejected.Count == 0) return;

        // 断られた値は前の値のまま（書いていない）。画面はその欄を赤枠にして理由を出す（key は画面の欄の名前 = 保存名の道）。
        _logger.LogInformation("{Id} rejected {Members}", moduleId, string.Join(", ", rejected.Keys));
        throw new ConfigRejectedException(
            [.. changed.Where(c => rejected.ContainsKey(c.Member.NamePath)).Select(c => new ConfigNote(c.Member.Path, rejected[c.Member.NamePath]))],
            applied: [.. entered.Select(c => c.Member.Path)]);
    }

    private Entry? Find(string moduleId) =>
        _entries.FirstOrDefault(e => string.Equals(e.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase));

    private JsonObject? Read(string file)
    {
        try
        {
            return File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file)) as JsonObject : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 壊れた設定でモジュールを止めない。既定値で動かして、次の保存で上書きする。
            _logger.LogWarning(ex, "Cannot read {File}; starting from defaults", file);
            return null;
        }
    }

    private void Write(Entry entry)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(entry.File)!);
            File.WriteAllText(entry.File, entry.Binding.Save().ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not write {File}", entry.File);
        }
    }
}
