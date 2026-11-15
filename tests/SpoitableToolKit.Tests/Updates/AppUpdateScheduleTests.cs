using SpoitableToolKit.Host.Updates;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Updates;

public sealed class AppUpdateScheduleTests
{
    [Fact]
    public void TheFirstLookIsTwentySecondsAfterTheStart()
    {
        var clock = new ManualClock();
        var schedule = new AppUpdateSchedule(clock, AppUpdateSchedule.FirstDelay);

        Assert.Equal(TimeSpan.FromSeconds(20), AppUpdateSchedule.FirstDelay);
        Assert.Equal(TimeSpan.FromSeconds(20), schedule.Wait);

        clock.Now += TimeSpan.FromSeconds(19);
        Assert.Equal(TimeSpan.FromSeconds(1), schedule.Wait);

        clock.Now += TimeSpan.FromSeconds(5);
        Assert.Equal(TimeSpan.Zero, schedule.Wait);
    }

    [Fact]
    public void AfterALookTheNextIsSixHoursLater()
    {
        var clock = new ManualClock();
        var schedule = new AppUpdateSchedule(clock, AppUpdateSchedule.FirstDelay);
        clock.Now += TimeSpan.FromSeconds(20);

        schedule.Looked();

        Assert.Equal(TimeSpan.FromHours(6), AppUpdateSchedule.Interval);
        Assert.Equal(clock.Now + TimeSpan.FromHours(6), schedule.NextAt);
        clock.Now += TimeSpan.FromHours(6);
        Assert.Equal(TimeSpan.Zero, schedule.Wait);
    }

    [Fact]
    public void LookingByHandStartsTheCountAgain()
    {
        var clock = new ManualClock();
        var schedule = new AppUpdateSchedule(clock, AppUpdateSchedule.FirstDelay);
        clock.Now += TimeSpan.FromSeconds(20);
        schedule.Looked();

        clock.Now += TimeSpan.FromHours(5);
        schedule.Looked();

        Assert.Equal(TimeSpan.FromHours(6), schedule.Wait);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("3", 3)]
    [InlineData(null, 20)]
    [InlineData("", 20)]
    [InlineData("-1", 20)]
    [InlineData("soon", 20)]
    public void TheFirstDelayCanBeShortenedForTests(string? value, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), AppUpdateSchedule.FirstDelayFrom(value));
    }
}
