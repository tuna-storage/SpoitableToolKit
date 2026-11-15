using Microsoft.Data.Sqlite;
using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Status;
using SpoitableToolKit.Host.Ui.Shell;
using SpoitableToolKit.Host.VrcLog.Store;
using SpoitableToolKit.Tests.Fakes;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Shell;

/// <summary>ログを「記録する側」と「画面へ出す側」へ繋ぐ規則。</summary>
public sealed class LogRoutingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stk-routing", Guid.NewGuid().ToString("N"));

    public LogRoutingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static VrcLogLine Line(string message) => new()
    {
        Timestamp = new DateTimeOffset(2026, 9, 2, 19, 39, 0, TimeSpan.FromHours(9)),
        Level = VrcLogLevel.Log,
        Tag = null,
        Message = message,
        SourceFile = "output_log_2026-09-02_19-39-01.txt",
    };

    private sealed record Wired(
        FakeVrcLogBus Bus, SqliteVrcLogStore Store, UiPump Pump, Recording Sent) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Pump.DisposeAsync();
            await Store.DisposeAsync();
        }
    }

    private Wired Connect(bool storeAllLines, Func<bool>? storeApiRequests = null)
    {
        var sent = new Recording();
        var footer = new StatusBoard(new HostConfig(), sent);
        var pump = new UiPump(sent, footer);
        var store = new SqliteVrcLogStore(Path.Combine(_root, "vrclog.db"), new Logs());
        var bus = new FakeVrcLogBus();

        LogRouting.Connect(bus, store, pump, () => storeAllLines, storeApiRequests ?? (() => false));
        return new Wired(bus, store, pump, sent);
    }

    private static object[] Rows(Recording sent, string channel) =>
        sent.On(channel).SelectMany(message => (object[])message.Payload).ToArray();

    /// <summary>保存が入なら、生行は 1 回しか出ない。保存する道（書けてから id 付き）と保存しない道（直に、id なし）の両方を通ると同じ行が 2 回出る。片方には id が無いので、継ぎ目の重複弾きでも落とせない。</summary>
    [Fact]
    public async Task WithStoringOnARawLineAppearsOnlyOnce()
    {
        await using var wired = Connect(storeAllLines: true);

        wired.Bus.Emit(Line("生の行"));
        await wired.Store.FlushAsync();
        wired.Pump.Flush();

        var row = Assert.Single(Rows(wired.Sent, "logLines"));

        // 保存した行なので通し番号を持つ。持たないなら直の道を通ってしまっている。
        Assert.NotNull(Recording.Field(row, "id"));
    }

    /// <summary>保存が切なら、直に流す。そのセッション限りなので通し番号は無く、記録にも残らない（全件ログは 1 セッション 15,000 行を超える）。</summary>
    [Fact]
    public async Task WithStoringOffARawLineIsPushedButNotStored()
    {
        await using var wired = Connect(storeAllLines: false);

        wired.Bus.Emit(Line("生の行"));
        await wired.Store.FlushAsync();
        wired.Pump.Flush();

        var row = Assert.Single(Rows(wired.Sent, "logLines"));
        Assert.Null(Recording.Field(row, "id"));

        Assert.Empty(await wired.Store.QueryLinesAsync(
            DateTimeOffset.UnixEpoch, toExclusive: null, limit: 10));
    }

    /// <summary>イベントは常に保存し、書けてから画面へ流す。保存の設定に関わらず同じ（設定が効くのは生行だけ）。</summary>
    [Fact]
    public async Task AnEventIsPushedOnlyAfterItIsWritten()
    {
        await using var wired = Connect(storeAllLines: false);

        wired.Bus.Emit(new PlayerJoined
        {
            Line = Line("[Behaviour] OnPlayerJoined だれか"),
            DisplayName = "だれか",
        });

        await wired.Store.FlushAsync();
        wired.Pump.Flush();

        var row = Assert.Single(Rows(wired.Sent, "logEvents"));
        Assert.Equal(nameof(PlayerJoined), Recording.Field(row, "kind"));

        // 記録にも入っている。画面に出た行は必ず記録にある、がこの経路の約束。
        Assert.Single(await wired.Store.QueryEventsAsync(new EventPageQuery { Limit = 10 }));
    }

    private static ApiRequest Api(string endpoint) => new()
    {
        Line = Line($"[API] Requesting Get {endpoint}"),
        Method = "Get",
        Endpoint = endpoint,
    };

    private static PlayerJoined Joined(string name) => new()
    {
        Line = Line($"[Behaviour] OnPlayerJoined {name}"),
        DisplayName = name,
    };

    private static async Task SettleAsync(Wired wired)
    {
        await wired.Store.FlushAsync();
        wired.Pump.Flush();
    }

    /// <summary>API の呼び出しは、設定が切なら保存も表示もしない（既定 切。出来事の 95% を占めて記録を膨らませるため）。同じ流れに PlayerJoined も入れる。それは出る — 何も流れない壊れ方でも「API が無い」は成り立つので、見張りの行が要る。</summary>
    [Fact]
    public async Task WithApiStoringOffAnApiRequestIsNeitherStoredNorShown()
    {
        await using var wired = Connect(storeAllLines: false);

        wired.Bus.Emit(Api("users/usr_x"));
        wired.Bus.Emit(Joined("だれか"));
        await SettleAsync(wired);

        var row = Assert.Single(Rows(wired.Sent, "logEvents"));
        Assert.Equal(nameof(PlayerJoined), Recording.Field(row, "kind"));

        var stored = Assert.Single(await wired.Store.QueryEventsAsync(new EventPageQuery { Limit = 10 }));
        Assert.Equal(nameof(PlayerJoined), stored.Kind);
    }

    [Fact]
    public async Task WithApiStoringOnAnApiRequestIsStoredAndShown()
    {
        await using var wired = Connect(storeAllLines: false, storeApiRequests: () => true);

        wired.Bus.Emit(Api("users/usr_x"));
        await SettleAsync(wired);

        var row = Assert.Single(Rows(wired.Sent, "logEvents"));
        Assert.Equal(nameof(ApiRequest), Recording.Field(row, "kind"));
        Assert.Single(await wired.Store.QueryEventsAsync(new EventPageQuery { Limit = 10 }));
    }

    /// <summary>設定は出来事が来るたびに読む。切り替えたらすぐ効く（再起動は要らない）。繋いだときに 1 回だけ読むと、入にしても何も残らない。</summary>
    [Fact]
    public async Task TheApiSwitchIsReadForEveryEvent()
    {
        var on = false;
        await using var wired = Connect(storeAllLines: false, storeApiRequests: () => on);

        wired.Bus.Emit(Api("users/usr_before"));
        on = true;
        wired.Bus.Emit(Api("users/usr_after"));
        await SettleAsync(wired);

        var stored = Assert.Single(await wired.Store.QueryEventsAsync(new EventPageQuery { Limit = 10 }));
        Assert.Equal(nameof(ApiRequest), stored.Kind);
        Assert.Contains("usr_after", stored.Message);
    }

    private static VrcLogLine Tagged(string tag, string message) => Line($"[{tag}] {message}") with { Tag = tag };

    private static async Task<string[]> StoredLinesAsync(Wired wired) =>
        (await wired.Store.QueryLinesAsync(DateTimeOffset.UnixEpoch, toExclusive: null, limit: 10))
            .Select(row => row.Message).ToArray();

    private static string[] ShownLines(Wired wired) =>
        Rows(wired.Sent, "logLines").Select(row => (string)Recording.Field(row, "message")!).ToArray();

    /// <summary>API を残さないあいだは、<c>[API]</c> の生行も保存も表示もしない（生行を保存する・しないのどちらの道でも）。出来事にならない <c>[N] Sending Get …</c> も同じタグなので一緒に落ちる。見張りに別のタグの生行を流す — それは出る。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithApiStoringOffAnApiRawLineIsNeitherStoredNorShown(bool storeAllLines)
    {
        await using var wired = Connect(storeAllLines);

        wired.Bus.Emit(Tagged("API", "Requesting Get users/usr_x"));
        wired.Bus.Emit(Tagged("API", "[12] Sending Get request to https://api.vrchat.cloud/api/1/users/usr_x"));
        wired.Bus.Emit(Tagged("Behaviour", "OnPlayerJoined だれか"));
        await SettleAsync(wired);

        Assert.Equal(["[Behaviour] OnPlayerJoined だれか"], ShownLines(wired));
        Assert.Equal(storeAllLines ? ["[Behaviour] OnPlayerJoined だれか"] : [], await StoredLinesAsync(wired));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithApiStoringOnAnApiRawLineFlowsAsBefore(bool storeAllLines)
    {
        await using var wired = Connect(storeAllLines, storeApiRequests: () => true);

        wired.Bus.Emit(Tagged("API", "Requesting Get users/usr_x"));
        await SettleAsync(wired);

        Assert.Equal(["[API] Requesting Get users/usr_x"], ShownLines(wired));
        Assert.Equal(storeAllLines ? ["[API] Requesting Get users/usr_x"] : [], await StoredLinesAsync(wired));
    }

    /// <summary>生行の側も、設定は行が来るたびに読む。</summary>
    [Fact]
    public async Task TheApiSwitchIsReadForEveryRawLine()
    {
        var on = false;
        await using var wired = Connect(storeAllLines: false, storeApiRequests: () => on);

        wired.Bus.Emit(Tagged("API", "Requesting Get users/usr_before"));
        on = true;
        wired.Bus.Emit(Tagged("API", "Requesting Get users/usr_after"));
        await SettleAsync(wired);

        Assert.Equal(["[API] Requesting Get users/usr_after"], ShownLines(wired));
    }
}
