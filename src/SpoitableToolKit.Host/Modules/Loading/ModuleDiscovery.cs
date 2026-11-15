using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.App;
using SpoitableToolKit.Host.Modules.Loading.Models;

namespace SpoitableToolKit.Host.Modules.Loading;

/// <summary>
/// 探索ルート配下の <c>modules/&lt;Id&gt;/</c> を洗い出す。読み込みはしない。
/// <para>
/// 1 つのフォルダが入れる単位。<b>共有の dll もそのフォルダの <c>shared/</c> に置く</b>（ルートの <c>shared/</c> は見ない）。
/// 入れる・外す・更新するのがフォルダ 1 つで済み、持ち主（持ってきたモジュール）がはっきりする。
/// <b>ここで読まないと決めたもの（OFF・入口が無い・影になった）の共有の dll は載らない</b>。参照が解けないと決まったものの分も載らない
/// （<see cref="ModuleDependencies.Resolve"/>）。
/// ⚠ <b>読み込んでみて落ちたものの分は、載ったまま</b>（dll が壊れている・名乗った Id がフォルダ名と違う・<c>OnInit</c> が false など）。
/// 共有は読み込みの前に決めて載せるので、あとからは外せない。管理画面では「読み込まれていない」と出るが、その <c>shared/</c> の dll はほかのモジュールに渡りうる。
/// </para>
/// </summary>
internal static class ModuleDiscovery
{
    public const string SharedDirectoryName = "shared";

    /// <summary>
    /// 無効化の判定。ディレクトリ内の <c>.disabled</c> マーカー、
    /// またはディレクトリ名が <c>.disabled</c> で終わること。
    /// <para>
    /// 同梱のモジュールは読み取り専用の場所に置かれうるのでマーカーを作れない。
    /// そちらは本体の設定 <c>DisabledModules</c>（Id の配列）で無効化する。
    /// </para>
    /// </summary>
    public static bool IsDisabled(string directory) =>
        directory.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
        || File.Exists(Path.Combine(directory, ".disabled"));

    /// <summary>
    /// 探索ルートを順に見て候補を返す。<b>同じ Id は先に見つかった方が勝つ</b>
    /// （<see cref="AppPaths.ModuleRoots"/> はユーザー → 同梱の順なので、ユーザー側が勝つ）。
    /// 影になった方は警告に出す。黙って古い方が動き続けるのが一番たちが悪い。
    /// <para>
    /// <b>候補の並びは「先に来た方」の順</b>（ルートの順、その中はフォルダ名の順）。共有の dll の版が同じときの勝ち負けにも使う。
    /// </para>
    /// </summary>
    public static ModuleDiscoveryResult Discover(IEnumerable<string> roots, IReadOnlySet<string> disabledIds, ILogger logger)
    {
        List<ModuleCandidate> found = [];
        List<SkippedModule> skipped = [];
        Dictionary<string, ModuleCandidate> byId = new(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            var modulesDirectory = Path.Combine(root, "modules");
            if (!Directory.Exists(modulesDirectory)) continue;

            foreach (var directory in Directory.GetDirectories(modulesDirectory).Order(StringComparer.OrdinalIgnoreCase))
            {
                var id = Path.GetFileName(directory);

                // `.` で始まるのは本体の作業場（外したものの置き場 .trash など）。モジュールではない。
                if (id.StartsWith('.')) continue;

                if (IsDisabled(directory))
                {
                    logger.LogInformation("Skipping {Id}: .disabled is present ({Directory})", id, directory);
                    skipped.Add(new(id, directory, root, SkipReason.Disabled));
                    continue;
                }

                if (disabledIds.Contains(id))
                {
                    logger.LogInformation("{Id} is disabled by the 'disabled modules' setting ({Directory})", id, directory);
                    skipped.Add(new(id, directory, root, SkipReason.Disabled));
                    continue;
                }

                ModuleCandidate candidate;
                if (FindEntryAssembly(directory) is { } entry)
                {
                    candidate = new(id, directory, entry, root);
                }
                else if (IsLibrary(directory))
                {
                    candidate = new(id, directory, EntryAssembly: null, root);
                }
                else
                {
                    logger.LogWarning("{Id}: no entry dll found (neither *.deps.json nor {Id}.dll)", id, id);
                    skipped.Add(new(id, directory, root, SkipReason.NoEntry));
                    continue;
                }

                if (byId.TryGetValue(id, out var winner))
                {
                    logger.LogWarning("{Id} exists in two places; using {Winner} and skipping {Loser}",
                        id, winner.Directory, directory);
                    skipped.Add(new(id, directory, root, SkipReason.Shadowed));
                    continue;
                }

                byId.Add(id, candidate);
                found.Add(candidate);
            }
        }

        return new(found, skipped);
    }

    /// <summary>
    /// <b>直下に dll が 1 つも無く、<c>shared/</c> に dll がある</b>ならライブラリ。
    /// 直下に dll があるのに入口が見つからないものは「読めなかったモジュール」で、黙ってライブラリとして読み替えない。
    /// </summary>
    private static bool IsLibrary(string directory)
    {
        var shared = Path.Combine(directory, SharedDirectoryName);
        return Directory.GetFiles(directory, "*.dll").Length == 0
               && Directory.Exists(shared)
               && Directory.GetFiles(shared, "*.dll").Length > 0;
    }

    /// <summary>
    /// 入り口の dll を決める。<c>*.deps.json</c> と同名の dll（ディレクトリ名とは無関係）、
    /// 無ければ <c>&lt;ディレクトリ名&gt;.dll</c>。
    /// </summary>
    internal static string? FindEntryAssembly(string directory)
    {
        foreach (var deps in Directory.GetFiles(directory, "*.deps.json"))
        {
            var dll = deps[..^".deps.json".Length] + ".dll";
            if (File.Exists(dll)) return dll;
        }

        var byDirectoryName = Path.Combine(directory, Path.GetFileName(directory) + ".dll");
        return File.Exists(byDirectoryName) ? byDirectoryName : null;
    }
}
