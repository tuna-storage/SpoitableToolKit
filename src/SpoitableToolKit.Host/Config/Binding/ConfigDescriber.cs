using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Config.Attributes;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Host.Config.Actions;
using SpoitableToolKit.Host.Config.Interfaces;
using SpoitableToolKit.Host.Config.Models;
using SpoitableToolKit.Host.Ui.Push;

namespace SpoitableToolKit.Host.Config.Binding;

/// <param name="actionSource">動きの行の本物（<see cref="ConfigAction"/>）。写しの describer も本物を指す — 写しの行は結ばれていない。</param>
internal sealed class ConfigDescriber(
    IReadOnlyList<ConfigMember> members,
    IReadOnlyList<ConfigMember> order,
    Func<IModuleConfig> suggestionSource,
    Func<IReadOnlyList<ConfigAction>> actionSource,
    ILogger logger)
{
    /// <summary>
    /// 出す / 出さないは C# が計算して <c>shown</c> で渡す。条件はモジュールが書いたふつうの式なので画面では評価できないし、
    /// 画面に条件を持たせると種別が増えるたびに 2 か所を直すことになる（<c>LogRow</c> と同じ規律）。
    /// 隠れている行も落とさずに渡す。落とすと、聞き直しのたびにその行に溜めてある変更が画面から消える。
    /// 落とすのは属性で隠した行と、本物の設定が実行時の印で外した行（<see cref="IRuntimeHiddenSettings"/>。起動している間は変わらない）だけ。
    /// </summary>
    public object[] Describe(ILocalizer text) =>
    [
        .. order.Where(m => !m.Hidden && !m.IsSectionSwitch && !HiddenNow(m)).Select(object (m) => m.IsSection
            ? new
            {
                key = m.Path,
                name = m.NamePath,
                label = m.LabelFor(text),
                description = m.DescriptionFor(text),
                kind = ConfigKind.Section,
                depth = m.Section?.Depth + 1 ?? 0,

                // 入れ物そのものは、親が出ている限り出す。**中が切でも、入れ直す手が中にあれば見出しは残す** —
                // 見出しごと消えると、その手が無くなる。手が外にある形（選ぶ欄を見て出し入れする）では見出しも隠す。
                shown = HeadingShown(m),

                // 入/切を持つ入れ物なら、見出しにチェックを出す。持たないなら null。
                toggle = Switch(m),

                // 畳んでいるかどうかはここでは渡さない — 畳みは画面の都合なので、シェルが持っていて、
                // ホストは置き場（HostConfig.CollapsedCategories）としてだけ使う。
                foldable = m.Foldable,

                group = GroupOf(m, text),
                groupKey = GroupKeyOf(m),
            }
            : m.IsAction
            ? ActionRow(m, text)
            : new
            {
                key = m.Path,
                name = m.NamePath,
                label = m.LabelFor(text),
                description = m.DescriptionFor(text),
                kind = m.Kind,
                choices = m.Choices,
                itemKind = m.ItemKind,

                // 伏せる欄には候補を出さない。伏せた字の下に平文で並べたら、伏せた意味が消える。
                suggestions = m.Secret ? null : Suggest(m),

                // 伏せて打たせるか。種別ではない — 値は文字列のままで、変わるのは欄の見え方だけ。
                // 画面へ渡す値は伏せない（保存も生なので、伏せても隠したことにならない）。
                secret = m.Secret,

                // 見せ方だけの束ね。**入れ子の中の項目には付けない** — 束ねは段の一番上の並びの話。
                group = GroupOf(m, text),

                // 翻訳する前のキー。本体の設定は束ねごとに 1 ページで、言語を変えても同じページに居るための目印。
                groupKey = GroupKeyOf(m),
                restart = m.Restart,

                switches = m.Switches,

                editor = m.Editor,
                min = m.Range?.Minimum,
                max = m.Range?.Maximum,
                depth = m.Section?.Depth + 1 ?? 0,
                shown = Shown(m),
                // 値は JSON 文字列で渡す。JsonNode をそのまま返すと CefSharp が JS へ渡せず、
                // 例外も出さずに呼び出しが返ってこなくなる。
                value = JsonSerializer.Serialize(m.GetValue(m.Owner), m.Type, ConfigJson.Options),
            }),
    ];

    /// <summary>
    /// 値ではない行。<c>value</c> を持たない（「適用」にも溜めにも乗らない）。
    /// <c>dependsOn</c> は前提の設定の宛先 — 画面はそれに溜めてある変更があるかを見て、ボタンの代わりに「変更を適用すると使えます」を出す。
    /// <c>state</c> は push（<c>configAction</c>）と同じ形の JSON 文字列（record を CefSharp へそのまま渡すと壊れる。<c>value</c> と同じ約束）。
    /// 読むのは<b>本物の行</b>（下見の写しを描いているときも）。
    /// </summary>
    private object ActionRow(ConfigMember m, ILocalizer text) => new
    {
        key = m.Path,
        name = m.NamePath,
        label = m.LabelFor(text),
        description = m.DescriptionFor(text),
        kind = ConfigKind.Action,
        group = GroupOf(m, text),
        groupKey = GroupKeyOf(m),
        depth = m.Section?.Depth + 1 ?? 0,

        // 入れ物について出し入れする（ほかの項目と同じ）。
        shown = Shown(m),
        dependsOn = m.DependsOn,
        state = actionSource().FirstOrDefault(a => a.Member.NamePath == m.NamePath) is { } real
            ? UiBroadcast.Json(real.View(text))
            : null,
    };

    /// <summary>聞く相手は本物の設定（下見の写しを描いているときも）。写しには実行時に入れた印が付いていない。</summary>
    private bool HiddenNow(ConfigMember member) =>
        suggestionSource() is IRuntimeHiddenSettings runtime && runtime.IsHidden(member.NamePath);

    private static string? GroupOf(ConfigMember m, ILocalizer text) =>
        GroupKeyOf(m) is { } key ? text[key] : null;

    private static string? GroupKeyOf(ConfigMember m) => m.Section is null ? m.GroupKey : null;

    private object? Switch(ConfigMember box) =>
        members.FirstOrDefault(m => m.IsSectionSwitch && m.Section == box.Self) is { } found
            ? new { key = found.Path, value = found.GetValue(found.Owner) is true }
            : null;

    /// <summary>
    /// 入れ物の見出しを出すか。親が出ていて、しかも自分が入か、切でも入へ戻す手（見出しのスイッチか中のスイッチ）を持っているとき。
    /// 戻す手が入れ物の外にある形（<see cref="IConditionalSettings"/> が別の項目を見て決める）では、切の間は見出しも出さない。
    /// 出しておくと、中身の無い見出しだけが並ぶ。戻すには外の項目を変えればよいので、隠しても戻せなくはならない。
    /// </summary>
    private bool HeadingShown(ConfigMember box)
    {
        if (!(box.Section?.IsShown(logger) ?? true)) return false;
        if (box.Self is not { } self || self.IsShown(logger)) return true;

        return members.Any(m => m.Section == self && (m.IsSectionSwitch || m.Toggle));
    }

    /// <summary>スイッチ自身は、自分の入れ物が切でも出す。出さないと、切にした瞬間に入へ戻す手が消える。</summary>
    private bool Shown(ConfigMember member) =>
        member.Section is not { } section
            || (member.Toggle ? section.Parent?.IsShown(logger) ?? true : section.IsShown(logger));

    /// <summary>
    /// 設定画面を開くたびに引くので、実行時に変わるものでもよい。
    /// 例外は握り潰して「候補なし」にする。モジュールのコードを UI のスレッドで呼んでいるので、
    /// ここで抜けると設定画面が丸ごと開かなくなる（<c>getConfigs()</c> が返らない）。
    /// 配列で返す。<c>IReadOnlyList</c> のまま CefSharp へ渡さない（<c>choices</c> と揃える）。
    /// </summary>
    private string[]? Suggest(ConfigMember member)
    {
        var source = suggestionSource();
        try
        {
            // 入れ子でも同じ設定クラスが答える。
            return source.SuggestValuesFor(member.NamePath) is { Count: > 0 } values ? [.. values] : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read suggestions for {Type}.{Member}",
                source.GetType().Name, member.Name);
            return null;
        }
    }
}
