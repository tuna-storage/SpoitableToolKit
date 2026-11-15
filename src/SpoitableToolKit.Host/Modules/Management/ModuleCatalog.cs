using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Host.Config;
using SpoitableToolKit.Host.Modules.Files;
using SpoitableToolKit.Host.Modules.Management.Catalog;
using SpoitableToolKit.Host.Modules.Management.Models;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Push;

namespace SpoitableToolKit.Host.Modules.Management;

/// <summary>
/// モジュールの管理画面の中身（入っているもの・ON/OFF・外す・外から入れる・更新）。<b>変えたものは次の起動で効く</b>
/// （モジュールは後から外せない読み込み方で、ネイティブの dll は動いている間ファイルを掴む）。
/// <list type="bullet">
///   <item>外から入れる（GitHub のリリース・ソースからの build）: 調べて確かめる所から「入れますか」と聞く所までは、1 件ごとに <see cref="ModuleInstallJobs"/> が進める。
///         ここは、聞くときに見せる形（<see cref="Describe"/>）と、利用者が「入れる」を押したあと（<see cref="ConfirmInstall"/>。次の起動で入る）。
///         更新も同じ道（同じ Id の上に入れる）。<b>入れる・外すは後から押した方が通る</b>（外すと入れかけは取り消し、入れると外す印は消える）</item>
///   <item>更新があるか（<see cref="ModuleUpdates"/>）は確かめるだけ。入れるのは押したとき</item>
///   <item>ON/OFF は本体の設定 <see cref="HostConfig.DisabledModules"/>（Id の一覧）に書く。<c>.disabled</c> の印で止まっているものを ON にすると、印を消す（ユーザー側の置き場だけ）</item>
///   <item>外すのは<b>ユーザー側の置き場</b>（<c>%LOCALAPPDATA%\SpoitableToolKit\modules\&lt;Id&gt;</c>）のものだけ。同梱のものは OFF だけ。
///         外すと決めたフォルダには <see cref="RemoveMarker"/> を置き、次の起動の頭で <see cref="PendingRemovals.Apply"/> が片付ける</item>
///   <item>起動し直しが要るか（<see cref="RestartNeeded"/>）は、起動したときの状態と今の設定・印を比べて決める（覚えておかない）</item>
/// </list>
/// <para>
/// 行（<see cref="ModuleCatalogEntry"/>）を組み立てるのは <see cref="ModuleCatalogBuilder"/>。ここは、組み立てた行を見て受け付けることと、画面へ配ること。
/// </para>
/// </summary>
internal sealed class ModuleCatalog(
    ModuleManager modules, HostConfig config, ModuleConfigStore configs, UiBroadcast broadcast, ILocalizer text,
    ModuleInstaller installer, ModuleUpdates updates, string userRoot, string bundledRoot, ILogger<ModuleCatalog> logger)
{
    public const string RemoveMarker = ".remove";

    /// <summary>落として確かめるのに待つ長さ（256 MB まで落とすので、遅い回線の分を見ておく）。</summary>
    public static readonly TimeSpan StageLimit = TimeSpan.FromMinutes(10);

    private readonly Lock _gate = new();
    private readonly ModuleCatalogBuilder _builder = new(modules, config, text, installer, updates, userRoot, bundledRoot);
    public bool RestartNeeded
    {
        get
        {
            lock (_gate) return _builder.Build().Any(NeedsRestart);
        }
    }

    /// <summary>画面に渡す形（JSON にするのは <see cref="UiBroadcast.Json"/>）。</summary>
    public ModuleCatalogView Snapshot()
    {
        lock (_gate)
        {
            var entries = _builder.Build();
            return new ModuleCatalogView(
                RestartNeeded: entries.Any(NeedsRestart),
                UpdatesCheckedAt: updates.CheckedAt?.ToString("O"),
                Entries: [.. entries.Select(ModuleEntryView.From)]);
        }
    }

    /// <summary>
    /// 次の起動から読むか。<b>同梱のものも切れる</b>（読み取り専用の場所でも、本体の設定に書くので効く）。
    /// </summary>
    /// <returns>受け付けたか（無い Id・切り替えられないもの）。</returns>
    public bool SetEnabled(string id, bool on)
    {
        lock (_gate)
        {
            if (Find(id) is not { } entry || !entry.CanToggle) return false;

            var disabled = config.DisabledModules.Where(d => !string.Equals(d, entry.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (!on) disabled.Add(entry.Id);

            if (on && File.Exists(Path.Combine(entry.Directory, ".disabled")))
            {
                try
                {
                    File.Delete(Path.Combine(entry.Directory, ".disabled"));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "{Id}: could not remove the .disabled marker", entry.Id);
                    return false;
                }
            }

            config.DisabledModules = [.. disabled];
            configs.Save(HostConfig.Id);
            logger.LogInformation("{Id} will be {State} from the next start", entry.Id, on ? "loaded" : "skipped");
        }

        Announce();
        return true;
    }

    /// <summary>次の起動で外すか（<paramref name="remove"/> が false なら、外すのをやめる）。<b>ユーザー側の置き場のものだけ</b>。</summary>
    public bool SetRemoval(string id, bool remove)
    {
        lock (_gate)
        {
            if (Find(id) is not { } entry || !entry.CanRemove) return false;

            var marker = Path.Combine(entry.Directory, RemoveMarker);
            try
            {
                if (remove) File.WriteAllText(marker, string.Empty);
                else File.Delete(marker);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "{Id}: could not {Action} the removal marker", entry.Id, remove ? "write" : "delete");
                return false;
            }

            // 外すと決めたら、同じ Id の入れかけ（更新）は取り消す。残すと、外したはずのものが次の起動で新しい版になって入る。
            // **印を書けてから**（書けなかったのに入れかけだけ消えると、外れもせず更新も入らない）。
            if (remove) installer.CancelPending(entry.Id);

            logger.LogInformation("{Id} {Action}", entry.Id, remove ? "will be removed at the next start" : "will be kept");
        }

        Announce();
        return true;
    }

    /// <summary>
    /// 仮置きしたものを、画面に見せる形にする（リリースから落としたものも、ここで build したものも）。
    /// いま入っている同じ Id のものとの比べ（上に入る・同梱より優先される）は、<b>呼んだ時点のもの</b>。
    /// </summary>
    internal StagedModuleView Describe(StagedModule staged)
    {
        ModuleCatalogEntry? current;
        lock (_gate) current = Find(staged.Manifest.Id);
        var installed = current is { State: not "pending" };

        return new StagedModuleView(
            Token: staged.Token, Id: staged.Manifest.Id, Name: staged.Manifest.Name, Version: staged.Manifest.Version,
            Kind: staged.Kind, Repository: staged.Source.Repository, Tag: staged.Source.Tag, Asset: staged.Source.Asset,
            Sha256: staged.Source.Sha256,
            Built: staged.Source.Kind == ModuleSource.GitHubSourceKind,
            Replaces: installed, ReplacesVersion: installed ? current!.Version : null, ReplacesBundled: installed && current!.Bundled);
    }

    public bool ConfirmInstall(string token)
    {
        var source = installer.SourceOf(token);
        if (!installer.Confirm(token)) return false;

        // 入れる物を、その入れ元のいちばん新しいものとして覚え直す（次の起動で、前に確かめたときに見た物を「更新がある」と出さない）。
        if (source is not null) updates.Saw(source);
        Announce();
        return true;
    }

    public bool CancelPending(string id)
    {
        if (!installer.CancelPending(id)) return false;
        Announce();
        return true;
    }

    public async Task<ModuleUpdateCheck> CheckUpdatesAsync(bool force, CancellationToken cancellationToken)
    {
        var check = await updates.CheckAsync(force, cancellationToken).ConfigureAwait(false);
        Announce();
        return check;
    }

    /// <summary>
    /// 起動してしばらくしてから、自分から 1 回確かめる（設定で切れる。前に確かめてから 1 日経っていなければ何もしない）。
    /// 新しく見つかったら知らせる。<b>入れはしない</b>。
    /// </summary>
    public async Task CheckUpdatesAfterAsync(TimeSpan delay, INotificationManager notify, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            if (!config.CheckModuleUpdates) return;

            var found = (await CheckUpdatesAsync(force: false, cancellationToken).ConfigureAwait(false)).Found;
            if (found.Count > 0) notify.PushToast(NotifyKind.Info, text["host.modules.update.found"], string.Join(", ", found));
        }
        catch (OperationCanceledException)
        {
            // 閉じている途中。
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The module update check failed");
        }
    }

    private void Announce() => broadcast.Push("modulesChanged", Snapshot());

    private ModuleCatalogEntry? Find(string id) =>
        _builder.Build().FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    private static bool NeedsRestart(ModuleCatalogEntry entry) =>
        entry.Enabled != entry.EnabledAtStart || entry.Removing || entry.Pending is not null;
}
