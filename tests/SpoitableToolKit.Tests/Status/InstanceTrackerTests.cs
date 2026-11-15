using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.Status;
using SpoitableToolKit.Tests.Fakes;
using Xunit;

namespace SpoitableToolKit.Tests.Status;

/// <summary>
/// 起動したときは VRChat のログを末尾から読むので、入ったあとのワールドは流れてこない。最初の行が届いたときに記録（DB）から追いつく。
/// </summary>
public sealed class InstanceTrackerTests
{
    private const string LogFile = "output_log_2026-11-09_20-00-00.txt";
    private const string OtherFile = "output_log_2026-11-08_20-00-00.txt";

    private static readonly DateTimeOffset At = new(2026, 11, 9, 20, 0, 0, TimeSpan.FromHours(9));

    private static VrcLogLine Line(string file = LogFile) => new()
    {
        Timestamp = At,
        Level = VrcLogLevel.Log,
        Message = "x",
        SourceFile = file,
    };

    private static WorldJoined World(string id = "wrld_a", string? name = "Cozy", string? access = "public") => new()
    {
        Line = Line(),
        WorldId = id,
        InstanceName = "1",
        WorldName = name,
        AccessType = access,
        RawInstance = "1",
    };

    private static PlayerJoined Join(string name, string? userId = null) =>
        new() { Line = Line(), DisplayName = name, UserId = userId };

    private static PlayerLeft Leave(string name, string? userId = null) =>
        new() { Line = Line(), DisplayName = name, UserId = userId };

    private static VideoPlaybackStarted Played() => new() { Line = Line(), Url = "https://example.com/v" };

    private static VideoPlaybackFailed Failed() => new() { Line = Line(), Url = "https://example.com/v" };

    private static (InstanceTracker Tracker, FakeVrcLogBus Bus, StubStore Store) New()
    {
        var bus = new FakeVrcLogBus();
        var store = new StubStore();

        return (new InstanceTracker(bus, store, new FakeLocalizer(), NullLogger<InstanceTracker>.Instance), bus, store);
    }

    [Fact]
    public void ResettingClearsTheWorldThePeopleAndTheVideoCount()
    {
        var (tracker, bus, _) = New();
        bus.Emit(World(name: "Cozy Room"));
        bus.Emit(Join("A", "usr_a"));
        bus.Emit(Played());
        bus.Emit(Failed());

        tracker.Reset();

        Assert.Equal("", tracker.World.Text);
        Assert.Equal(StatusState.Off, tracker.World.State);
        Assert.Null(tracker.World.Note);
        Assert.Equal("", tracker.People.Text);
        Assert.Equal(StatusState.Off, tracker.People.State);
        Assert.Equal("", tracker.Video.Text);
        Assert.Null(tracker.Video.Note);
    }

    [Fact]
    public void AfterAResetTheNextWorldIsCountedFromScratch()
    {
        var (tracker, bus, _) = New();
        bus.Emit(World(name: "Cozy Room"));
        bus.Emit(Join("A", "usr_a"));

        tracker.Reset();
        bus.Emit(World(name: "Next Room"));
        bus.Emit(Join("B", "usr_b"));

        Assert.Equal("Next Room", tracker.World.Text);
        Assert.Equal("host.status.people(1)", tracker.People.Text);
    }

    [Fact]
    public void BeforeAnyWorldNothingIsShown()
    {
        var (tracker, bus, _) = New();

        // 入室の前に人の行が来ても、数は出さない（どのワールドの人数か分からない）。
        bus.Emit(Join("A", "usr_a"));

        Assert.Equal("", tracker.World.Text);
        Assert.Equal(StatusState.Off, tracker.World.State);
        Assert.Equal("", tracker.People.Text);
        Assert.Equal(StatusState.Off, tracker.People.State);
        Assert.Null(tracker.People.Note);
    }

    [Fact]
    public void JoiningAWorldShowsItsName()
    {
        var (tracker, bus, _) = New();

        bus.Emit(World(name: "Cozy Room"));

        Assert.Equal("Cozy Room", tracker.World.Text);
        Assert.Equal(StatusState.Ok, tracker.World.State);
    }

    [Fact]
    public void AWorldWithoutANameFallsBackToItsId()
    {
        var (tracker, bus, _) = New();

        bus.Emit(World(id: "wrld_1234", name: null));

        Assert.Equal("wrld_1234", tracker.World.Text);
    }

    [Fact]
    public void TheWorldNoteHasTheAccessTypeAndTheTimeJoined()
    {
        var (tracker, bus, _) = New();

        bus.Emit(World(access: "friends"));

        Assert.StartsWith("friends · host.status.world.since(", tracker.World.Note);
    }

    [Fact]
    public void TheWorldNoteOmitsAnUnknownAccessType()
    {
        var (tracker, bus, _) = New();

        bus.Emit(World(access: null));

        Assert.StartsWith("host.status.world.since(", tracker.World.Note);
    }

    [Fact]
    public void PeopleAreCountedAndTheNoteHasJoinsAndLeaves()
    {
        var (tracker, bus, _) = New();

        bus.Emit(World());
        bus.Emit(Join("A", "usr_a"));
        bus.Emit(Join("B", "usr_b"));
        bus.Emit(Join("C", "usr_c"));
        bus.Emit(Leave("B", "usr_b"));

        Assert.Equal("host.status.people(2)", tracker.People.Text);
        Assert.Equal(StatusState.Ok, tracker.People.State);
        Assert.Equal("host.status.people.note(3, 1)", tracker.People.Note);
    }

    /// <summary>同じ人の入室行が二度来ても（再接続）、人数は増えない。入退室の回数は素直に数える。</summary>
    [Fact]
    public void ThePeopleAreASetSoJoiningTwiceCountsOnce()
    {
        var (tracker, bus, _) = New();

        bus.Emit(World());
        bus.Emit(Join("A", "usr_a"));
        bus.Emit(Join("A", "usr_a"));

        Assert.Equal("host.status.people(1)", tracker.People.Text);
        Assert.Equal("host.status.people.note(2, 0)", tracker.People.Note);
    }

    [Fact]
    public void APersonIsIdentifiedByUserIdWhenThereIsOne()
    {
        var (tracker, bus, _) = New();

        bus.Emit(World());
        bus.Emit(Join("Old Name", "usr_a"));
        bus.Emit(Join("New Name", "usr_a"));
        bus.Emit(Join("Twin", "usr_b"));
        bus.Emit(Join("Twin", "usr_c"));

        Assert.Equal("host.status.people(3)", tracker.People.Text);
    }

    /// <summary>ID の無い行しか出さない VRChat の版でも、表示名で入りと出を合わせる。</summary>
    [Fact]
    public void WithoutAUserIdTheDisplayNameIdentifiesThePerson()
    {
        var (tracker, bus, _) = New();

        bus.Emit(World());
        bus.Emit(Join("A"));
        bus.Emit(Join("B"));
        bus.Emit(Leave("A"));

        Assert.Equal("host.status.people(1)", tracker.People.Text);
    }

    [Fact]
    public void ALeaveOfSomeoneUnseenDoesNotGoBelowZero()
    {
        var (tracker, bus, _) = New();

        bus.Emit(World());
        bus.Emit(Leave("Ghost", "usr_x"));

        Assert.Equal("host.status.people(0)", tracker.People.Text);
        Assert.Equal("host.status.people.note(0, 1)", tracker.People.Note);
    }

    [Fact]
    public void JoiningANewWorldStartsTheCountOver()
    {
        var (tracker, bus, _) = New();

        bus.Emit(World(name: "First"));
        bus.Emit(Join("A", "usr_a"));
        bus.Emit(Join("B", "usr_b"));
        bus.Emit(Leave("B", "usr_b"));

        bus.Emit(World(name: "Second"));

        Assert.Equal("Second", tracker.World.Text);
        Assert.Equal("host.status.people(0)", tracker.People.Text);
        Assert.Equal("host.status.people.note(0, 0)", tracker.People.Note);

        // 前のワールドの A が残っていたら、ここで 1 になってしまう。
        bus.Emit(Join("A", "usr_a"));
        Assert.Equal("host.status.people(1)", tracker.People.Text);
    }

    [Fact]
    public void VideoShowsNothingUntilSomethingPlaysOrFails()
    {
        var (tracker, bus, _) = New();

        bus.Emit(World());

        Assert.Equal("", tracker.Video.Text);
        Assert.Equal(StatusState.Ok, tracker.Video.State);
        Assert.Null(tracker.Video.Note);
    }

    [Fact]
    public void VideoCountsPlaysWithoutWarning()
    {
        var (tracker, bus, _) = New();

        bus.Emit(Played());
        bus.Emit(Played());

        Assert.Equal("host.status.video(2)", tracker.Video.Text);
        Assert.Equal(StatusState.Ok, tracker.Video.State);
        Assert.Null(tracker.Video.Note);
    }

    [Fact]
    public void AFailureWarnsAndSaysHowManyFailed()
    {
        var (tracker, bus, _) = New();

        bus.Emit(Played());
        bus.Emit(Failed());
        bus.Emit(Failed());

        Assert.Equal(StatusState.Warn, tracker.Video.State);
        Assert.Equal("host.status.video.note(2)", tracker.Video.Note);
        Assert.Equal("host.status.video(1)", tracker.Video.Text);
    }

    [Fact]
    public void AFailureAloneStillShowsTheCard()
    {
        var (tracker, bus, _) = New();

        bus.Emit(Failed());

        Assert.Equal("host.status.video(0)", tracker.Video.Text);
        Assert.Equal(StatusState.Warn, tracker.Video.State);
    }

    /// <summary>ワールドを移っても動画の数は積み上がる（このアプリの起動以来の数）。</summary>
    [Fact]
    public void VideoCountsSurviveChangingWorld()
    {
        var (tracker, bus, _) = New();

        bus.Emit(Played());
        bus.Emit(World());
        bus.Emit(Played());

        Assert.Equal("host.status.video(2)", tracker.Video.Text);
    }

    [Fact]
    public void AfterDisposingItStopsListening()
    {
        var (tracker, bus, store) = New();

        tracker.Dispose();
        bus.Emit(World());
        bus.Emit(Line());

        Assert.Equal("", tracker.World.Text);
        Assert.Equal(0, store.Queries);
    }

    [Fact]
    public async Task TheFirstLineCatchesUpFromTheStore()
    {
        var (tracker, bus, store) = New();
        store.Add(nameof(WorldJoined), LogFile, WorldPayload("Stored World", "friends"));
        store.Add(nameof(PlayerJoined), LogFile, PersonPayload("A", "usr_a"));
        store.Add(nameof(PlayerJoined), LogFile, PersonPayload("B", "usr_b"));
        store.Add(nameof(PlayerLeft), LogFile, PersonPayload("A", "usr_a"));

        bus.Emit(Line());
        await tracker.Seeded;

        Assert.Equal("Stored World", tracker.World.Text);
        Assert.StartsWith("friends · ", tracker.World.Note);
        Assert.Equal("host.status.people(1)", tracker.People.Text);
        Assert.Equal("host.status.people.note(2, 1)", tracker.People.Note);
    }

    [Fact]
    public async Task OnlyLaterRowsOfTheSameFileAreCounted()
    {
        var (tracker, bus, store) = New();
        store.Add(nameof(PlayerJoined), LogFile, PersonPayload("Before", "usr_before"));
        store.Add(nameof(WorldJoined), LogFile, WorldPayload("Now", "public"));
        store.Add(nameof(PlayerJoined), OtherFile, PersonPayload("Elsewhere", "usr_else"));
        store.Add(nameof(PlayerJoined), LogFile, PersonPayload("A", "usr_a"));

        bus.Emit(Line());
        await tracker.Seeded;

        Assert.Equal("host.status.people(1)", tracker.People.Text);
        Assert.Equal("host.status.people.note(1, 0)", tracker.People.Note);
    }

    /// <summary>最後のワールドが前のログファイルのものなら VRChat を起動し直しているので、古い部屋を「いま」として出さない。</summary>
    [Fact]
    public async Task AWorldFromAnotherLogFileIsNotShown()
    {
        var (tracker, bus, store) = New();
        store.Add(nameof(WorldJoined), OtherFile, WorldPayload("Yesterday", "public"));
        store.Add(nameof(PlayerJoined), OtherFile, PersonPayload("A", "usr_a"));

        bus.Emit(Line());
        await tracker.Seeded;

        Assert.Equal("", tracker.World.Text);
        Assert.Equal(StatusState.Off, tracker.World.State);
        Assert.Equal("", tracker.People.Text);
    }

    [Fact]
    public async Task WithNoStoredWorldNothingIsShown()
    {
        var (tracker, bus, _) = New();

        bus.Emit(Line());
        await tracker.Seeded;

        Assert.Equal("", tracker.World.Text);
        Assert.Equal(StatusState.Off, tracker.World.State);
    }

    /// <summary>追いつきは 1 回だけ。行が来るたびに記録を引き直すと、読むたびに DB が叩かれる。</summary>
    [Fact]
    public async Task OnlyTheFirstLineStartsCatchingUp()
    {
        var (tracker, bus, store) = New();
        store.Add(nameof(WorldJoined), LogFile, WorldPayload("Stored", "public"));

        bus.Emit(Line());
        var first = tracker.Seeded;
        await first;
        var queries = store.Queries;

        bus.Emit(Line());
        bus.Emit(Line(OtherFile));

        Assert.Same(first, tracker.Seeded);
        Assert.Equal(queries, store.Queries);
    }

    [Fact]
    public void NothingIsReadFromTheStoreBeforeALineArrives()
    {
        var (_, bus, store) = New();

        bus.Emit(World());

        Assert.Equal(0, store.Queries);
    }

    /// <summary>追いつきの最中に生のワールドが届いたら、古い記録で上書きしない。</summary>
    [Fact]
    public async Task AWorldThatArrivedLiveIsNotOverwrittenByTheStore()
    {
        var (tracker, bus, store) = New();
        store.Add(nameof(WorldJoined), LogFile, WorldPayload("Old Room", "public"));
        store.Add(nameof(PlayerJoined), LogFile, PersonPayload("Old", "usr_old"));
        var gate = store.HoldQueries();

        bus.Emit(Line());                                  // 追いつきが記録を待っている間に…
        bus.Emit(World(name: "New Room"));                 // …生のワールドが届く
        bus.Emit(Join("Fresh", "usr_fresh"));
        gate.SetResult();
        await tracker.Seeded;

        Assert.Equal("New Room", tracker.World.Text);
        Assert.Equal("host.status.people(1)", tracker.People.Text);
        Assert.Equal("host.status.people.note(1, 0)", tracker.People.Note);
    }

    /// <summary>VRChat が終わって札を消したあと、走りかけの追いつきが前のワールドを戻さない。</summary>
    [Fact]
    public async Task ACatchUpThatFinishesAfterAResetDoesNotBringTheOldWorldBack()
    {
        var (tracker, bus, store) = New();
        store.Add(nameof(WorldJoined), LogFile, WorldPayload("Old Room", "public"));
        var gate = store.HoldQueries();

        bus.Emit(Line());                                  // 追いつきが記録を待っている間に…
        tracker.Reset();                                   // …VRChat が終わる
        gate.SetResult();
        await tracker.Seeded;

        Assert.Equal("", tracker.World.Text);
        Assert.Equal(StatusState.Off, tracker.World.State);
    }

    [Fact]
    public async Task AFailingStoreDoesNotBreakTheLiveCount()
    {
        var (tracker, bus, store) = New();
        store.Fail = true;

        bus.Emit(Line());
        await tracker.Seeded;

        bus.Emit(World(name: "Live"));

        Assert.Equal("Live", tracker.World.Text);
    }

    [Fact]
    public async Task AnUnreadableStoredRowIsSkipped()
    {
        var (tracker, bus, store) = New();
        store.Add(nameof(WorldJoined), LogFile, WorldPayload("Stored", "public"));
        store.Add(nameof(PlayerJoined), LogFile, "{ これは JSON ではない");
        store.Add(nameof(PlayerJoined), LogFile, PersonPayload("A", "usr_a"));

        bus.Emit(Line());
        await tracker.Seeded;

        Assert.Equal("Stored", tracker.World.Text);
        Assert.Equal("host.status.people(1)", tracker.People.Text);
    }

    private static string WorldPayload(string name, string access) => new JsonObject
    {
        ["WorldId"] = "wrld_stored",
        ["InstanceName"] = "1",
        ["WorldName"] = name,
        ["AccessType"] = access,
        ["RawInstance"] = "1",
    }.ToJsonString();

    private static string PersonPayload(string name, string userId) => new JsonObject
    {
        ["DisplayName"] = name,
        ["UserId"] = userId,
    }.ToJsonString();

    /// <summary>記録を読む側の偽物。新しい順に返す（本物と同じ）。<see cref="HoldQueries"/> で最初の問い合わせを止められる。</summary>
    private sealed class StubStore : IVrcLogStore
    {
        private readonly List<StoredLogEvent> _rows = [];
        private TaskCompletionSource? _gate;
        private int _queries;

        public bool Fail { get; set; }

        public int Queries => Volatile.Read(ref _queries);

        public void Add(string kind, string file, string payload) => _rows.Add(new StoredLogEvent
        {
            Id = _rows.Count + 1,
            Timestamp = At,
            Kind = kind,
            Payload = payload,
            Message = "x",
            SourceFile = file,
        });

        public TaskCompletionSource HoldQueries() => _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<StoredLogEvent>> QueryRecentAsync(
            int limit, string? kind = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _queries);
            if (_gate is { } gate) await gate.Task;
            if (Fail) throw new InvalidOperationException("わざと");

            return [.. _rows.Where(r => kind is null || r.Kind == kind).OrderByDescending(r => r.Id).Take(limit)];
        }
    }
}
