using SpoitableToolKit.Host.Modules.Loading.Models;
using AssemblyReference = SpoitableToolKit.Host.Modules.Loading.Models.AssemblyReference;
using SpoitableToolKit.Host.Modules.Packaging;

namespace SpoitableToolKit.Host.Modules.Loading;

/// <summary>
/// <b>読み込む前に</b>、どのモジュールが動けるかと、どの共有の dll を載せるかを決める。dll は読み込まない（純粋な関数）。
/// <para>
/// 調べる順番は<b>ローダーと同じ</b>（<see cref="ModuleLoadContext"/>: 共有 → モジュールのフォルダ → 本体）。
/// 順番がずれると、ここでは通ったのに動かすと落ちる（またはその逆）になる。
/// </para>
/// <list type="bullet">
///   <item>同じ名前の共有の dll が複数あれば、<b>いちばん新しい版</b>を載せる。版が同じで中身が違えば警告して、先に来た方（並びの順）</item>
///   <item>参照している版が、載せた版より<b>新しければ</b>そのモジュールは読まない（持ってきた側を更新してもらう）。古い版で作ったものは新しい方で動かす</item>
///   <item>共有の dll が参照してよいのは、本体が持つものと、ほかの共有の dll だけ（共有は無条件で先に返るので、モジュールのフォルダの dll には頼れない）</item>
///   <item>読めないと決まったものの共有は外し、もう一度決め直す（別のモジュールが持っていた写しが代わりに載ることがある）。落ちるものが無くなるまで繰り返す</item>
/// </list>
/// <para>
/// 外す順番: <b>共有の dll の参照が解けないもの</b>を先に外す（外すと共有は減る一方なので、これは後から直らない）。
/// それが無くなってから、入口の参照が解けないものを外す。ただし「古すぎる」の持ち主がその回で落ちるなら、まだ外さない
/// （持ち主が外れて写しが無くなれば、自分のフォルダの写しで動けることがある）。
/// </para>
/// <para>
/// ⚠ 調べるのは<b>入口の dll の参照と、共有の dll の参照だけ</b>。モジュールのフォルダにある別の dll が古い共有を参照していても、
/// ここでは見えない（動かしたときにその dll を触った所で落ちる。落ちるのはそのモジュールだけ）。
/// </para>
/// </summary>
internal static class ModuleDependencies
{
    /// <summary>本体の API の dll の名前。モジュールは持ち込まず、本体が読み込んだ 1 つを使う。</summary>
    public const string AppApi = "SpoitableToolKit.Abstractions";

    /// <param name="units">調べる単位。<b>並びの順が「先に来た方」</b>（ユーザー側 → 同梱、フォルダ名の順）。</param>
    /// <param name="hostResolves">本体（既定のコンテキスト）がその名前・版を出せるか。</param>
    /// <param name="ownFolderHas">そのモジュールのフォルダ（deps.json）から出せるか。引数は Id と参照。</param>
    /// <param name="minimumApi">本体の API の、読めるいちばん古い版（<see cref="ModuleManifest.MinimumSdk"/>）。これより古い版を参照するものは読まない。</param>
    /// <param name="hostVersion">
    /// 本体がその名前で持っている版（持っていなければ null）。<b>理由の文のためだけ</b>に聞く:
    /// 出せなかった参照を本体が古い版で持っていれば「見つからない」ではなく「アプリが古い」と言う。出せるかどうかは <paramref name="hostResolves"/> が決める。
    /// </param>
    public static DependencyResolution Resolve(
        IReadOnlyList<ModuleUnit> units, Func<AssemblyReference, bool> hostResolves, Func<string, AssemblyReference, bool> ownFolderHas,
        Func<string, Version?> hostVersion, Version minimumApi)
    {
        var alive = units.ToList();
        Dictionary<string, DependencyProblem> failures = new(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            // 警告は最後の回の分だけ（途中の回で選んだ写しの持ち主は、あとで外れることがある）。
            List<string> warnings = [];
            var shared = Choose(alive, warnings);

            var failing = alive
                .Select(unit => (unit, problem: Check(unit, shared, hostResolves, ownFolderHas, hostVersion, minimumApi)))
                .Where(f => f.problem is not null)
                .Select(f => (f.unit, problem: f.problem!))
                .ToList();

            if (failing.Count == 0) return new(shared, failures, warnings.Distinct().ToArray());

            var remove = failing.Where(f => f.problem.From is not null).ToList();
            if (remove.Count == 0)
            {
                var failingIds = failing.Select(f => f.unit.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                remove = [.. failing.Where(f => f.problem.Provider is not { } provider || !failingIds.Contains(provider))];
                if (remove.Count == 0) remove = failing;
            }

            foreach (var (unit, problem) in remove)
            {
                failures[unit.Id] = problem;
                alive.Remove(unit);
            }
        }
    }

    private static Dictionary<string, SharedFile> Choose(IEnumerable<ModuleUnit> units, List<string> warnings)
    {
        Dictionary<string, SharedFile> chosen = new(StringComparer.OrdinalIgnoreCase);

        foreach (var file in units.SelectMany(u => u.Shared))
        {
            if (!chosen.TryGetValue(file.Name, out var current))
            {
                chosen[file.Name] = file;
                continue;
            }

            var order = file.Version.CompareTo(current.Version);
            if (order > 0)
            {
                chosen[file.Name] = file;
            }
            else if (order == 0 && !string.Equals(file.Sha256, current.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"The shared {file.Name} {file.Version} differs between {current.Owner} and {file.Owner} " +
                             $"although the version is the same; using {current.Owner}'s copy");
            }
        }

        return chosen;
    }

    private static DependencyProblem? Check(ModuleUnit unit, IReadOnlyDictionary<string, SharedFile> shared,
        Func<AssemblyReference, bool> hostResolves, Func<string, AssemblyReference, bool> ownFolderHas,
        Func<string, Version?> hostVersion, Version minimumApi)
    {
        // 自分の写しが選ばれた共有の dll。載るのはこれなので、これの参照が解けないと持ち主が責任を負う。
        foreach (var file in unit.Shared)
        {
            if (!ReferenceEquals(shared.GetValueOrDefault(file.Name), file)) continue;

            foreach (var reference in file.References)
            {
                if (reference.Name.Equals(file.Name, StringComparison.OrdinalIgnoreCase)) continue;
                if (BuiltWithAnOldSdk(reference, file.Name, minimumApi) is { } old) return old;
                if (FromShared(reference, shared, file.Name) is { } tooOld) return tooOld;
                if (shared.ContainsKey(reference.Name) || hostResolves(reference)) continue;
                return NotFound(reference, file.Name, hostVersion);
            }
        }

        foreach (var reference in unit.EntryReferences)
        {
            // 古い SDK で作ったものは、本体が新しい版で出せても読まない（消えた・変わった型で落ちる）。
            if (BuiltWithAnOldSdk(reference, from: null, minimumApi) is { } old) return old;

            // ローダーと同じ順: 共有 → 自分のフォルダ → 本体。
            if (shared.ContainsKey(reference.Name))
            {
                if (FromShared(reference, shared, from: null) is { } tooOld) return tooOld;
                continue;
            }

            if (ownFolderHas(unit.Id, reference) || hostResolves(reference)) continue;
            return NotFound(reference, from: null, hostVersion);
        }

        return null;
    }

    /// <summary>
    /// どこからも出せなかった参照。本体が古い版で持っているなら、そう言う。
    /// <b>本体の API なら「アプリが古い」</b>（直すのはアプリの更新）。ほかの dll なら「見つからない」のまま、本体の版を添える
    /// （直すのはモジュールの側: 自分のフォルダに新しい版を持ってくる。本体を更新しても、その dll が新しくなるとは限らない）。
    /// </summary>
    private static DependencyProblem NotFound(AssemblyReference reference, string? from, Func<string, Version?> hostVersion)
    {
        if (hostVersion(reference.Name) is not { } has || has >= reference.Version)
            return new(DependencyProblemKind.Missing, reference, from);

        return reference.Name.Equals(AppApi, StringComparison.OrdinalIgnoreCase)
            ? new(DependencyProblemKind.HostTooOld, reference, from, has)
            : new(DependencyProblemKind.Missing, reference, from, has);
    }

    /// <summary>
    /// 読み込んだ dll の参照（<see cref="System.Reflection.Assembly.GetReferencedAssemblies"/>）に、本体が読めるより古い API があるか。
    /// 読み込む前の確かめ（<see cref="Resolve"/>）で dll が開けなかったとき（ほかのプログラムが掴んでいた など）の、読み込んだ後のもう一度。
    /// </summary>
    public static DependencyProblem? BuiltWithAnOldSdk(IEnumerable<System.Reflection.AssemblyName> references, Version minimumApi) =>
        references
            .Where(r => r.Name is not null)
            .Select(r => BuiltWithAnOldSdk(new AssemblyReference(r.Name!, r.Version ?? new Version(0, 0, 0, 0)), from: null, minimumApi))
            .FirstOrDefault(problem => problem is not null);

    private static DependencyProblem? BuiltWithAnOldSdk(AssemblyReference reference, string? from, Version minimumApi) =>
        reference.Name.Equals(AppApi, StringComparison.OrdinalIgnoreCase) && reference.Version < minimumApi
            ? new(DependencyProblemKind.SdkTooOld, reference, from, minimumApi)
            : null;

    private static DependencyProblem? FromShared(AssemblyReference reference, IReadOnlyDictionary<string, SharedFile> shared, string? from) =>
        shared.TryGetValue(reference.Name, out var file) && file.Version < reference.Version
            ? new(DependencyProblemKind.TooOld, reference, from, file.Version, file.Owner)
            : null;
}
