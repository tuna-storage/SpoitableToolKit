using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.App;
using SpoitableToolKit.Host.Settings;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace SpoitableToolKit.Host.Updates;

/// <summary>
/// 入れ元は公開の repo の GitHub Releases（<c>.github/workflows/</c> が作る）。
/// <list type="bullet">
///   <item>stable は「最新の Release」（<c>releases/latest/download/</c>）の <c>releases.stable.json</c> だけを読む。
///         Velopack の <see cref="GithubSource"/> は新しい順の 10 件しか見ないので、nightly（消さない）が 10 本出た時点で stable が見つからなくなる</item>
///   <item>nightly は <see cref="GithubSource"/> で、prerelease も含めた新しい順の 10 件から探す（stable を出した次の日も、nightly はその中にある）</item>
/// </list>
/// </summary>
internal sealed class VelopackUpdateSource : IAppUpdateSource
{
    public const string Repository = "https://github.com/tuna-storage/SpoitableToolKit";

    /// <summary>stable の入れ元。GitHub はこの下の名前を、最新の Release（prerelease でも下書きでもないもの）の同じ名前の物へ送る。</summary>
    public const string StableFeed = Repository + "/releases/latest/download/";

    /// <summary>e2e の印: このフォルダの <c>releases.&lt;channel&gt;.json</c> を読み、入れる代わりにここの <see cref="AppliedRecord"/> へ版を書く。</summary>
    public const string FeedVariable = "SPOITABLE_UPDATE_FEED";

    public const string AppliedRecord = "applied.txt";

    public static readonly TimeSpan CheckTimeout = TimeSpan.FromMinutes(1);

    /// <summary><c>.github/scripts/pack.ps1</c> の packId と同じ。</summary>
    private const string PackId = "SpoitableToolKit.App";

    private readonly IVelopackLocator? _locator;
    private readonly Func<UpdateChannel, IUpdateSource> _sources;
    private readonly string? _record;
    private readonly ILogger _logger;

    private VelopackUpdateSource(IVelopackLocator? locator, Func<UpdateChannel, IUpdateSource> sources, string? record, ILogger logger)
    {
        _locator = locator;
        _sources = sources;
        _record = record;
        _logger = logger;
    }

    /// <summary>インストールした版なら、<see cref="VelopackApp"/> が <c>Main</c> の頭で置いた物を使う。</summary>
    public static VelopackUpdateSource Create(string dataDirectory, ILogger logger)
    {
        var bridge = new VelopackLogBridge(logger);

        if (Environment.GetEnvironmentVariable(FeedVariable) is { Length: > 0 } feed)
        {
            var root = Path.Combine(dataDirectory, "update-e2e");
            Directory.CreateDirectory(Path.Combine(root, "packages"));
            var locator = new TestVelopackLocator(PackId, AppVersion.Text, Path.Combine(root, "packages"), AppContext.BaseDirectory, root,
                Path.Combine(root, "Update.exe"), channel: UpdateChannelNames.ToVelopack(UpdateChannel.Stable), logger: bridge);
            logger.LogInformation("{Variable} is set; app updates come from {Feed} and are recorded instead of installed", FeedVariable, feed);
            return new VelopackUpdateSource(locator, _ => new SimpleFileSource(new DirectoryInfo(feed)), Path.Combine(feed, AppliedRecord), logger);
        }

        var current = VelopackLocator.IsCurrentSet ? VelopackLocator.Current : null;
        current?.AddLogger(bridge);
        return new VelopackUpdateSource(current, SourceFor, null, logger);
    }

    /// <summary>
    /// Velopack が入れた形か（exe のフォルダの親に Update.exe がある。インストールした版も、持ち運ぶ版も）。
    /// <b>そうでなければ <see cref="VelopackApp"/> を呼ばない</b> — 呼ぶと Velopack は、入れた形でなくても利用者の
    /// <c>%LOCALAPPDATA%\velopack</c> へ記録を書く（開発の bin・e2e の起動のたびに）。
    /// </summary>
    public static bool IsVelopackLayout(string appDirectory) =>
        File.Exists(Path.Combine(appDirectory, "..", "Update.exe"));

    internal static IUpdateSource SourceFor(UpdateChannel channel) => channel == UpdateChannel.Nightly
        ? new GithubSource(Repository, accessToken: null, prerelease: true)
        : new SimpleWebSource(StableFeed);

    /// <summary>版が下がるのを許すのは、入っている版と違うチャンネルを選んだときだけ。同じチャンネルで下がった版は出さない。</summary>
    internal static UpdateOptions OptionsFor(UpdateChannel channel, UpdateChannel? installed) => new()
    {
        ExplicitChannel = UpdateChannelNames.ToVelopack(channel),
        AllowVersionDowngrade = installed is { } from && from != channel,
    };

    public bool IsInstalled => _locator?.CurrentlyInstalledVersion is not null;

    public string? InstalledVersion => _locator?.CurrentlyInstalledVersion?.ToString();

    public UpdateChannel? InstalledChannel => IsInstalled ? UpdateChannelNames.FromVelopack(_locator!.Channel) : null;

    public async Task<AppUpdateOffer?> CheckAsync(UpdateChannel channel, CancellationToken cancellationToken)
    {
        var manager = new UpdateManager(_sources(channel), OptionsFor(channel, InstalledChannel), _locator);

        // Velopack の確かめは止める口を持たず、繋がりが詰まると 30 分待つ。こちらで区切る（区切ったあとの待ちは捨てる）。
        var info = await manager.CheckForUpdatesAsync().WaitAsync(CheckTimeout, cancellationToken).ConfigureAwait(false);
        if (info?.TargetFullRelease is not { } target) return null;

        return new AppUpdateOffer(target.Version.ToString(), info.IsDowngrade, target.NotesMarkdown ?? string.Empty)
        {
            Ticket = new Ticket(manager, info),
        };
    }

    public Task DownloadAsync(AppUpdateOffer offer, Action<int> progress, CancellationToken cancellationToken)
    {
        var ticket = TicketOf(offer);
        return ticket.Manager.DownloadUpdatesAsync(ticket.Info, progress, cancellationToken);
    }

    public bool ApplyAfterExit(AppUpdateOffer offer)
    {
        var ticket = TicketOf(offer);

        if (_record is not null)
        {
            File.WriteAllText(_record, offer.Version);
            _logger.LogInformation("{Variable} is set; wrote {Version} to {File} instead of installing it", FeedVariable, offer.Version, _record);
            return false;
        }

        // Update.exe はこのアプリが抜けるのを 60 秒まで待ってから入れ、起動し直す。
        ticket.Manager.WaitExitThenApplyUpdates(ticket.Info.TargetFullRelease, silent: false, restart: true);
        return true;
    }

    private static Ticket TicketOf(AppUpdateOffer offer) =>
        offer.Ticket as Ticket ?? throw new ArgumentException("The offer did not come from this source", nameof(offer));

    private sealed record Ticket(UpdateManager Manager, UpdateInfo Info);
}
