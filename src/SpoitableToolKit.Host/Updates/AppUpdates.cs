using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Events.Config;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Push;
using SpoitableToolKit.Host.Updates.Models;

namespace SpoitableToolKit.Host.Updates;

internal sealed class AppUpdates(
    IAppUpdateSource source, HostConfig config, Action save, UiBroadcast broadcast, AppUpdateSchedule schedule,
    string appVersion, CancellationToken stopping, ILogger logger) : IConfigEventListener<HostConfig>
{
    public const string Channel = "appUpdate";

    /// <summary>まだ作られていない（画面が先に来た）ときの答え。確かめない。</summary>
    public static readonly AppUpdateView Unavailable = new(false, "", Name(AppUpdatePhase.Idle), null, false, false, null, false);

    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _looking = new(1, 1);
    private AppUpdatePhase _phase = AppUpdatePhase.Idle;
    private AppUpdateOffer? _offer;
    private int? _percent;

    /// <summary>窓が出来てから挿す。</summary>
    internal Action? Close;

    public AppUpdateView View()
    {
        lock (_gate) return ViewLocked();
    }

    public AppUpdateNotesView Notes(string language)
    {
        AppUpdateOffer? offer;
        lock (_gate) offer = _offer;
        return offer is null ? new AppUpdateNotesView(null, []) : new AppUpdateNotesView(offer.Version, ReleaseNotes.Read(offer.Notes, language));
    }

    /// <summary>
    /// 利用者がまだチャンネルを選んでいなければ、入っている版のチャンネルに合わせる
    /// （nightly の Setup で入れた人の設定が既定の stable のままだと、最初の確かめで stable へ下げる版を「新しいバージョン」として出してしまう）。
    /// </summary>
    public void FollowInstalledChannel()
    {
        if (!source.IsInstalled || config.UpdateChannelChosen) return;
        if (source.InstalledChannel is not { } installed || installed == config.UpdateChannel) return;

        logger.LogInformation("The update channel follows the installed version: {Before} -> {After}", config.UpdateChannel, installed);
        config.UpdateChannel = installed;
        save();
    }

    /// <summary>設定が切のときも時刻は数え続け、その回は確かめない（入にすれば、次の時刻から）。</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!source.IsInstalled) return;

        try
        {
            while (true)
            {
                var wait = schedule.Wait;
                if (wait > TimeSpan.Zero)
                {
                    // 起きたら測り直す（その間に「更新を確認」で確かめていれば、そこから数え直している）。
                    await Task.Delay(wait, schedule.Clock, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                schedule.Looked();
                if (config.CheckAppUpdates) await CheckAsync(manual: false, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 閉じている途中。
        }
    }

    /// <summary>
    /// 確かめる。<paramref name="manual"/> は「更新を確認」から（スキップした版も出し直す）。
    /// 確かめている最中にもう 1 回来たら、その終わりを待って同じ答えを返す（2 回繋がない）。
    /// </summary>
    public async Task<AppUpdateView> CheckAsync(bool manual, CancellationToken cancellationToken)
    {
        if (!source.IsInstalled) return View();

        if (!await _looking.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
        {
            await _looking.WaitAsync(cancellationToken).ConfigureAwait(false);
            _looking.Release();

            // 待っていた確かめの間にチャンネルが変わって、その答えを捨てていたら、新しいチャンネルで確かめ直す。
            return View().Phase == Name(AppUpdatePhase.Idle) ? await CheckAsync(manual, cancellationToken).ConfigureAwait(false) : View();
        }

        try
        {
            UpdateChannel channel;
            lock (_gate)
            {
                if (_phase is AppUpdatePhase.Downloading or AppUpdatePhase.Restarting) return ViewLocked();
                _phase = AppUpdatePhase.Checking;
                channel = config.UpdateChannel;
            }

            Push();

            AppUpdateOffer? found = null;
            var failed = false;
            try
            {
                found = await source.CheckAsync(channel, cancellationToken).ConfigureAwait(false);
            }
            // 時間切れも失敗（HttpClient は時間切れを TaskCanceledException で知らせる）。止めるのは閉じるときだけ。
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failed = true;
                logger.LogWarning("Could not check for a newer version of the app ({Channel}): {Reason}", channel, ex.Message);
            }

            schedule.Looked();

            var unskipped = false;
            lock (_gate)
            {
                if (channel != config.UpdateChannel)
                {
                    // 確かめている間にチャンネルが変わった。前のチャンネルの答えは出さない（変えた側が確かめ直す）。
                    _phase = AppUpdatePhase.Idle;
                    _offer = null;
                }
                else if (failed)
                {
                    // 前に見つけた版があれば、それを出したまま（繋がらなかっただけで、無くなってはいない）。
                    _phase = _offer is null ? AppUpdatePhase.CheckFailed : AppUpdatePhase.Available;
                }
                else
                {
                    _offer = found;
                    _phase = found is null ? AppUpdatePhase.UpToDate : AppUpdatePhase.Available;
                    if (manual && found is not null && string.Equals(config.SkippedAppVersion, found.Version, StringComparison.Ordinal))
                    {
                        config.SkippedAppVersion = string.Empty;
                        unskipped = true;
                    }
                }
            }

            if (unskipped) save();
            if (found is not null && !failed)
                logger.LogInformation("{Version} is available on the {Channel} channel{Downgrade}", found.Version, channel, found.Downgrade ? " (a downgrade)" : "");

            Push();
            return View();
        }
        finally
        {
            _looking.Release();
        }
    }

    public AppUpdateView Skip()
    {
        string version;
        lock (_gate)
        {
            if (_offer is null || _phase is AppUpdatePhase.Downloading or AppUpdatePhase.Restarting) return ViewLocked();
            version = _offer.Version;
            config.SkippedAppVersion = version;
        }

        save();
        logger.LogInformation("Skipped {Version}; the banner comes back with a newer version", version);
        Push();
        return View();
    }

    public AppUpdateView Install()
    {
        AppUpdateOffer offer;
        lock (_gate)
        {
            if (_offer is null || _phase is AppUpdatePhase.Downloading or AppUpdatePhase.Restarting or AppUpdatePhase.Checking) return ViewLocked();
            offer = _offer;
            _phase = AppUpdatePhase.Downloading;
            _percent = 0;
        }

        Push();
        _ = Task.Run(() => InstallAsync(offer), CancellationToken.None);
        return View();
    }

    private async Task InstallAsync(AppUpdateOffer offer)
    {
        try
        {
            logger.LogInformation("Downloading {Version}", offer.Version);
            await source.DownloadAsync(offer, percent =>
            {
                lock (_gate) _percent = Math.Clamp(percent, 0, 100);
                Push();
            }, stopping).ConfigureAwait(false);

            var close = source.ApplyAfterExit(offer);
            lock (_gate)
            {
                _phase = AppUpdatePhase.Restarting;
                _percent = null;
            }

            Push();
            logger.LogInformation("Downloaded {Version}; it is installed when the app closes, and the app starts again", offer.Version);
            if (close) Close?.Invoke();
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // 閉じている途中。
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not update to {Version}", offer.Version);
            lock (_gate)
            {
                _phase = AppUpdatePhase.InstallFailed;
                _percent = null;
            }

            Push();
        }
    }

    /// <summary>チャンネルを変えたら、利用者が選んだものとして覚える。</summary>
    public void OnConfigChanged(ConfigChangedEventParams<HostConfig> e)
    {
        if (!e.Changed(c => c.UpdateChannel)) return;

        lock (_gate)
        {
            config.UpdateChannelChosen = true;
            if (_phase is not (AppUpdatePhase.Downloading or AppUpdatePhase.Restarting))
            {
                _offer = null;
                _phase = AppUpdatePhase.Idle;
            }
        }

        save();
        Push();
        if (source.IsInstalled && config.CheckAppUpdates) _ = CheckAsync(manual: false, stopping);
    }

    private void Push() => broadcast.Push(Channel, View());

    private AppUpdateView ViewLocked()
    {
        var offer = _offer;
        var busy = _phase is AppUpdatePhase.Downloading or AppUpdatePhase.Restarting or AppUpdatePhase.InstallFailed;
        var banner = offer is not null && (busy || !string.Equals(config.SkippedAppVersion, offer.Version, StringComparison.Ordinal));

        return new AppUpdateView(
            source.IsInstalled,
            source.InstalledVersion ?? appVersion,
            Name(_phase),
            offer?.Version,
            offer?.Downgrade ?? false,
            banner,
            _phase == AppUpdatePhase.Downloading ? _percent : null,
            !string.IsNullOrWhiteSpace(offer?.Notes));
    }

    private static string Name(AppUpdatePhase phase) => JsonNamingPolicy.CamelCase.ConvertName(phase.ToString());
}
