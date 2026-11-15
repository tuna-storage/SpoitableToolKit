using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Events.VrcLog;
using SpoitableToolKit.Host.Localization;
using SpoitableToolKit.Host.Status;
using SpoitableToolKit.Tests.Fakes;
using Xunit;

namespace SpoitableToolKit.Tests.Status;

/// <summary>
/// 本体が文字にして持っている状態（タイトルバーの「ログを読んでいます」・人数の札）を書き直すきっかけ（<see cref="StatusRouting"/>）。
/// </summary>
public sealed class StatusRoutingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stk-status-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>本体の辞書（日本語と英語で、タイトルバーと人数の文言だけ）。</summary>
    private LocaleSource HostDictionary()
    {
        var locales = Path.Combine(_root, "locales");
        Directory.CreateDirectory(locales);
        File.WriteAllText(Path.Combine(locales, "ja.json"), """
            { "host.status.log.reading": "読んでいます", "host.status.log.none": "見つかりません", "host.status.people": "{0} 人" }
            """);
        File.WriteAllText(Path.Combine(locales, "en.json"), """
            { "host.status.log.reading": "Reading", "host.status.log.none": "Not found", "host.status.people": "{0} people" }
            """);
        return new LocaleSource(_root, null);
    }

    private static WorldJoined World() => new()
    {
        Line = new VrcLogLine { Timestamp = DateTimeOffset.UnixEpoch, Level = VrcLogLevel.Log, Message = "x", SourceFile = "output_log.txt" },
        WorldId = "wrld_a",
        InstanceName = "1",
        WorldName = "Cozy",
        AccessType = "public",
        RawInstance = "1",
    };

    /// <summary>
    /// <b>言語を読み直したら、タイトルバーと札が新しい言語で書き直される</b>（次の出来事を待たない — VRChat が止まっていると来ない）。
    /// 本物の <see cref="Localizer"/> を読み直して、口から届くところまで通す。
    /// </summary>
    [Fact]
    public void ReloadingTheLanguageRewritesTheTitleAndTheChips()
    {
        var source = HostDictionary();
        var text = new Localizer(NullLogger<Localizer>.Instance);
        text.Load("ja", [source]);
        var bus = new FakeVrcLogBus();
        var instance = new InstanceTracker(bus, new FakeVrcLogStore(), text, NullLogger<InstanceTracker>.Instance);
        var reading = new LogReadingStatus(() => @"C:\logs\output_log.txt", text);
        var files = EventHubs.HostOnly<ILogFileEventListener>(NullLogger.Instance);
        StatusRouting.Connect(files.Source, text.Reloaded, reading, instance);
        bus.Emit(World());
        Assert.Equal("読んでいます", reading.Status.Text);
        Assert.Equal("0 人", instance.People.Text);

        text.Load("en", [source]);

        Assert.Equal("Reading", reading.Status.Text);
        Assert.Equal("0 people", instance.People.Text);
    }

    [Fact]
    public void AChangeOfLogFileRewritesTheTitle()
    {
        var text = new Localizer(NullLogger<Localizer>.Instance);
        text.Load("ja", [HostDictionary()]);
        string? current = null;
        var reading = new LogReadingStatus(() => current, text);
        var instance = new InstanceTracker(new FakeVrcLogBus(), new FakeVrcLogStore(), text, NullLogger<InstanceTracker>.Instance);
        var files = EventHubs.HostOnly<ILogFileEventListener>(NullLogger.Instance);
        StatusRouting.Connect(files.Source, text.Reloaded, reading, instance);
        Assert.Equal("見つかりません", reading.Status.Text);

        current = @"C:\logs\output_log_2026-11-13_21-00-00.txt";
        var found = new LogFileChangedEventParams(current);
        files.Fire<ILogFileEventListener>(l => l.OnLogFileChanged(found));

        Assert.Equal("読んでいます", reading.Status.Text);
        Assert.Equal("output_log_2026-11-13_21-00-00.txt", reading.Status.Note);

        current = null;
        var gone = new LogFileChangedEventParams(null);
        files.Fire<ILogFileEventListener>(l => l.OnLogFileChanged(gone));

        Assert.Equal("見つかりません", reading.Status.Text);
    }
}
