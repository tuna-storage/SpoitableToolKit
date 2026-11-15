using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Modules.Files;

namespace SpoitableToolKit.Host.Modules.Management;

/// <summary>
/// 外すと決めたモジュール（<see cref="ModuleCatalog.RemoveMarker"/> の印）を、<b>起動の頭で</b>片付ける（まだ何も読み込んでいないので、ファイルは掴まれていない）。
/// <para>
/// 消すのは<b>ユーザー側の置き場の <c>modules/</c> の中だけ</b>。同梱の場所と同じフォルダなら何もしない（別の綴りでも）。
/// いったん <c>modules/.trash/</c> へ移してから消す（途中で消せなくても、半分消えたモジュールが次の起動で読まれない）。
/// <c>.trash</c> がリンクなら何もしない（リンクの先のフォルダを消してしまうので）。
/// 設定と記録（<c>module-data/&lt;Id&gt;</c>）は残す（入れ直したら戻る）。
/// </para>
/// </summary>
internal static class PendingRemovals
{
    public const string TrashDirectoryName = ".trash";

    public static IReadOnlyList<string> Apply(string userRoot, string bundledRoot, ILogger logger)
    {
        List<string> removed = [];
        var modules = Path.Combine(ModulePaths.Normalize(userRoot), "modules");
        if (ModulePaths.SameDirectory(userRoot, bundledRoot)
            || ModulePaths.SameDirectory(modules, Path.Combine(bundledRoot, "modules")))
            return removed;

        if (!Directory.Exists(modules)) return removed;

        var trash = Path.Combine(modules, TrashDirectoryName);
        if (ModulePaths.IsLink(trash))
        {
            logger.LogWarning("{Trash} is a link; not removing anything through it (delete the link to let removals work)", trash);
            return removed;
        }

        foreach (var directory in Directory.GetDirectories(modules))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith('.')) continue;
            if (!File.Exists(Path.Combine(directory, ModuleCatalog.RemoveMarker))) continue;
            if (!ModulePaths.IsUnder(directory, modules)) continue;

            try
            {
                Directory.CreateDirectory(trash);
                Directory.Move(directory, Path.Combine(trash, $"{name}-{Guid.NewGuid():N}"));
                removed.Add(name);
                logger.LogInformation("Removed the module {Id} ({Directory})", name, directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not remove the module {Id}; trying again at the next start", name);
            }
        }

        if (!Directory.Exists(trash)) return removed;

        foreach (var leftover in Directory.GetDirectories(trash))
        {
            try
            {
                Directory.Delete(leftover, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not clear {Directory}; trying again at the next start", leftover);
            }
        }

        return removed;
    }
}
