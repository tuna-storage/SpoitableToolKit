using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Updates;
using Velopack.Sources;
using Xunit;

namespace SpoitableToolKit.Tests.Updates;

public sealed class VelopackUpdateSourceTests
{
    [Fact]
    public void StableReadsTheLatestReleaseOfThePublicRepository()
    {
        var source = Assert.IsType<SimpleWebSource>(VelopackUpdateSource.SourceFor(UpdateChannel.Stable));

        Assert.Equal("https://github.com/tuna-storage/SpoitableToolKit/releases/latest/download/", source.BaseUri.ToString());
    }

    [Fact]
    public void NightlyLooksThroughPrereleasesOfThePublicRepository()
    {
        var source = Assert.IsType<GithubSource>(VelopackUpdateSource.SourceFor(UpdateChannel.Nightly));

        Assert.True(source.Prerelease);
        Assert.Equal("https://github.com/tuna-storage/SpoitableToolKit", source.RepoUri.ToString().TrimEnd('/'));
    }

    /// <summary>引数は名前で受ける（public な試験の引数に internal な enum は出せない）。</summary>
    [Theory]
    [InlineData("Stable", "Stable", "stable", false)]
    [InlineData("Nightly", "Nightly", "nightly", false)]
    [InlineData("Stable", "Nightly", "stable", true)]
    [InlineData("Nightly", "Stable", "nightly", true)]
    [InlineData("Stable", null, "stable", false)]
    public void ADowngradeIsAllowedOnlyWhenTheChannelDiffersFromTheInstalledOne(string chosen, string? installed, string channel, bool downgrade)
    {
        var options = VelopackUpdateSource.OptionsFor(Enum.Parse<UpdateChannel>(chosen), installed is null ? null : Enum.Parse<UpdateChannel>(installed));

        Assert.Equal(channel, options.ExplicitChannel);
        Assert.Equal(downgrade, options.AllowVersionDowngrade);
    }

    /// <summary>開発の bin・e2e で Velopack を起こすと、利用者の <c>%LOCALAPPDATA%\velopack</c> へ起動のたびに記録が溜まる。</summary>
    [Fact]
    public void OnlyAnAppPutInPlaceByVelopackWakesIt()
    {
        var root = Directory.CreateTempSubdirectory("stk-velopack-");
        try
        {
            var current = Directory.CreateDirectory(Path.Combine(root.FullName, "current")).FullName;
            Assert.False(VelopackUpdateSource.IsVelopackLayout(current));
            Assert.False(VelopackUpdateSource.IsVelopackLayout(current + Path.DirectorySeparatorChar));

            File.WriteAllBytes(Path.Combine(root.FullName, "Update.exe"), []);
            Assert.True(VelopackUpdateSource.IsVelopackLayout(current));
            Assert.True(VelopackUpdateSource.IsVelopackLayout(current + Path.DirectorySeparatorChar));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("stable", "Stable")]
    [InlineData("nightly", "Nightly")]
    [InlineData("Nightly", "Nightly")]
    [InlineData("win", null)]
    [InlineData(null, null)]
    public void TheInstalledChannelIsReadFromItsName(string? name, string? expected)
    {
        Assert.Equal(expected is null ? null : Enum.Parse<UpdateChannel>(expected), UpdateChannelNames.FromVelopack(name));
    }
}
