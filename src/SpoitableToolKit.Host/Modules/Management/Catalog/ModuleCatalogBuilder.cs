using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Host.Modules.Files;
using SpoitableToolKit.Host.Modules.Loading;
using SpoitableToolKit.Host.Modules.Loading.Models;
using SpoitableToolKit.Host.Modules.Management.Models;
using SpoitableToolKit.Host.Settings;

namespace SpoitableToolKit.Host.Modules.Management.Catalog;

/// <summary>
/// 管理画面の行を組み立てる（読み込んだもの・ライブラリ・飛ばしたもの・次の起動で入るもの）。
/// 同梱か・外せるか・切り替えられるか・入れ元と更新は、<b>置き場と印と今の設定から毎回決める</b>。
/// </summary>
/// <param name="modules">読み込んだもの・ライブラリ・飛ばしたもの。</param>
/// <param name="config">本体の設定（OFF にしてある Id）。</param>
/// <param name="text">理由の文の訳。</param>
/// <param name="installer">次の起動で入るもの。</param>
/// <param name="updates">入れ元にある新しい版。</param>
/// <param name="userRoot">ユーザー側の置き場の根（その下の <c>modules</c> に並ぶ）。</param>
/// <param name="bundledRoot">同梱の場所の根。</param>
internal sealed class ModuleCatalogBuilder(
    ModuleManager modules, HostConfig config, ILocalizer text, ModuleInstaller installer, ModuleUpdates updates,
    string userRoot, string bundledRoot)
{
    private readonly string _userModules = Path.Combine(ModulePaths.Normalize(userRoot), "modules");
    private readonly string _bundledRoot = ModulePaths.Normalize(bundledRoot);

    /// <summary>
    /// ユーザー側の置き場が同梱の場所と<b>同じフォルダ</b>か（別の綴りでも: junction・subst のドライブ）。
    /// 同じなら、並ぶものは全部「同梱」で、何も外せない。
    /// </summary>
    private readonly bool _userIsBundled = ModulePaths.SameDirectory(userRoot, bundledRoot)
                                           || ModulePaths.SameDirectory(Path.Combine(userRoot, "modules"), Path.Combine(bundledRoot, "modules"));

    /// <summary>
    /// 今の行を全部作る。読み込んだもの → ライブラリ → 飛ばしたもの → 次の起動で入るもの の順。
    /// <b>呼ぶたびに今の設定と印から作る</b>（覚えておかない）。
    /// </summary>
    public List<ModuleCatalogEntry> Build()
    {
        List<ModuleCatalogEntry> entries = [];
        var shadowed = modules.Skipped.Where(s => s.Reason == SkipReason.Shadowed).Select(s => s.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var module in modules.Modules)
        {
            var (state, reason) = module.State switch
            {
                ModuleState.Failed => ("failed", text.Format("host.modules.reason.failed", module.Failure)),
                _ => ("running", (string?)null),
            };

            entries.Add(Make(module.Id, module.Instance.DisplayName, "module", module.Candidate.Directory, module.Candidate.Root,
                module.Candidate.EntryAssembly, state, reason, enabledAtStart: true, shadowed));
        }

        foreach (var library in modules.Libraries)
        {
            entries.Add(Make(library.Id, library.Id, "library", library.Directory, library.Root, entryAssembly: null,
                "running", reason: null, enabledAtStart: true, shadowed));
        }

        foreach (var skipped in modules.Skipped)
        {
            if (skipped.Reason == SkipReason.Shadowed) continue;
            if (entries.Any(e => string.Equals(e.Id, skipped.Id, StringComparison.OrdinalIgnoreCase))) continue;

            var (state, reason) = skipped.Reason switch
            {
                SkipReason.Disabled => ("off", (string?)null),
                SkipReason.MissingDependency when skipped.Problem is { } problem => ("refused", modules.Describe(problem)),
                SkipReason.NoEntry => ("broken", text.Format("host.modules.reason.noEntry", skipped.Id)),
                _ => ("broken", skipped.Detail),
            };

            var kind = IsLibraryFolder(skipped.Directory) ? "library" : "module";
            entries.Add(Make(skipped.Id, skipped.Id, kind, skipped.Directory, skipped.Root, EntryOf(skipped.Directory), state, reason,
                enabledAtStart: skipped.Reason != SkipReason.Disabled, shadowed));
        }

        // 次の起動で入るもの。もう並んでいる Id なら、その行に「次の起動でこの版になる」を足す。まだ無い Id は、入れかけの行を足す。
        foreach (var pending in installer.Pending())
        {
            var index = entries.FindIndex(e => string.Equals(e.Id, pending.Manifest.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                // 入れかけがあれば、外す印が残っていても外れない（次の起動の頭は入れる方が先で、印の付いた前の版ごと .trash へ移す）。
                // 印が残るのは、入れかけにしたときに消せなかったときだけ（外すと決めると、入れかけは取り消す）。
                // 入れ元は、次の起動で入る物のもの。動いている物のままにすると、別の入れ元から入れ直しても（ソースから Build した物の上にリリースを）
                // 行の開発元が次の起動まで前の commit のまま残る。
                var current = entries[index];
                entries[index] = current with
                {
                    Pending = pending.Manifest.Version, PendingCommit = CommitOf(pending.Source), Removing = false,
                    Source = pending.Source?.Repository ?? current.Source,
                    SourceTag = pending.Source?.Tag ?? current.SourceTag,
                    Built = pending.Source is { } source ? source.Kind == ModuleSource.GitHubSourceKind : current.Built,
                };
                continue;
            }

            entries.Add(new ModuleCatalogEntry(pending.Manifest.Id, pending.Manifest.Name, pending.Kind, pending.Manifest.Version, Bundled: false,
                State: "pending", Reason: null, Shared: [], Enabled: true, EnabledAtStart: true, Removing: false, CanRemove: false,
                CanToggle: false, Overrides: false, pending.Directory, pending.Source?.Repository, pending.Source?.Tag,
                Update: null, Pending: pending.Manifest.Version, Built: pending.Source?.Kind == ModuleSource.GitHubSourceKind,
                PendingCommit: CommitOf(pending.Source)));
        }

        return entries;
    }

    /// <summary>ソースから Build した物なら、その commit（次の起動で入る行に出す。ソースの更新は module.json の版が変わらないことが多い）。</summary>
    private static string? CommitOf(ModuleSource? source) => source is { Kind: ModuleSource.GitHubSourceKind } ? source.Tag : null;

    private ModuleCatalogEntry Make(string id, string name, string kind, string directory, string root, string? entryAssembly, string state,
        string? reason, bool enabledAtStart, HashSet<string> shadowed)
    {
        var bundled = _userIsBundled || string.Equals(ModulePaths.Normalize(root), _bundledRoot, StringComparison.OrdinalIgnoreCase);
        var marked = File.Exists(Path.Combine(directory, ".disabled"));
        var renamed = directory.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
        var enabled = !renamed && !marked
                      && !config.DisabledModules.Contains(id, StringComparer.OrdinalIgnoreCase);

        // 外せるのはユーザー側の置き場の中だけ（同梱の場所・置き場の外は、どの道を通っても消さない）。
        // ユーザー側が同梱の場所と同じフォルダなら、並ぶものは全部「同梱」になる（ここで外れる）。
        var canRemove = !bundled && ModulePaths.IsUnder(directory, _userModules);

        // 印で止まっているものは、印を消せる所（ユーザー側）でないと ON にできない。名前の後ろの .disabled は触らない。
        var canToggle = !renamed && (!marked || canRemove);

        var version = entryAssembly is not null && AssemblyMetadata.Read(entryAssembly) is { } metadata ? Short(metadata.Version) : null;
        var shared = modules.SharedFiles.Values
            .Where(f => string.Equals(f.Owner, id, StringComparison.OrdinalIgnoreCase))
            .Select(f => $"{f.Name} {Short(f.Version)}")
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // 入れ元は、外から入れたときに残したもの（ユーザー側の置き場だけ）。更新は、その入れ元に違うタグ（Build した物は違う先頭の commit）があるとき。
        var source = canRemove ? ModuleSource.ReadFrom(directory) : null;

        return new ModuleCatalogEntry(id, name, kind, version, bundled, state, reason, shared, enabled, enabledAtStart,
            Removing: canRemove && File.Exists(Path.Combine(directory, ModuleCatalog.RemoveMarker)), canRemove, canToggle,
            Overrides: !bundled && shadowed.Contains(id), directory,
            source?.Repository, source?.Tag, Update: source is not null ? updates.LatestFor(source) : null,
            Built: source?.Kind == ModuleSource.GitHubSourceKind);
    }

    private static bool IsLibraryFolder(string directory) =>
        Directory.Exists(directory) && Directory.GetFiles(directory, "*.dll").Length == 0;

    private static string? EntryOf(string directory) =>
        Directory.Exists(directory) ? ModuleDiscovery.FindEntryAssembly(directory) : null;

    /// <summary><c>2026.11.0.0</c> → <c>2026.11.0</c>（4 つ目が 0 なら落とす）。</summary>
    private static string Short(Version version) =>
        version.Revision > 0 ? version.ToString() : version.Build >= 0 ? version.ToString(3) : version.ToString(2);
}
