using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.AppLog;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Status;
using SpoitableToolKit.Host.Ui.Shell;
using SpoitableToolKit.Host.VrcLog.Store;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Shell;

/// <summary>ログを画面へ流す前にまとめる段。ワールド遷移の瞬間は 1 秒で数百行流れるので、まとめる仕組み自体が本番の要件。</summary>
public sealed class UiPumpTests
{
    private static (UiPump Pump, Recording Sent) New(Action<long, long>? counted = null)
    {
        var sent = new Recording();
        var footer = new StatusBoard(new HostConfig(), sent);

        return (new UiPump(sent, footer, counted), sent);
    }

    private static VrcLogLine Line(string message) => new()
    {
        Timestamp = DateTimeOffset.Now,
        Level = VrcLogLevel.Log,
        Message = message,
        SourceFile = "output_log.txt",
    };

    private static AppLogEntry Entry(string message) =>
        new(DateTimeOffset.Now, LogLevel.Information, "SpoitableToolKit.Host.VrcLog.VrcLogBus", message, null, 12);

    [Fact]
    public async Task AppLogLinesReachTheScreen()
    {
        var (pump, sent) = New();
        await using var _ = pump;

        pump.Enqueue(Entry("tail を開始した") with { Seq = 41, Run = 1_731_212_405_000 });
        pump.Flush();

        var pushed = Assert.Single(sent.On("appLogs"));
        var rows = Assert.IsType<object[]>(pushed.Payload);
        var row = Assert.Single(rows);

        Assert.Equal("tail を開始した", Recording.Field(row, "message"));

        // 追いつき（getStatus）と同じ形にする。起動の印が push の行にだけ無いと、
        // 流れてきた行と前の起動の行の境目に「ここで起動」が出なくなる（画面は両側の印を比べる）。
        Assert.Equal(41L, Recording.Field(row, "seq"));
        Assert.Equal(1_731_212_405_000L, Recording.Field(row, "run"));
        Assert.Equal("INF", Recording.Field(row, "shortLevel"));

        // 画面はカテゴリの末尾だけを出す（完全名はファイルに残っている）。
        Assert.Equal("VrcLogBus", Recording.Field(row, "category"));
        Assert.Equal("SpoitableToolKit.Host.VrcLog.VrcLogBus", Recording.Field(row, "fullCategory"));
    }

    [Fact]
    public async Task NothingIsPushedWhenNothingIsQueued()
    {
        var (pump, sent) = New();
        await using var _ = pump;

        pump.Flush();

        // 200ms ごとに空の配列を投げると、画面は何も変わらないのに毎回叩かれる。
        Assert.Empty(sent.On("appLogs"));
    }

    [Fact]
    public async Task TheThreeLanesDoNotMix()
    {
        var (pump, sent) = New();
        await using var _ = pump;

        pump.Enqueue(Entry("アプリの行"));
        pump.Enqueue(Line("VRChat の生行"));
        pump.Flush();

        // 同じ 200ms に相乗りしているが、宛先の名前は別。混ざると画面が別の一覧へ描いてしまう。
        Assert.Single(sent.On("appLogs"));
        Assert.Single(sent.On("logLines"));
        Assert.Empty(sent.On("logEvents"));
    }

    [Fact]
    public async Task WhatOverflowsTheQueueIsDropped()
    {
        var (pump, sent) = New();
        await using var _ = pump;

        // 画面が見ていない間に際限なく溜めると、押し出したときに一気に数万行を投げることになる。
        for (var i = 0; i < 2500; i++) pump.Enqueue(Entry($"行 {i}"));
        pump.Flush();

        var rows = Assert.IsType<object[]>(Assert.Single(sent.On("appLogs")).Payload);
        Assert.Equal(2000, rows.Length);
    }

    [Fact]
    public async Task TheCountsReachTheFooter()
    {
        var counted = new List<(long Events, long Lines)>();
        var (pump, _) = New((e, l) => counted.Add((e, l)));
        await using var __ = pump;

        // 画面へ流れるのは書けたイベントだけ。id は DB が採番したもの。
        pump.Enqueue(new StoredEvent(7, new WorldJoined
        {
            Line = Line("join"),
            WorldId = "wrld_1",
            InstanceName = "12345",
            RawInstance = "12345~friends(usr_1)~region(jp)",
        }));
        pump.Flush();

        // 数えるのはここ（バッチが通る場所）。logBus にもう 1 つ購読を足すと順序が 2 つになる。
        Assert.Equal([(1L, 0L)], counted);
    }

    [Fact]
    public async Task AppLogLinesAreNotCounted()
    {
        var counted = new List<(long Events, long Lines)>();
        var (pump, _) = New((e, l) => counted.Add((e, l)));
        await using var __ = pump;

        pump.Enqueue(Entry("アプリの行"));
        pump.Flush();

        // フッターの「events / lines」は VRChat のログの話。
        // アプリログを混ぜると、VRChat を起動していないのに件数が増えて見える。
        Assert.Empty(counted);
    }
}
