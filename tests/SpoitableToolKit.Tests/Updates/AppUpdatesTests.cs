using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Updates;
using SpoitableToolKit.Host.Updates.Models;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Updates;

public sealed class AppUpdatesTests
{
    private sealed class Rig
    {
        public FakeAppUpdateSource Source { get; } = new();

        public HostConfig Config { get; } = new();

        public Recording Broadcast { get; } = new();

        public ManualClock Clock { get; } = new();

        public AppUpdateSchedule Schedule { get; }

        public int Saves;

        public int Closes;

        public Rig() => Schedule = new AppUpdateSchedule(Clock, TimeSpan.Zero);

        public AppUpdates Start() =>
            new(Source, Config, () => Interlocked.Increment(ref Saves), Broadcast, Schedule, "2026.11.0", CancellationToken.None, NullLogger.Instance)
            {
                Close = () => Interlocked.Increment(ref Closes),
            };
    }

    private static Task<AppUpdateView> LookAsync(AppUpdates updates, bool manual = false) => updates.CheckAsync(manual, CancellationToken.None);

    private static async Task<AppUpdateView> SettledAsync(AppUpdates updates)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var view = updates.View();
            if (view.Phase is "restarting" or "installFailed") return view;
            await Task.Delay(10);
        }

        throw new TimeoutException($"the install did not finish: {updates.View().Phase}");
    }

    [Fact]
    public async Task AnAppThatIsNotInstalledDoesNotAskTheSourceEvenWhenPressed()
    {
        var rig = new Rig();
        rig.Source.IsInstalled = false;
        rig.Source.Next = FakeAppUpdateSource.Offer("2099.1.0");
        var updates = rig.Start();

        var view = await LookAsync(updates, manual: true);

        Assert.False(view.Installed);
        Assert.Equal("idle", view.Phase);
        Assert.False(view.Banner);
        Assert.Empty(rig.Source.Checked);
    }

    [Fact]
    public async Task AnAppThatIsNotInstalledDoesNotLookByItself()
    {
        var rig = new Rig();
        rig.Source.IsInstalled = false;

        var run = rig.Start().RunAsync(CancellationToken.None);

        Assert.True(run.IsCompletedSuccessfully);
        await run;
        Assert.Empty(rig.Source.Checked);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task TheAppLooksByItselfOnlyWhileTheSettingIsOn(bool enabled, int expected)
    {
        var rig = new Rig();
        rig.Config.CheckAppUpdates = enabled;
        using var stop = new CancellationTokenSource();

        var run = rig.Start().RunAsync(stop.Token);

        for (var attempt = 0; attempt < 200 && rig.Schedule.NextAt != rig.Clock.Now + AppUpdateSchedule.Interval; attempt++) await Task.Delay(10);
        Assert.Equal(rig.Clock.Now + AppUpdateSchedule.Interval, rig.Schedule.NextAt);

        await stop.CancelAsync();
        await run;
        Assert.Equal(expected, rig.Source.Checked.Count);
    }

    [Fact]
    public async Task ALookByHandPutsOffTheNextLookByItself()
    {
        var rig = new Rig();
        rig.Clock.Now += TimeSpan.FromMinutes(5);

        await LookAsync(rig.Start(), manual: true);

        Assert.Equal(rig.Clock.Now + AppUpdateSchedule.Interval, rig.Schedule.NextAt);
    }

    [Fact]
    public async Task AVersionFoundIsOfferedInTheBanner()
    {
        var rig = new Rig();
        rig.Source.Next = FakeAppUpdateSource.Offer("2099.1.0");
        var updates = rig.Start();

        var view = await LookAsync(updates);

        Assert.True(view.Installed);
        Assert.Equal("available", view.Phase);
        Assert.Equal("2099.1.0", view.Version);
        Assert.True(view.Banner);
        Assert.True(view.HasNotes);

        var pushes = rig.Broadcast.On(AppUpdates.Channel);
        Assert.Equal(view, Assert.IsType<AppUpdateView>(pushes[^1].Payload));
    }

    [Fact]
    public async Task NothingNewerMeansUpToDateAndNoBanner()
    {
        var rig = new Rig();
        var updates = rig.Start();

        var view = await LookAsync(updates);

        Assert.Equal("upToDate", view.Phase);
        Assert.Null(view.Version);
        Assert.False(view.Banner);
    }

    [Fact]
    public async Task TheSettingsChannelIsTheOneAsked()
    {
        var rig = new Rig();
        rig.Config.UpdateChannel = UpdateChannel.Nightly;

        await LookAsync(rig.Start());

        Assert.Equal([UpdateChannel.Nightly], rig.Source.Checked);
    }

    [Fact]
    public async Task ACheckThatGetsNoAnswerSaysSoButKeepsAVersionFoundBefore()
    {
        var rig = new Rig();
        var updates = rig.Start();

        rig.Source.CheckFails = true;
        Assert.Equal("checkFailed", (await LookAsync(updates)).Phase);

        rig.Source.CheckFails = false;
        rig.Source.Next = FakeAppUpdateSource.Offer("2099.1.0");
        await LookAsync(updates);
        rig.Source.CheckFails = true;
        var view = await LookAsync(updates);

        Assert.Equal("available", view.Phase);
        Assert.True(view.Banner);
    }

    /// <summary>
    /// 時間切れは失敗の 1 つ（HttpClient は時間切れを <see cref="TaskCanceledException"/> で知らせる）。閉じるときの取り消しと取り違えると、
    /// 「確かめています」のまま固まり、自分からの確かめも止まる。
    /// </summary>
    [Fact]
    public async Task ACheckThatTimesOutIsAFailureAndTheAppKeepsLooking()
    {
        var rig = new Rig();
        rig.Source.CheckThrows = new TaskCanceledException("the request timed out");
        using var stop = new CancellationTokenSource();
        var updates = rig.Start();

        var run = updates.RunAsync(stop.Token);
        for (var attempt = 0; attempt < 200 && updates.View().Phase != "checkFailed"; attempt++) await Task.Delay(10);

        Assert.Equal("checkFailed", updates.View().Phase);
        Assert.False(run.IsCompleted);

        rig.Source.CheckThrows = null;
        rig.Source.Next = FakeAppUpdateSource.Offer("2099.1.0");
        Assert.Equal("available", (await LookAsync(updates, manual: true)).Phase);

        await stop.CancelAsync();
        await run;
    }

    [Fact]
    public async Task AChannelChangedWhileLookingIsLookedAtOnceTheLookEnds()
    {
        var rig = new Rig();
        var gate = new TaskCompletionSource();
        rig.Source.CheckGate = gate;
        rig.Source.Next = FakeAppUpdateSource.Offer("2099.1.0");
        var updates = rig.Start();

        var first = LookAsync(updates);
        rig.Config.UpdateChannel = UpdateChannel.Nightly;
        var second = LookAsync(updates);
        gate.SetResult();
        await first;
        var view = await second;

        Assert.Equal([UpdateChannel.Stable, UpdateChannel.Nightly], rig.Source.Checked);
        Assert.Equal("available", view.Phase);
    }

    [Fact]
    public async Task SkippingRemembersTheVersionAndHidesTheBanner()
    {
        var rig = new Rig();
        rig.Source.Next = FakeAppUpdateSource.Offer("2099.1.0");
        var updates = rig.Start();
        await LookAsync(updates);

        var view = updates.Skip();

        Assert.False(view.Banner);
        Assert.Equal("2099.1.0", rig.Config.SkippedAppVersion);
        Assert.Equal(1, rig.Saves);
    }

    [Fact]
    public async Task ASkippedVersionIsNotOfferedAgainOnTheNextStart()
    {
        var rig = new Rig();
        rig.Config.SkippedAppVersion = "2099.1.0";
        rig.Source.Next = FakeAppUpdateSource.Offer("2099.1.0");

        var view = await LookAsync(rig.Start());

        Assert.Equal("available", view.Phase);
        Assert.False(view.Banner);
    }

    [Fact]
    public async Task AVersionNewerThanTheSkippedOneIsOffered()
    {
        var rig = new Rig();
        rig.Config.SkippedAppVersion = "2099.1.0";
        rig.Source.Next = FakeAppUpdateSource.Offer("2099.2.0");

        Assert.True((await LookAsync(rig.Start())).Banner);
    }

    [Fact]
    public async Task CheckingByHandOffersASkippedVersionAgain()
    {
        var rig = new Rig();
        rig.Config.SkippedAppVersion = "2099.1.0";
        rig.Source.Next = FakeAppUpdateSource.Offer("2099.1.0");

        var view = await LookAsync(rig.Start(), manual: true);

        Assert.True(view.Banner);
        Assert.Equal("", rig.Config.SkippedAppVersion);
        Assert.Equal(1, rig.Saves);
    }

    [Fact]
    public async Task NothingIsInstalledUntilUpdateIsPressed()
    {
        var rig = new Rig();
        rig.Source.Next = FakeAppUpdateSource.Offer("2099.1.0");
        var updates = rig.Start();

        await LookAsync(updates);
        await LookAsync(updates, manual: true);

        Assert.Empty(rig.Source.Downloaded);
        Assert.Empty(rig.Source.Applied);
        Assert.Equal(0, rig.Closes);
    }

    [Fact]
    public async Task UpdateDownloadsAsksForTheInstallAndClosesTheApp()
    {
        var rig = new Rig();
        rig.Source.Next = FakeAppUpdateSource.Offer("2099.1.0");
        var updates = rig.Start();
        await LookAsync(updates);

        Assert.Equal("downloading", updates.Install().Phase);
        var view = await SettledAsync(updates);

        Assert.Equal("restarting", view.Phase);
        Assert.True(view.Banner);
        Assert.Equal(["2099.1.0"], rig.Source.Downloaded);
        Assert.Equal(["2099.1.0"], rig.Source.Applied);
        Assert.Equal(1, rig.Closes);

        Assert.Contains(rig.Broadcast.On(AppUpdates.Channel), push => ((AppUpdateView)push.Payload).Percent == 40);
    }

    [Fact]
    public async Task TheAppStaysOpenWhenTheSourceDoesNotWantItClosed()
    {
        var rig = new Rig();
        rig.Source.Next = FakeAppUpdateSource.Offer("2099.1.0");
        rig.Source.CloseAfterApply = false;
        var updates = rig.Start();
        await LookAsync(updates);

        updates.Install();
        await SettledAsync(updates);

        Assert.Equal(["2099.1.0"], rig.Source.Applied);
        Assert.Equal(0, rig.Closes);
    }

    [Fact]
    public async Task AFailedDownloadSaysSoAndLeavesTheBannerToTryAgain()
    {
        var rig = new Rig();
        rig.Source.Next = FakeAppUpdateSource.Offer("2099.1.0");
        rig.Source.DownloadFails = true;
        var updates = rig.Start();
        await LookAsync(updates);

        updates.Install();
        var view = await SettledAsync(updates);

        Assert.Equal("installFailed", view.Phase);
        Assert.True(view.Banner);
        Assert.Empty(rig.Source.Applied);
        Assert.Equal(0, rig.Closes);
    }

    [Fact]
    public async Task ASkippedVersionBeingDownloadedStaysInTheBanner()
    {
        var rig = new Rig();
        rig.Source.Next = FakeAppUpdateSource.Offer("2099.1.0");
        rig.Source.CloseAfterApply = false;
        var updates = rig.Start();
        await LookAsync(updates);
        updates.Install();
        await SettledAsync(updates);

        Assert.True(updates.Skip().Banner);
        Assert.Equal("", rig.Config.SkippedAppVersion);
    }

    [Fact]
    public async Task ChangingTheChannelDropsTheVersionFoundAndRemembersTheChoice()
    {
        var rig = new Rig();
        rig.Source.Next = FakeAppUpdateSource.Offer("2099.1.0");
        rig.Config.CheckAppUpdates = false;
        var updates = rig.Start();
        await LookAsync(updates);

        rig.Config.UpdateChannel = UpdateChannel.Nightly;
        updates.OnConfigChanged(ConfigTesting.Changed(rig.Config, nameof(HostConfig.UpdateChannel)));

        var view = updates.View();
        Assert.Equal("idle", view.Phase);
        Assert.False(view.Banner);
        Assert.True(rig.Config.UpdateChannelChosen);
        Assert.Equal(1, rig.Saves);
    }

    [Fact]
    public void AnotherSettingDoesNotCountAsChoosingTheChannel()
    {
        var rig = new Rig();
        var updates = rig.Start();

        updates.OnConfigChanged(ConfigTesting.Changed(rig.Config, nameof(HostConfig.CheckAppUpdates)));

        Assert.False(rig.Config.UpdateChannelChosen);
        Assert.Equal(0, rig.Saves);
    }

    /// <summary>引数は名前で受ける（public な試験の引数に internal な enum は出せない）。</summary>
    [Theory]
    [InlineData(true, false, "Nightly", "Nightly")]
    [InlineData(true, true, "Nightly", "Stable")]
    [InlineData(false, false, "Nightly", "Stable")]
    [InlineData(true, false, null, "Stable")]
    public void TheChannelFollowsTheInstalledVersionUntilTheUserChoosesOne(bool installed, bool chosen, string? installedChannel, string expected)
    {
        var rig = new Rig();
        rig.Source.IsInstalled = installed;
        rig.Source.InstalledChannel = installedChannel is null ? null : Enum.Parse<UpdateChannel>(installedChannel);
        rig.Config.UpdateChannelChosen = chosen;

        rig.Start().FollowInstalledChannel();

        Assert.Equal(Enum.Parse<UpdateChannel>(expected), rig.Config.UpdateChannel);
        Assert.Equal(expected == "Stable" ? 0 : 1, rig.Saves);
    }

    /// <summary>
    /// 利用者が選んだチャンネルは、あとから組み立て直さない。nightly の版のまま起動し直しても、選んだ stable のまま
    /// （次の確かめで stable へ戻す版が出る）。
    /// </summary>
    [Fact]
    public void AChannelTheUserChoseStaysWhenTheNightlyStartsAgain()
    {
        var rig = new Rig();
        rig.Config.CheckAppUpdates = false;
        rig.Source.InstalledChannel = UpdateChannel.Nightly;
        var updates = rig.Start();
        updates.FollowInstalledChannel();
        Assert.Equal(UpdateChannel.Nightly, rig.Config.UpdateChannel);

        rig.Config.UpdateChannel = UpdateChannel.Stable;
        updates.OnConfigChanged(ConfigTesting.Changed(rig.Config, nameof(HostConfig.UpdateChannel)));
        rig.Start().FollowInstalledChannel();

        Assert.Equal(UpdateChannel.Stable, rig.Config.UpdateChannel);
        Assert.True(rig.Config.UpdateChannelChosen);
    }

    [Fact]
    public async Task ADowngradeIsOfferedAsASwitch()
    {
        var rig = new Rig();
        rig.Source.Next = FakeAppUpdateSource.Offer("2026.11.0", downgrade: true);

        var view = await LookAsync(rig.Start());

        Assert.True(view.Downgrade);
        Assert.True(view.Banner);
    }

    [Fact]
    public async Task TheNotesAreThoseOfTheVersionFoundInTheScreensLanguage()
    {
        var rig = new Rig();
        rig.Source.Next = FakeAppUpdateSource.Offer("2099.1.0", notes: "<!-- lang: ja -->\n- 日本語\n<!-- lang: en -->\n- English\n");
        var updates = rig.Start();
        Assert.Null(updates.Notes("ja").Version);

        await LookAsync(updates);

        var notes = updates.Notes("ja");
        Assert.Equal("2099.1.0", notes.Version);
        Assert.Equal([new ReleaseNoteBlock("item", "日本語", 0)], notes.Blocks);
    }

    [Fact]
    public async Task TheCurrentVersionIsTheInstalledOneWhenThereIsOne()
    {
        var rig = new Rig();
        rig.Source.InstalledVersion = "2026.11.15-42.gabc1234";
        Assert.Equal("2026.11.15-42.gabc1234", (await LookAsync(rig.Start())).CurrentVersion);

        rig.Source.IsInstalled = false;
        rig.Source.InstalledVersion = null;
        Assert.Equal("2026.11.0", rig.Start().View().CurrentVersion);
    }
}
