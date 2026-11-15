using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Modules.Files;
using SpoitableToolKit.Host.Modules.Packaging;

namespace SpoitableToolKit.Host.Modules.Management;

/// <summary>
/// 起動の頭で、入れかけ（<c>.pending/&lt;Id&gt;</c>）を <c>modules/&lt;Id&gt;</c> へ入れ替える。前のものは <c>.trash</c> へ移す（片付けは <see cref="PendingRemovals"/>）。
/// 仮置き（<c>.staging</c>）の残りは消す（前のアプリが聞いている途中で閉じた）。
/// <b>ユーザー側が同梱の場所と同じフォルダなら何もしない</b>（別の綴りでも）。<see cref="PendingRemovals.Apply"/> より<b>先に</b>呼ぶ（<c>.trash</c> を空にするのは向こう）。
/// </summary>
internal static class PendingInstalls
{
    /// <summary>build の作業場 1 つの名前（16 進 8 桁。<see cref="ModuleInstaller.StageFromSourceAsync"/> が作る）。片付けは、この形のフォルダだけを消す。</summary>
    private static readonly System.Text.RegularExpressions.Regex WorkName =
        new(@"^[0-9a-f]{8}\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Apply(string userRoot, string bundledRoot, ILogger logger)
    {
        List<string> installed = [];
        var modules = Path.Combine(ModulePaths.Normalize(userRoot), "modules");
        if (ModulePaths.SameDirectory(userRoot, bundledRoot)
            || ModulePaths.SameDirectory(modules, Path.Combine(bundledRoot, "modules")))
            return installed;

        // リンクなら、消えるのはリンクだけ（先のフォルダは辿らない）。
        TryDelete(Path.Combine(modules, ModuleInstaller.StagingDirectoryName));

        // build の作業場の残り（前のアプリが build の途中で閉じた）。消すのは、自分が作った形の名前のフォルダだけ。
        var build = Path.Combine(ModulePaths.Normalize(userRoot), ModuleInstaller.BuildDirectoryName);
        if (Directory.Exists(build) && !ModulePaths.IsLink(build))
        {
            foreach (var work in Directory.GetDirectories(build).Where(directory => WorkName.IsMatch(Path.GetFileName(directory))))
                TryDelete(work);
        }

        var pending = Path.Combine(modules, ModuleInstaller.PendingDirectoryName);
        if (!Directory.Exists(pending)) return installed;

        var trash = Path.Combine(modules, PendingRemovals.TrashDirectoryName);
        if (ModulePaths.IsLink(pending) || ModulePaths.IsLink(trash))
        {
            logger.LogWarning("{Pending} or {Trash} is a link; not installing anything through it (delete the link to let installs work)", pending, trash);
            return installed;
        }

        foreach (var directory in Directory.GetDirectories(pending))
        {
            var id = Path.GetFileName(directory);
            if (!ModuleManifest.IsValidId(id) || ModulePaths.IsLink(directory)) continue;

            var target = Path.Combine(modules, id);
            string? previous = null;
            try
            {
                if (Directory.Exists(target))
                {
                    Directory.CreateDirectory(trash);
                    previous = Path.Combine(trash, $"{id}-{Guid.NewGuid():N}");
                    Directory.Move(target, previous);
                }

                Directory.Move(directory, target);
                installed.Add(id);
                logger.LogInformation("Installed the module {Id} ({Directory})", id, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not install the module {Id}; trying again at the next start", id);

                // 新しい版を置けなかった。**前の版を元の場所へ戻す** — 戻さないと、このあとの片付けが .trash ごと消して、何も入っていない状態になる。
                if (previous is null || !Directory.Exists(previous) || Directory.Exists(target)) continue;
                try
                {
                    Directory.Move(previous, target);
                }
                catch (Exception back) when (back is IOException or UnauthorizedAccessException)
                {
                    logger.LogError(back, "Could not put the previous {Id} back either; it was left in {Trash}", id, previous);
                }
            }
        }

        return installed;
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 次の起動の頭でまた消す。
        }
    }
}
