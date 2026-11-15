using System.Reflection;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Config;

namespace SpoitableToolKit.Host.Config.Binding;

/// <summary>
/// 入れ子は組み立てのときに 1 回だけ潜って平らにする。読み込み・保存・適用は平らな一覧を回すだけで済み、
/// 変わるのは読み書きの相手（根の設定 → その項目が載っているインスタンス）だけ。
/// </summary>
internal sealed class ConfigMemberCollector
{
    private const int MaxDepth = 32;

    private readonly List<ConfigMember> _members = [];

    private readonly List<ConfigMember> _order = [];

    private ConfigMemberCollector()
    {
    }

    public static CollectedConfigMembers Collect(IModuleConfig root, ILogger logger)
    {
        var collector = new ConfigMemberCollector();

        // 根も「踏んだ物」に入れておく。子が親を指し返しても戻ってこられる。
        collector.Collect(root, section: null, prefix: "", namePrefix: "", depth: 0,
            new HashSet<object>(ReferenceEqualityComparer.Instance) { root }, logger);

        return new CollectedConfigMembers(collector._members, collector._order);
    }

    private void Collect(object instance, ConfigSection? section, string prefix, string namePrefix,
        int depth, HashSet<object> visited, ILogger logger)
    {
        // 安全網。**踏んだ物を覚える見張りが本命**で、これはそれが効かなかったときのため。
        // 無いと、思わぬ形の設定でスタックを使い切って**アプリごと落ちる**（例外にすらならない）。
        // 32 段は現実の設定では届かない深さ。
        if (depth > MaxDepth)
        {
            logger.LogError("{Prefix} is nested deeper than {Max}; stopping here", prefix, MaxDepth);
            return;
        }

        foreach (var member in instance.GetType()
                     .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                     .Where(m => m.MemberType is MemberTypes.Field or MemberTypes.Property))
        {
            // 入れ物が先。**書けなくてもよい**ので、書ける条件で弾かれる前に見る。
            if (ConfigMember.TryCreateSection(member, out var box))
            {
                Attach(box, instance, section, prefix, namePrefix);

                if (box.GetValue(instance) is not { } inner)
                {
                    // 中身の無い入れ物。1 つ壊れていても他を巻き添えにしない。
                    logger.LogWarning("{Path} is null; skipping the settings inside it", box.Path);
                    continue;
                }

                // **踏んだ物を覚える。** IsEnabled で親を見る形だと子が親への参照を持つので、
                // 素直に潜ると 子 → 親 → 子 で戻ってこない。
                if (!visited.Add(inner))
                {
                    logger.LogWarning("{Path} points back at settings already bound; not descending", box.Path);
                    continue;
                }

                box.Self = new ConfigSection(box, inner, section);
                _order.Add(box);

                Collect(inner, box.Self, box.Path + ".", box.NamePath + ".", depth + 1, visited, logger);
                continue;
            }

            if (ConfigMember.TryCreate(member, out var bound))
            {
                Attach(bound, instance, section, prefix, namePrefix);
                _members.Add(bound);
                _order.Add(bound);
            }
            else if (!IsConditionOf(instance, member))
                // readonly フィールドや get だけのプロパティ。画面から変えられないので
                // 設定として扱わない。黙って無視すると「変えたのに効かない」に見えるので知らせる。
                logger.LogWarning("{Type}.{Member} is not writable; not treated as a setting",
                    instance.GetType().Name, member.Name);
        }
    }

    /// <summary>
    /// <see cref="IConditionalSettings.IsEnabled"/> の実装か。出す条件を読む口で、<b>設定ではないと決まっている</b>ので知らせない。
    /// 名前ではなく実装の対応で見る（同じ名前の別のプロパティは今までどおり知らせる）。
    /// </summary>
    private static bool IsConditionOf(object instance, MemberInfo member) =>
        instance is IConditionalSettings
        && member is PropertyInfo { GetMethod: { } getter }
        && instance.GetType().GetInterfaceMap(typeof(IConditionalSettings)).TargetMethods.Contains(getter);

    private static void Attach(ConfigMember member, object owner, ConfigSection? section,
        string prefix, string namePrefix)
    {
        member.Owner = owner;
        member.Section = section;
        member.Path = prefix + member.Key;
        member.NamePath = namePrefix + member.Name;
    }
}
