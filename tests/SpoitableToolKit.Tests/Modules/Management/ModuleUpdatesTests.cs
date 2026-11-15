using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Modules.GitHub;
using SpoitableToolKit.Host.Modules.Management;
using SpoitableToolKit.Host.Modules.Management.Models;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Modules.Management;

public sealed class ModuleUpdatesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stk-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeGitHub _github = new();
    private readonly ManualClock _clock = new();

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private ModuleUpdates Updates() =>
        new(_root, new GitHubReleases(new HttpClient(_github), new Uri("http://127.0.0.1:1/")), _clock, NullLogger.Instance);

    /// <summary>GitHub から入れた、という跡を置く（<c>modules/&lt;id&gt;/.source.json</c>）。</summary>
    private ModuleSource Installed(string id, string tag, string repository = "someone/sample")
    {
        var directory = Path.Combine(_root, "modules", id);
        Directory.CreateDirectory(directory);
        var source = new ModuleSource(ModuleSource.GitHubKind, repository, tag, "sample-module.zip", "00", DateTimeOffset.UnixEpoch);
        source.WriteTo(directory);
        return source;
    }

    /// <summary>入っている commit（偽の GitHub の先頭の既定と同じ）と、先へ進んだ先頭。</summary>
    private const string InstalledCommit = "0123456789abcdef0123456789abcdef01234567";

    private const string NewerCommit = "89abcdef0123456789abcdef0123456789abcdef";

    /// <summary>ソースから Build して入れた、という跡を置く（タグの欄に commit）。</summary>
    private ModuleSource Built(string id, string commit, string repository = "someone/sample")
    {
        var directory = Path.Combine(_root, "modules", id);
        Directory.CreateDirectory(directory);
        var source = new ModuleSource(ModuleSource.GitHubSourceKind, repository, commit, string.Empty, "00", DateTimeOffset.UnixEpoch);
        source.WriteTo(directory);
        return source;
    }

    [Fact]
    public async Task ANewerReleaseOfAnInstalledModuleIsFoundAndRemembered()
    {
        var source = Installed("sample", "v1.0.0");
        var updates = Updates();
        Assert.Null(updates.LatestFor(source));
        Assert.Null(updates.CheckedAt);

        var check = await updates.CheckAsync(force: false, default);

        Assert.Equal(["someone/sample v1.2.0"], check.Found);
        Assert.True(check.Complete);
        Assert.Equal("v1.2.0", updates.LatestFor(source));
        Assert.Equal(_clock.Now, updates.CheckedAt);
        Assert.Contains(_github.Asked, u => u.AbsolutePath == "/repos/someone/sample/releases/latest");

        var later = Updates();
        Assert.Equal("v1.2.0", later.LatestFor(source));
        Assert.Equal(_clock.Now, later.CheckedAt);
    }

    /// <summary>ソースから Build して入れた物は、その repo の既定の branch の先頭の commit と比べる（commit には順番が無いので、違えば更新）。</summary>
    [Fact]
    public async Task AModuleBuiltFromSourceIsComparedWithTheHeadCommit()
    {
        var built = Built("built", InstalledCommit);
        var updates = Updates();

        Assert.Empty((await updates.CheckAsync(force: true, default)).Found);
        Assert.Null(updates.LatestFor(built));

        _github.Commit = NewerCommit;
        var check = await updates.CheckAsync(force: true, default);

        Assert.Equal(["someone/sample commit 89abcde"], check.Found);
        Assert.True(check.Complete);
        Assert.Equal(NewerCommit, updates.LatestFor(built));
        Assert.Equal(NewerCommit, Updates().LatestFor(built));
        Assert.Equal(2, _github.Asked.Count);
        Assert.All(_github.Asked, asked => Assert.Equal("/repos/someone/sample/commits/HEAD", asked.AbsolutePath));
    }

    /// <summary>同じ repo にリリースから入れた物とソースから Build した物があっても、それぞれ自分の相手とだけ比べる。片方が入っていなくなったら、その欄だけ捨てる。</summary>
    [Fact]
    public async Task ReleasesAndHeadCommitsAreKeptApart()
    {
        var released = Installed("sample", "v1.0.0");
        var built = Built("built", InstalledCommit);
        var updates = Updates();

        await updates.CheckAsync(force: true, default);

        Assert.Equal("v1.2.0", updates.LatestFor(released));
        Assert.Null(updates.LatestFor(built));
        Assert.Equal(1, _github.Asked.Count(asked => asked.AbsolutePath == "/repos/someone/sample/releases/latest"));
        Assert.Equal(1, _github.Asked.Count(asked => asked.AbsolutePath == "/repos/someone/sample/commits/HEAD"));

        Directory.Delete(Path.Combine(_root, "modules", "sample"), recursive: true);
        _github.Commit = NewerCommit;
        await updates.CheckAsync(force: true, default);

        Assert.Null(updates.LatestFor(released));
        Assert.Equal(NewerCommit, updates.LatestFor(built));
    }

    /// <summary>先頭の commit の知らせも 1 回だけ。繋がらなかったら、前に見た先頭のまま（「確かめられなかった」と分かる）。</summary>
    [Fact]
    public async Task AHeadCommitIsAnnouncedOnceAndKeptWhenGitHubDoesNotAnswer()
    {
        var built = Built("built", InstalledCommit);
        _github.Commit = NewerCommit;
        var updates = Updates();

        Assert.Equal(["someone/sample commit 89abcde"], (await updates.CheckAsync(force: true, default)).Found);
        Assert.Empty((await updates.CheckAsync(force: true, default)).Found);

        _github.CommitStatus = HttpStatusCode.InternalServerError;
        var check = await updates.CheckAsync(force: true, default);

        Assert.False(check.Complete);
        Assert.Equal(NewerCommit, updates.LatestFor(built));
    }

    /// <summary>インストールを決めた物は、その repo のいちばん新しいものとして覚え直す。commit には順番が無いので、覚え直さないと次の起動で違うだけで更新に見える。</summary>
    [Fact]
    public async Task WhatIsBeingInstalledIsRememberedAsTheNewest()
    {
        Installed("sample", "v1.0.0");
        Built("built", InstalledCommit, "someone/other");
        _github.Commit = NewerCommit;
        var updates = Updates();
        await updates.CheckAsync(force: true, default);

        var release = new ModuleSource(ModuleSource.GitHubKind, "someone/sample", "v1.3.0", "sample-module.zip", "00", DateTimeOffset.UnixEpoch);
        var head = new ModuleSource(ModuleSource.GitHubSourceKind, "someone/other", "fedcba9876543210fedcba9876543210fedcba98", string.Empty, "00",
            DateTimeOffset.UnixEpoch);
        updates.Saw(release);
        updates.Saw(head);

        Assert.Null(updates.LatestFor(release));
        Assert.Null(updates.LatestFor(head));
        var later = Updates();
        Assert.Null(later.LatestFor(release));
        Assert.Null(later.LatestFor(head));
    }

    /// <summary>先頭の commit の欄が無い <c>module-updates.json</c> も読める。</summary>
    [Fact]
    public void TheFileWrittenBeforeHeadCommitsWereKeptIsStillRead()
    {
        var released = Installed("sample", "v1.0.0");
        var built = Built("built", InstalledCommit);
        File.WriteAllText(Path.Combine(_root, ModuleUpdates.FileName),
            """{ "checkedAt": "2026-11-01T00:00:00+00:00", "latest": { "someone/sample": "v1.1.0" } }""");

        var updates = Updates();

        Assert.Equal("v1.1.0", updates.LatestFor(released));
        Assert.Null(updates.LatestFor(built));
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T00:00:00+00:00"), updates.CheckedAt);
    }

    [Fact]
    public async Task TheSameTagIsNotAnUpdate()
    {
        var source = Installed("sample", "v1.2.0");
        var updates = Updates();

        Assert.Empty((await updates.CheckAsync(force: false, default)).Found);
        Assert.Null(updates.LatestFor(source));
    }

    /// <summary>同じ更新を何度も知らせない（知らせるのは、新しく見つけたときだけ）。印は残る。</summary>
    [Fact]
    public async Task AnUpdateIsAnnouncedOnceButStaysMarked()
    {
        var source = Installed("sample", "v1.0.0");
        var updates = Updates();
        await updates.CheckAsync(force: true, default);

        Assert.Empty((await updates.CheckAsync(force: true, default)).Found);
        Assert.Equal("v1.2.0", updates.LatestFor(source));

        _github.Tag = "v1.3.0";
        var again = await updates.CheckAsync(force: true, default);
        Assert.Equal(["someone/sample v1.3.0"], again.Found);
    }

    /// <summary>自分から確かめるのは 1 日に 1 回まで。押したとき（force）は、いつでも確かめる。</summary>
    [Fact]
    public async Task ItAsksAtMostOnceADayUnlessToldTo()
    {
        Installed("sample", "v1.0.0");
        var updates = Updates();
        await updates.CheckAsync(force: false, default);
        Assert.Equal(1, _github.Releases);

        _clock.Now += TimeSpan.FromHours(23);
        await updates.CheckAsync(force: false, default);
        await Updates().CheckAsync(force: false, default);
        Assert.Equal(1, _github.Releases);

        await updates.CheckAsync(force: true, default);
        Assert.Equal(2, _github.Releases);

        _clock.Now += TimeSpan.FromHours(25);
        await updates.CheckAsync(force: false, default);
        Assert.Equal(3, _github.Releases);
    }

    /// <summary>繋がらなかったら「確かめた」ことにしない（次の起動でまた試す）。前に見たタグは残す。</summary>
    [Fact]
    public async Task WhenNothingCouldBeReachedItTriesAgainNextTime()
    {
        var source = Installed("sample", "v1.0.0");
        var updates = Updates();
        await updates.CheckAsync(force: false, default);
        var first = updates.CheckedAt;

        _clock.Now += TimeSpan.FromHours(25);
        _github.Offline = true;
        var offline = await updates.CheckAsync(force: false, default);
        Assert.Empty(offline.Found);
        Assert.False(offline.Complete, "繋がらなかったのに、全部確かめたことになっている");
        Assert.Equal(first, updates.CheckedAt);
        Assert.Equal("v1.2.0", updates.LatestFor(source));

        _github.Offline = false;
        _github.Tag = "v1.3.0";
        Assert.Equal(["someone/sample v1.3.0"], (await updates.CheckAsync(force: false, default)).Found);
    }

    /// <summary>GitHub の答えが JSON でなくても、確かめる処理は落ちない（その入れ元は「答えが返らなかった」になる）。</summary>
    [Fact]
    public async Task AnAnswerThatIsNotAReleaseCountsAsNotReached()
    {
        var source = Installed("sample", "v1.0.0");
        var updates = Updates();
        _github.Garbage = true;

        var check = await updates.CheckAsync(force: true, default);

        Assert.False(check.Complete);
        Assert.Empty(check.Found);
        Assert.Null(updates.LatestFor(source));
    }

    /// <summary>見込んでいない失敗でも、確かめる処理は投げない（その入れ元は「答えが返らなかった」になる）。</summary>
    [Fact]
    public async Task AnUnexpectedFailureCountsAsNotReached()
    {
        Installed("sample", "v1.0.0");
        _github.Throws = new InvalidCastException("something nobody planned for");

        var check = await Updates().CheckAsync(force: true, default);

        Assert.False(check.Complete);
        Assert.Empty(check.Found);
    }

    /// <summary>手で書き換えられて入れ元やタグが抜けた跡は、入れ元が分からないものとして扱う（null を持ったまま回さない）。</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "kind": "github", "repository": null, "tag": "v1" }""")]
    [InlineData("""{ "kind": "github", "repository": "someone/sample" }""")]
    [InlineData("not json")]
    public async Task ASourceFileWithoutARepositoryOrATagIsNotASource(string json)
    {
        var directory = Path.Combine(_root, "modules", "sample");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ModuleSource.FileName), json);

        Assert.Null(ModuleSource.ReadFrom(directory));
        Assert.True((await Updates().CheckAsync(force: true, default)).Complete);
        Assert.Empty(_github.Asked);
    }

    [Fact]
    public async Task WithNothingInstalledFromGitHubNothingIsAsked()
    {
        Directory.CreateDirectory(Path.Combine(_root, "modules", "byhand"));
        Directory.CreateDirectory(Path.Combine(_root, "modules", ".pending", "sample"));
        new ModuleSource(ModuleSource.GitHubKind, "someone/sample", "v1", "a.zip", "00", DateTimeOffset.UnixEpoch)
            .WriteTo(Path.Combine(_root, "modules", ".pending", "sample"));

        Assert.Empty((await Updates().CheckAsync(force: true, default)).Found);
        Assert.Empty(_github.Asked);
    }

    /// <summary>同じ repo から入れたものが 2 つあっても、聞くのは 1 回。外したものの分は忘れる。</summary>
    [Fact]
    public async Task ARepositoryIsAskedOnceAndForgottenWhenNothingFromItIsInstalled()
    {
        var first = Installed("one", "v1.0.0", "Someone/Sample");
        Installed("two", "v1.0.0", "someone/sample");
        var updates = Updates();

        await updates.CheckAsync(force: true, default);
        Assert.Equal(1, _github.Releases);
        Assert.Equal("v1.2.0", updates.LatestFor(first));

        Directory.Delete(Path.Combine(_root, "modules", "one"), recursive: true);
        Directory.Delete(Path.Combine(_root, "modules", "two"), recursive: true);
        await updates.CheckAsync(force: true, default);
        Assert.Null(updates.LatestFor(first));
    }
}
