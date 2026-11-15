using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Modules.Hrs.Broadcast;
using SpoitableToolKit.Modules.Hrs.Config;
using SpoitableToolKit.Modules.Hrs.Recording;
using Xunit;

namespace SpoitableToolKit.Tests.Hrs.Recording;

/// <summary>心拍の記録と、帯（zone）のしきい値。記録は間引かずに 1 件ずつ入れる。</summary>
public sealed class HrsRecordsTests : IDisposable
{
    private readonly string _directory = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "stk-hrs-records", Guid.NewGuid().ToString("N"))).FullName;

    private HeartRateRecords New() => new(Path.Combine(_directory, "records.db"), NullLogger.Instance);

    private static readonly DateTimeOffset Noon = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 消せなくても試験の結果は変わらない（次の実行は別のディレクトリを作る）。
        }
    }

    /// <summary>間引かないので、1 秒に満たない間隔で来ても全部残る（時計とベルトが乗り換わる境目では、実際に詰まって届く）。</summary>
    [Fact]
    public async Task EveryBeatThatArrivesIsStored()
    {
        await using var records = New();

        for (var i = 0; i < 10; i++) records.Write(70 + i, Noon.AddMilliseconds(i * 100));

        await records.FlushAsync();
        var summary = await records.SummaryAsync();

        Assert.Equal(10, summary.Count);
        Assert.Equal(Noon, summary.First);
        Assert.Equal(Noon.AddMilliseconds(900), summary.Last);
    }

    /// <summary>指定より外を混ぜると、切り出した CSV が別の日のものになる。</summary>
    [Fact]
    public async Task BeatsOutsideTheRangeAreNotExported()
    {
        await using var records = New();

        records.Write(60, Noon.AddHours(-2));
        records.Write(80, Noon);
        records.Write(100, Noon.AddHours(2));

        var path = Path.Combine(_directory, "range.csv");
        var rows = await records.ExportAsync(Noon.AddMinutes(-1), Noon.AddMinutes(1), path);

        Assert.Equal(1, rows);

        var lines = await File.ReadAllLinesAsync(path);

        Assert.Equal("timestamp,bpm", lines[0]);
        Assert.Equal(2, lines.Length);
        Assert.EndsWith(",80", lines[1]);
    }

    /// <summary>書き出す前に溜まっているぶんを入れる（「いま」まで指定したのに直前の数拍が無いと事故に見える）。書き込みスレッドを足止めしてから書き出す。足止めしないと書き込みが先回りして、待たない実装でも通ってしまう。</summary>
    [Fact]
    public async Task TheBeatJustBeforeExportIsIncluded()
    {
        await using var records = New();

        var held = new TaskCompletionSource();
        records.BeforeAppend = _ => held.Task;

        for (var i = 0; i < 5; i++) records.Write(70 + i, Noon.AddMilliseconds(i));

        // Flush を挟まずにいきなり書き出す。
        var path = Path.Combine(_directory, "fresh.csv");
        var export = records.ExportAsync(Noon.AddMinutes(-1), Noon.AddMinutes(1), path);

        // 足止めしている間は書き出しも終わらない（溜まっているぶんを待っているため）。
        await Task.Delay(200);
        Assert.False(export.IsCompleted);

        held.SetResult();

        Assert.Equal(5, await export);
    }

    /// <summary>畳むときに残りを書く。落とすと記録の終わりが実際より早く見える。</summary>
    [Fact]
    public async Task ShuttingDownFlushesWhatIsLeft()
    {
        var records = New();
        records.Write(99, Noon);

        // Flush せずに閉じる。ここで捨てられていないか。
        await records.DisposeAsync();

        await using var reopened = New();
        var summary = await reopened.SummaryAsync();

        Assert.Equal(1, summary.Count);
    }

    /// <summary>範囲の両端を含み、古い順に返す（届いた順ではない — 乗り換えの境目では時計の分が後から届くことがある）。0 bpm（計れていない）も混ぜて返す。</summary>
    [Fact]
    public async Task ReadingGivesTheRangeOldestFirstWithBothEnds()
    {
        await using var records = New();

        records.Write(60, Noon.AddHours(-2));
        records.Write(81, Noon.AddMinutes(1));
        records.Write(0, Noon);
        records.Write(79, Noon.AddMinutes(-1));
        records.Write(100, Noon.AddHours(2));

        var points = await records.ReadAsync(Noon.AddMinutes(-1), Noon.AddMinutes(1));

        Assert.Equal(
            [
                new RecordPoint(Noon.AddMinutes(-1).ToUnixTimeMilliseconds(), 79),
                new RecordPoint(Noon.ToUnixTimeMilliseconds(), 0),
                new RecordPoint(Noon.AddMinutes(1).ToUnixTimeMilliseconds(), 81),
            ],
            points);
    }

    /// <summary>読む前に溜まっているぶんを入れる（今日を開いて直前の数拍が線に無いと止まって見える）。足止めの仕方は <see cref="TheBeatJustBeforeExportIsIncluded"/> と同じ。</summary>
    [Fact]
    public async Task TheBeatJustBeforeReadingIsIncluded()
    {
        await using var records = New();

        var held = new TaskCompletionSource();
        records.BeforeAppend = _ => held.Task;

        for (var i = 0; i < 5; i++) records.Write(70 + i, Noon.AddMilliseconds(i));

        var read = records.ReadAsync(Noon.AddMinutes(-1), Noon.AddMinutes(1));

        await Task.Delay(200);
        Assert.False(read.IsCompleted);

        held.SetResult();

        Assert.Equal(5, (await read).Count);
    }

    [Fact]
    public async Task TheCountCanBeReadWhenEmpty()
    {
        await using var records = New();

        Assert.Equal(0, (await records.SummaryAsync()).Count);
    }

    /// <summary>境目はその値を含む（<c>&gt;=</c>）。</summary>
    [Theory]
    [InlineData(60, "rest")]
    [InlineData(99, "rest")]
    [InlineData(100, "light")]
    [InlineData(119, "light")]
    [InlineData(120, "moderate")]
    [InlineData(139, "moderate")]
    [InlineData(140, "hard")]
    [InlineData(200, "hard")]
    public void ZonesSplitAtTheirThresholds(int bpm, string expected) =>
        Assert.Equal(expected, HrsZones.Of(new HrsConfig(), bpm));

    /// <summary>しきい値は動かせる。安静時の心拍は人によって 20 以上違うので、決め打ちだと座っているだけで light に居続ける人が出る。</summary>
    [Fact]
    public void ThresholdsCanBeMoved()
    {
        var config = new HrsConfig { Zones = { LightBpm = 70, ModerateBpm = 90, HardBpm = 110 } };

        Assert.Equal("rest", HrsZones.Of(config, 69));
        Assert.Equal("light", HrsZones.Of(config, 70));
        Assert.Equal("moderate", HrsZones.Of(config, 90));
        Assert.Equal("hard", HrsZones.Of(config, 110));
    }

    /// <summary>順番が崩れていたら理由を返す。moderate ≧ hard なら、hard の条件を満たす心拍が必ず先に moderate で拾われ、上の帯へ二度と入れない。</summary>
    [Fact]
    public void OutOfOrderThresholdsReturnAReason()
    {
        Assert.Null(HrsZones.ErrorIn(new HrsConfig()));

        Assert.NotNull(HrsZones.ErrorIn(new HrsConfig { Zones = { ModerateBpm = 150, HardBpm = 140 } }));
        Assert.NotNull(HrsZones.ErrorIn(new HrsConfig { Zones = { LightBpm = 130, ModerateBpm = 120 } }));

        // 全部同じ。境目が無いので light も moderate も出ない。
        Assert.NotNull(HrsZones.ErrorIn(new HrsConfig { Zones = { LightBpm = 100, ModerateBpm = 100, HardBpm = 100 } }));
    }

    /// <summary>却下の理由はキーだけ返す（文にするのは文言を持っているモジュール側の仕事。ここで日本語を組むと翻訳できない）。</summary>
    [Fact]
    public void TheRejectionReasonIsALocalizationKey() =>
        Assert.Equal("hrs.error.zoneOrder", HrsZones.ErrorIn(new HrsConfig { Zones = { HardBpm = 1 } }));
}
