using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Theming;
using Xunit;

namespace SpoitableToolKit.Tests.Theming;

public sealed class HostThemesTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "stk-themes", Guid.NewGuid().ToString("N"))).FullName;

    private HostThemes New() => new(_dir, NullLogger<HostThemes>.Instance);

    private string File_(string name) => Path.Combine(_dir, "themes", name, "theme.css");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void TheBundledThemesAreWrittenOut()
    {
        New().Ensure();

        Assert.True(File.Exists(File_("dark")));
        Assert.True(File.Exists(File_("light")));

        Assert.StartsWith("/* stk-theme: bundled ", File.ReadAllText(File_("dark")), StringComparison.Ordinal);
    }

    /// <summary>毎起動で無条件に上書きすると、編集した同梱テーマが黙って消える。</summary>
    [Fact]
    public void AMatchingStampLeavesTheFileAlone()
    {
        New().Ensure();

        var edited = File.ReadAllText(File_("dark")) + "\n/* 手を入れた */\n";
        File.WriteAllText(File_("dark"), edited);

        New().Ensure();

        Assert.Equal(edited, File.ReadAllText(File_("dark")));
    }

    /// <summary>1 回だけ展開する形だと、更新しても古い同梱テーマが残る。</summary>
    [Fact]
    public void ADifferentStampReplacesTheFile()
    {
        New().Ensure();

        File.WriteAllText(File_("dark"), "/* stk-theme: bundled 00000000 */\n:root { --bg: #123456; }\n");

        New().Ensure();

        var after = File.ReadAllText(File_("dark"));
        Assert.DoesNotContain("#123456", after, StringComparison.Ordinal);
        Assert.Contains("--accent", after, StringComparison.Ordinal);
        Assert.Contains("--radius", after, StringComparison.Ordinal);
    }

    /// <summary>同梱の名前はこちらの持ち物という約束。</summary>
    [Fact]
    public void AMissingStampReplacesTheFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File_("light"))!);
        File.WriteAllText(File_("light"), ":root { --bg: #abcdef; }\n");

        New().Ensure();

        Assert.DoesNotContain("#abcdef", File.ReadAllText(File_("light")), StringComparison.Ordinal);
    }

    /// <summary>色の値は書かない（配色を直すたびに赤くなる）。</summary>
    [Fact]
    public void TheChosenThemeIsServed()
    {
        var themes = New();
        themes.Ensure();

        var dark = themes.Read("dark");
        var light = themes.Read("light");

        Assert.NotEqual(dark, light);
        Assert.Contains("color-scheme: dark", dark, StringComparison.Ordinal);
        Assert.Contains("color-scheme: light", light, StringComparison.Ordinal);
    }

    [Fact]
    public void AUserThemeIsServedToo()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File_("mine"))!);
        File.WriteAllText(File_("mine"), ":root { --bg: #010203; }\n");

        Assert.Contains("#010203", New().Read("mine"), StringComparison.Ordinal);
    }

    /// <summary>空を返すと色が全部消えた画面になるので、必ず既定へ落とす。</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    [InlineData(@"..\..\windows\system32")]
    public void AnUnreadableNameFallsBackToTheDefault(string? name)
    {
        var themes = New();
        themes.Ensure();

        Assert.Equal(themes.Read(HostThemes.Default), themes.Read(name));
    }

    [Fact]
    public void ServingWorksBeforeTheThemesAreWrittenOut()
    {
        Assert.Contains("--accent", New().Read("dark"), StringComparison.Ordinal);
    }

    [Fact]
    public void SuggestionsListBundledAndUserThemes()
    {
        var themes = New();
        themes.Ensure();

        Directory.CreateDirectory(Path.GetDirectoryName(File_("zebra"))!);
        File.WriteAllText(File_("zebra"), ":root { --bg: #000000; }\n");

        // theme.css が無いディレクトリは数えない（作りかけを候補に出さない）。
        Directory.CreateDirectory(Path.Combine(_dir, "themes", "empty"));

        Assert.Equal(["dark", "light", "zebra"], themes.Names());
    }
}
