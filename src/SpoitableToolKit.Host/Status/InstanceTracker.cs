using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Events.VrcLog;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.VRChat;
using SpoitableToolKit.Host.VrcLog;
using SpoitableToolKit.Host.VrcLog.Store;

namespace SpoitableToolKit.Host.Status;

/// <summary>
/// いまのワールド・人数・動画の再生回数を数えて、状態の口に流す（<c>host.world</c> / <c>host.people</c> / <c>host.video</c>）。
/// <para>
/// 起動したときは VRChat のログを末尾から読む（<see cref="VrcLogBus"/>）ので、入ったあとのワールドは流れてこない。
/// 最初の行が届いたときに記録（DB）から追いつく: 同じログファイルの最後の <see cref="WorldJoined"/> と、そのあとの入室・退室を数え直す。
/// ログファイルが違えば（VRChat を起動し直した）分からないので出さない。
/// </para>
/// <para>
/// 人数は自分を含めた数（VRChat の入室は自分の分も出る）。抜けた行があると狂うので、0 を下回らないようにだけしてある。
/// </para>
/// </summary>
internal sealed class InstanceTracker : IVrcLogEventListener, IDisposable
{
    public const string WorldId = "host.world";
    public const string PeopleId = "host.people";
    public const string VideoId = "host.video";

    private readonly IVrcLogBus _bus;
    private readonly IVrcLogStore _store;
    private readonly ILocalizer _text;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();

    private string? _world;
    private string? _access;
    private DateTimeOffset? _since;
    private readonly HashSet<string> _present = new(StringComparer.Ordinal);
    private int _joins;
    private int _leaves;
    private int _plays;
    private int _failures;

    /// <summary>追いつきを始めたか。1 回だけ。</summary>
    private int _seeding;

    /// <summary>追いつきの最中に生のワールドが来たら、古い記録で上書きしない。</summary>
    private bool _liveWorld;

    public ModuleStatus World { get; } = new();

    public ModuleStatus People { get; } = new();

    public ModuleStatus Video { get; } = new();

    public InstanceTracker(IVrcLogBus bus, IVrcLogStore store, ILocalizer text, ILogger<InstanceTracker> logger)
    {
        _bus = bus;
        _store = store;
        _text = text;
        _logger = logger;

        bus.AddListener<IVrcLogEventListener>(HostOwner.Instance, this);
    }

    void IVrcLogEventListener.OnLogLine(VrcLogLine line) => OnLine(line);

    void IVrcLogEventListener.OnLogEvent(VrcLogEventParams e) => OnEvent(e);

    /// <summary>試験から、追いつきが済んだのを待つため。</summary>
    internal Task Seeded { get; private set; } = Task.CompletedTask;

    private void OnLine(VrcLogLine line)
    {
        if (Interlocked.Exchange(ref _seeding, 1) != 0) return;

        Seeded = SeedAsync(line.SourceFile);
    }

    private void OnEvent(VrcLogEventParams e)
    {
        lock (_gate)
        {
            if (e is WorldJoined) _liveWorld = true;

            Apply(e);
        }

        Publish();
    }

    /// <summary>
    /// 記録から追いつく。例外は投げない（数えられなくても、ログを読むのは止めない）。
    /// </summary>
    private async Task SeedAsync(string file)
    {
        try
        {
            var worlds = await _store.QueryRecentAsync(1, nameof(WorldJoined)).ConfigureAwait(false);
            if (worlds is not [var last] || last.SourceFile != file) return;

            var joined = await _store.QueryRecentAsync(1000, nameof(PlayerJoined)).ConfigureAwait(false);
            var left = await _store.QueryRecentAsync(1000, nameof(PlayerLeft)).ConfigureAwait(false);

            var since = joined.Concat(left)
                .Where(row => row.Id > last.Id && row.SourceFile == file)
                .OrderBy(row => row.Id)
                .Select(row => StoredEventCodec.Rebuild(row, _logger))
                .OfType<VrcLogEventParams>();

            lock (_gate)
            {
                if (_liveWorld) return;

                if (StoredEventCodec.Rebuild(last, _logger) is { } world) Apply(world);
                foreach (var e in since) Apply(e);
            }

            Publish();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not catch up on the current instance from the log store");
        }
    }

    /// <summary>1 件当てる。錠の中で呼ぶ。</summary>
    private void Apply(VrcLogEventParams e)
    {
        switch (e)
        {
            case WorldJoined world:
                _world = world.WorldName ?? world.WorldId;
                _access = world.AccessType;
                _since = world.Timestamp;
                _present.Clear();
                _joins = 0;
                _leaves = 0;
                break;

            case PlayerJoined joined:
                _joins++;
                _present.Add(joined.UserId ?? joined.DisplayName);
                break;

            case PlayerLeft left:
                _leaves++;
                _present.Remove(left.UserId ?? left.DisplayName);
                break;

            case VideoPlaybackStarted:
                _plays++;
                break;

            case VideoPlaybackFailed:
                _failures++;
                break;
        }
    }

    /// <summary>
    /// VRChat が終わった（<see cref="VRChatPresence"/>）。ワールド・人数・動画の札を消す。
    /// このあとに届いた行は、いつもどおり数える（見張りは最大 2 秒遅れるので、終わったと見た後の行も本物）。
    /// 走りかけの追いつき（<see cref="SeedAsync"/>）が後から前のワールドを戻さないよう、生のワールドが来た扱いにする。
    /// </summary>
    public void Reset()
    {
        lock (_gate)
        {
            _world = null;
            _access = null;
            _since = null;
            _present.Clear();
            _joins = 0;
            _leaves = 0;
            _plays = 0;
            _failures = 0;
            _liveWorld = true;
        }

        Publish();
    }

    /// <summary>
    /// 状態の口へ書く。同じ値なら Changed は上がらないので、毎回書いてよい。言語を変えたあとにも呼ぶ（文言だけ変わる）。
    /// 読むのと書くのを同じ錠の中でする。分けると、ログの行の Publish が消す前の値を読んで、消した（<see cref="Reset"/>）後に書き、
    /// 閉じた VRChat のワールドが札に戻る。
    /// </summary>
    public void Publish()
    {
        lock (_gate) PublishLocked();
    }

    private void PublishLocked()
    {
        string? world, access;
        DateTimeOffset? since;
        int people, joins, leaves, plays, failures;
        (world, access, since) = (_world, _access, _since);
        (people, joins, leaves, plays, failures) = (_present.Count, _joins, _leaves, _plays, _failures);

        World.Text = world ?? "";
        World.State = world is null ? StatusState.Off : StatusState.Ok;
        World.Note = world is null ? null
            : string.Join(" · ", new[] { access, since is { } at ? _text.Format("host.status.world.since", at.ToLocalTime().ToString("HH:mm")) : null }
                .Where(part => !string.IsNullOrEmpty(part)));

        People.Text = world is null ? "" : _text.Format("host.status.people", people);
        People.State = world is null ? StatusState.Off : StatusState.Ok;
        People.Note = world is null ? null : _text.Format("host.status.people.note", joins, leaves);

        Video.Text = plays + failures == 0 ? "" : _text.Format("host.status.video", plays);
        Video.State = failures > 0 ? StatusState.Warn : StatusState.Ok;
        Video.Note = failures > 0 ? _text.Format("host.status.video.note", failures) : null;
    }

    public void Dispose()
    {
        _bus.RemoveListener<IVrcLogEventListener>(HostOwner.Instance, this);
    }
}
