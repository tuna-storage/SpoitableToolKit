using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Events.Config;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Host.Config.Actions;
using SpoitableToolKit.Host.Config.Binding;
using SpoitableToolKit.Host.Config.Models;

namespace SpoitableToolKit.Host.Config;

/// <summary>
/// reflection をここに閉じ込める。知らない型も保存も表示も落とさない（<see cref="ConfigKind.Json"/> になる。
/// 「保存されているのに画面に無い」を作らないため）。
/// インスタンスはモジュールと共有していて、ここが書き込んだ値をモジュールはそのまま読む。
/// </summary>
internal sealed class ModuleConfigBinding
{
    private readonly IModuleConfig _instance;
    private readonly ILogger _logger;

    /// <summary>
    /// 候補を引く相手。下見の写し（<see cref="Copy"/>）は本物を指す — 候補は実行時の中身（テーマの一覧・状態の口の名前など）から作られ、写しに写らない。
    /// 引き換えに、候補をほかの設定の値で変える設定は、下見の途中の値では候補が変わらない（<see cref="IModuleConfig.SuggestValuesFor"/> に書いてある）。
    /// </summary>
    private IModuleConfig SuggestionSource { get; init; }

    /// <summary>
    /// 動きの行の本物。写し（<see cref="Copy"/>）も本物を指す — 写しの行は <c>new</c> で作り直されたもので、モジュールは本物にしか結ばない。
    /// </summary>
    private IReadOnlyList<ConfigAction> ActionSource { get; init; }

    private readonly IReadOnlyList<ConfigMember> _members;

    private readonly IReadOnlyList<ConfigMember> _order;

    private readonly ConfigDescriber _describer;

    public ModuleConfigBinding(IModuleConfig instance, ILogger logger)
        : this(instance, logger, collectLogger: logger)
    {
    }

    /// <param name="collectLogger">組み立ての警告を残す先。写し（<see cref="Copy"/>）は黙らせる —
    /// 本物を組み立てたときに 1 回出ていて、写しは打つたびに作るので、同じ警告がアプリログに積もる。</param>
    private ModuleConfigBinding(IModuleConfig instance, ILogger logger, ILogger collectLogger)
    {
        _instance = instance;
        _logger = logger;
        SuggestionSource = instance;

        (_members, _order) = ConfigMemberCollector.Collect(instance, collectLogger);

        ActionSource =
        [
            .. _order.Where(m => m.IsAction)
                .Select(m => new ConfigAction(m, (SpoitableSettingsAction)m.GetValue(m.Owner)!, logger)),
        ];

        // 候補と動きの行の相手は読むときに引く（写しは、作ったあとに本物へ向け直す）。
        _describer = new ConfigDescriber(_members, _order, () => SuggestionSource, () => ActionSource, logger);
    }

    public IReadOnlyList<ConfigMember> Members => _members;

    /// <summary>動きの行（本物）。設定の一覧（<see cref="Members"/>）には入っていない。</summary>
    public IReadOnlyList<ConfigAction> Actions => ActionSource;

    public IModuleConfig Instance => _instance;

    /// <summary>入れ子を持っているか。持たない設定では聞き直しを 1 回も走らせない。</summary>
    public bool HasSections => _order.Any(m => m.IsSection);

    public void Load(JsonObject saved, ILogger logger) => ConfigJson.Load(_members, saved, logger);

    public JsonObject Save() => ConfigJson.Save(_members);

    public object[] Describe(ILocalizer text) => _describer.Describe(text);

    /// <summary>
    /// 溜めてある変更を当てたつもりで材料を作る。スイッチを入れた瞬間に中身が出ないと入にした直後に打ち込めないが、
    /// <c>IsEnabled</c> はモジュールが書いたふつうの式で画面側では評価できないので、写し（<see cref="Copy"/>）に当てて描く。
    /// 共有の設定には当てない（当てて戻す間、モジュールの裏の thread が範囲外の値や、listener が断ったばかりの値を読める）。候補だけは本物から引く。
    /// 読めない値は飛ばす。適用の「1 つでも駄目なら全部やめる」はここでは通さない —
    /// 打ちかけの数字で画面が組み直せなくなると、打ち終わる前に何も見えなくなる。
    /// </summary>
    public object[] DescribeWith(IEnumerable<KeyValuePair<string, JsonNode?>> changes, ILocalizer text)
    {
        var preview = Copy();

        foreach (var (key, value) in changes)
        {
            if (preview._members.FirstOrDefault(m => m.Path == key && !m.Hidden) is not { } member) continue;

            try
            {
                preview.Write(member, value);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
            {
                continue;
            }
        }

        return preview.Describe(text);
    }

    /// <summary>
    /// 画面の「適用」の値を読んで確かめ、今の値と違うものだけを返す。まだ書かない（書くのは聞き終わってから: <see cref="Write"/>）。
    /// 全部読めることを確かめてから返す。1 つでも読めない・範囲外なら例外（<see cref="ConfigValueException"/>。どの欄かを持つ）。
    /// 値が変わらなかったものは返さない。押しただけで listener が動くと、受け取る側が「変わったら作り直す」と素直に書けなくなる。
    /// </summary>
    public IReadOnlyList<(ConfigMember Member, JsonNode? Node)> Changes(IEnumerable<KeyValuePair<string, JsonNode?>> changes, ILocalizer text)
    {
        List<(ConfigMember Member, JsonNode? Node)> changed = [];

        foreach (var (key, value) in changes)
        {
            // 弾くのは属性の Hidden だけ。IsEnabled で隠れている行は適用できる — 値は消さないので、隠れる前に溜めた変更も生きたまま通す。
            if (_members.FirstOrDefault(m => m.Path == key && !m.Hidden) is not { } member)
                throw new ArgumentException(text.Format("host.config.unknownItem", key));

            object? parsedValue;
            try
            {
                parsedValue = value.Deserialize(member.Type, ConfigJson.Options);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
            {
                throw new ConfigValueException(member.Path, ex.Message);
            }

            // 範囲外は読めない値と同じ扱い。丸めない（打った値ではないものを保存しない）。
            if (member.RangeError(parsedValue, text) is { } outOfRange)
                throw new ConfigValueException(member.Path, outOfRange);

            if (ConfigJson.SameValue(member.GetValue(member.Owner), parsedValue, member.Type)) continue;

            // 写しと共有の設定に別々に読み直すので、JSON のまま持つ（List などを 2 つで共有しない）。
            changed.Add((member, value?.DeepClone()));
        }

        return changed;
    }

    public void Write(ConfigMember member, JsonNode? node) => member.SetValue(member.Owner, node.Deserialize(member.Type, ConfigJson.Options));

    /// <summary>
    /// 入ったらこうなる写し（<see cref="ConfigChangingEventParams{TConfig}.Proposed"/>）。引数なしで新しく作り、今の値を全部入れてから、変わった値を重ねる
    /// （変わった値だけを写すと、残りが既定値になって組み合わせの確かめが狂う）。
    /// 設定画面に出ない中身（internal の field など）は写らない。
    /// </summary>
    public IModuleConfig Propose(IEnumerable<(ConfigMember Member, JsonNode? Node)> changed)
    {
        var binding = Copy();

        foreach (var (member, node) in changed)
            binding.Write(binding._members.First(m => m.Path == member.Path), node);

        return binding._instance;
    }

    /// <summary>今の値を全部入れた写し。共有の設定には触らない。候補は本物から引く（<see cref="SuggestionSource"/>）。</summary>
    private ModuleConfigBinding Copy()
    {
        var copy = (IModuleConfig)Activator.CreateInstance(_instance.GetType())!;
        var binding = new ModuleConfigBinding(copy, _logger, NullLogger.Instance) { SuggestionSource = _instance, ActionSource = ActionSource };
        binding.Load(Save(), NullLogger.Instance);
        return binding;
    }
}
