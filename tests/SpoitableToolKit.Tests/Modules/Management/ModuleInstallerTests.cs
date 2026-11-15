using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Modules.Build;
using SpoitableToolKit.Host.Modules.GitHub;
using SpoitableToolKit.Host.Modules.Management;
using SpoitableToolKit.Host.Modules.Management.Models;
using SpoitableToolKit.Host.Modules.Packaging;
using SpoitableToolKit.Tests.Helpers;
using Xunit;

namespace SpoitableToolKit.Tests.Modules.Management;

/// <summary>外からモジュールを入れる流れ（GitHub の URL・添付ファイル・zip の展開・module.json・仮置き → 確認 → 次の起動で入る）。本物の GitHub へは出ない（偽の HTTP を挿す）。</summary>
public sealed class ModuleInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stk-tests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _links = [];

    public void Dispose()
    {
        // リンクは先にリンクだけを外す（中を辿って消さない）。
        foreach (var link in _links)
        {
            try { if (Directory.Exists(link)) Directory.Delete(link); } catch (IOException) { }
        }

        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>junction を作る。先は必ずこの試験の置き場の中（守りが壊れていたら、リンクの先が本当に書き換わる）。</summary>
    private void Junction(string link, string target)
    {
        Assert.StartsWith(_root, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Directory.CreateDirectory(target);
        using var made = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        made.WaitForExit();
        Assert.True(Directory.Exists(link), $"junction を作れなかった: {made.StandardError.ReadToEnd()}");
        _links.Add(link);
    }

    private string User => Path.Combine(_root, "user");

    private string Modules => Path.Combine(User, "modules");

    [Theory]
    [InlineData("https://github.com/tuna-storage/hrs", "tuna-storage/hrs")]
    [InlineData("https://github.com/tuna-storage/hrs/", "tuna-storage/hrs")]
    [InlineData("https://github.com/tuna-storage/hrs.git", "tuna-storage/hrs")]
    [InlineData("https://github.com/tuna-storage/hrs/releases", "tuna-storage/hrs")]
    [InlineData("https://github.com/tuna-storage/hrs/releases/tag/v1", "tuna-storage/hrs")]
    [InlineData("  https://GitHub.com/a.b/c_d-e  ", "a.b/c_d-e")]
    public void AGitHubRepositoryUrlIsRead(string url, string expected)
    {
        Assert.Equal(expected, GitHubRepository.TryParse(url, out var problem)?.ToString());
        Assert.Null(problem);
    }

    [Theory]
    [InlineData("http://github.com/a/b", "https")]
    [InlineData("https://gitlab.com/a/b", "github.com")]
    [InlineData("https://github.com.evil.example/a/b", "github.com")]
    [InlineData("https://user@github.com/a/b", "github.com")]
    [InlineData("https://github.com:8443/a/b", "github.com")]
    [InlineData("https://github.com/a", "owner")]
    [InlineData("https://github.com/a/b/tree/main", "releases")]
    [InlineData("https://github.com/a/..", "owner")]           // .. は URL の側で解かれて、owner だけが残る
    [InlineData("https://github.com/a/b%2Fc", "characters")]
    [InlineData("file:///C:/a/b", "https")]
    [InlineData("-a/b", "URL")]
    public void AnythingElseIsRefusedWithAReason(string url, string reason)
    {
        Assert.Null(GitHubRepository.TryParse(url, out var problem));
        Assert.Contains(reason, problem);
    }

    private static ReleaseAsset Asset(string name) => new(name, new Uri("https://example.invalid/" + name), 10, null);

    [Fact]
    public void TheOnlyZipIsChosen()
    {
        var release = new GitHubRelease("v1", [Asset("notes.txt"), Asset("hrs-1.0.zip")]);
        Assert.Equal("hrs-1.0.zip", GitHubReleases.ChooseAsset(release).Name);
    }

    [Fact]
    public void AmongSeveralZipsTheOneNamedModuleIsChosen()
    {
        var release = new GitHubRelease("v1", [Asset("source.zip"), Asset("hrs-module.zip")]);
        Assert.Equal("hrs-module.zip", GitHubReleases.ChooseAsset(release).Name);
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("a.zip", "b.zip")]
    [InlineData("a-module.zip", "b-module.zip")]
    public void WhenTheZipIsNotClearNothingIsChosen(params string[] names)
    {
        var release = new GitHubRelease("v1", [.. names.Select(Asset)]);
        Assert.Throws<InvalidDataException>(() => GitHubReleases.ChooseAsset(release));
    }

    [Fact]
    public void AManifestNeedsAnIdANameAndAVersion()
    {
        var manifest = ModuleManifest.Parse("""{ "id": "hrs", "name": "Heart rate", "version": "2026.11.0" }""");
        Assert.Equal(new ModuleManifest("hrs", "Heart rate", "2026.11.0"), manifest);

        var missing = Assert.Throws<InvalidDataException>(() => ModuleManifest.Parse("""{ "id": "hrs", "name": "x" }"""));
        Assert.Contains("\"version\"", missing.Message);
        Assert.Contains("for example", missing.Message);
    }

    [Theory]
    [InlineData("Hrs")]
    [InlineData("../x")]
    [InlineData("a/b")]
    [InlineData(".hidden")]
    [InlineData("a..b")]
    [InlineData("")]
    [InlineData("x.")]          // Windows は終わりの . を落とす → 別のモジュール x のフォルダになる
    [InlineData("x-")]
    [InlineData("x\n")]         // $ は最後の改行の手前でも合う
    [InlineData("con")]
    [InlineData("nul.module")]
    [InlineData("com1")]
    public void AManifestIdThatCannotBeAFolderNameIsRefused(string id)
    {
        var json = JsonSerializer.Serialize(new { id, name = "x", version = "1" });
        Assert.Throws<InvalidDataException>(() => ModuleManifest.Parse(json));
    }

    private static MemoryStream Zip(params (string Name, string Content)[] entries) => FakeGitHub.ZipOf(entries);

    private const string Manifest = """{ "id": "sample", "name": "Sample", "version": "1.0.0" }""";

    [Fact]
    public void APackageWithTheManifestAtTheTopIsExtracted()
    {
        var (directory, manifest) = ModulePackage.Extract(Zip(("module.json", Manifest), ("sample.dll", "x"), ("web/index.html", "<p>")), _root);

        Assert.Equal("sample", manifest.Id);
        Assert.True(File.Exists(Path.Combine(directory, "web", "index.html")));
    }

    [Fact]
    public void APackageWithOneTopFolderIsExtractedFromInsideIt()
    {
        var (directory, manifest) = ModulePackage.Extract(Zip(("sample/module.json", Manifest), ("sample/sample.dll", "x")), _root);

        Assert.Equal("sample", manifest.Id);
        Assert.Equal("sample", Path.GetFileName(directory));
    }

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("a/../../evil.dll")]
    [InlineData("C:/Windows/evil.dll")]
    [InlineData("/evil.dll")]
    public void AnEntryPointingOutsideIsRefusedBeforeAnythingIsWritten(string name)
    {
        var staging = Path.Combine(_root, "staging");

        Assert.Throws<InvalidDataException>(() => ModulePackage.Extract(Zip(("module.json", Manifest), (name, "x")), staging));
        Assert.False(Directory.Exists(staging), "something was written before the package was checked");
        Assert.False(File.Exists(Path.Combine(_root, "evil.dll")));
    }

    /// <summary>項目の名前が道として読めない（NUL が入っている）ものも、同じ形で断る（別の種類の例外にしない）。</summary>
    [Fact]
    public void AnEntryWhoseNameIsNotAPathIsRefused()
    {
        var staging = Path.Combine(_root, "staging");

        var problem = Assert.Throws<InvalidDataException>(() => ModulePackage.Extract(Zip(("module.json", Manifest), ("a\0b.dll", "x")), staging));

        Assert.Contains("not a usable path", problem.Message);
        Assert.False(Directory.Exists(staging));
    }

    [Fact]
    public void APackageWithoutAManifestIsRefused()
    {
        var problem = Assert.Throws<InvalidDataException>(() => ModulePackage.Extract(Zip(("sample.dll", "x")), _root));
        Assert.Contains("module.json", problem.Message);
    }

    /// <summary>大きさの上限は、項目が名乗る大きさの合計で見ている。名乗り（10 バイト）より大きく膨らむ項目（中身は 200,000 バイト）でも、書かれるのは名乗った分まで（.NET が名乗った大きさで読むのをやめる）。これが変わったら、上限をすり抜けて置き場を埋められるので見張る。</summary>
    [Fact]
    public void AnEntryIsNeverWrittenBeyondWhatItDeclares()
    {
        var zip = Zip(("module.json", Manifest), ("big.bin", new string('a', 200_000))).ToArray();
        Declare(zip, "big.bin", 10);

        var (directory, _) = ModulePackage.Extract(new MemoryStream(zip), Path.Combine(_root, "staging"), maxExpandedBytes: 50_000);

        Assert.Equal(10, new FileInfo(Path.Combine(directory, "big.bin")).Length);
    }

    [Fact]
    public void APackageThatDeclaresMoreThanTheLimitIsRefusedBeforeAnythingIsWritten()
    {
        var staging = Path.Combine(_root, "staging");

        Assert.Throws<InvalidDataException>(() => ModulePackage.Extract(
            Zip(("module.json", Manifest), ("big.bin", new string('a', 200_000))), staging, maxExpandedBytes: 50_000));
        Assert.False(Directory.Exists(staging));
    }

    /// <summary>zip の中の、その項目が名乗る「展開したあとの大きさ」を書き換える（項目の頭と、後ろの目録の両方）。</summary>
    private static void Declare(byte[] zip, string name, uint size)
    {
        var wanted = System.Text.Encoding.ASCII.GetBytes(name);
        var patched = 0;
        for (var i = 0; i + 46 < zip.Length; i++)
        {
            var signature = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(i));
            var (sizeAt, nameLengthAt, nameAt) = signature switch
            {
                0x04034b50 => (22, 26, 30),   // 項目の頭
                0x02014b50 => (24, 28, 46),   // 目録
                _ => (0, 0, 0),
            };
            if (sizeAt == 0) continue;
            if (BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(i + nameLengthAt)) != wanted.Length) continue;
            if (!zip.AsSpan(i + nameAt, wanted.Length).SequenceEqual(wanted)) continue;

            BinaryPrimitives.WriteUInt32LittleEndian(zip.AsSpan(i + sizeAt), size);
            patched++;
        }

        Assert.Equal(2, patched);
    }

    private (ModuleInstaller Installer, FakeGitHub GitHub) Installer(Uri? api = null, string? bundled = null)
    {
        var github = new FakeGitHub { Zip = Zip(("sample/module.json", Manifest), ("sample/sample.dll", "x")).ToArray() };
        var releases = new GitHubReleases(new HttpClient(github), api ?? new Uri("http://127.0.0.1:1/"));
        return (new ModuleInstaller(User, bundled ?? Path.Combine(_root, "app"), releases, NullLogger.Instance), github);
    }

    [Fact]
    public void AManifestMaySayWhichSdkTheModuleWasBuiltWith()
    {
        Assert.Null(ModuleManifest.Parse(Manifest).Sdk);

        var manifest = ModuleManifest.Parse("""{ "id": "sample", "name": "Sample", "version": "1.0.0", "sdk": "2026.11.0" }""");

        Assert.Equal(new ModuleManifest("sample", "Sample", "1.0.0", "2026.11.0"), manifest);
    }

    [Theory]
    [InlineData("\"soon\"")]
    [InlineData("\"\"")]
    [InlineData("\"2026\"")]        // 桁が 1 つでは版にならない
    [InlineData("7")]
    [InlineData("null")]
    [InlineData("[\"2026.11.0\"]")]
    public void AManifestWhoseSdkIsNotAVersionIsRefused(string sdk)
    {
        var json = $$"""{ "id": "sample", "name": "Sample", "version": "1.0.0", "sdk": {{sdk}} }""";

        var refused = Assert.Throws<InvalidDataException>(() => ModuleManifest.Parse(json));

        Assert.Equal(
            "module.json: \"sdk\" must be the version of the SpoitableToolKit SDK the module was built with, such as \"2026.11.0\" " +
            $"(the SDK's build writes it; got {sdk})",
            refused.Message);
    }

    /// <summary>版の後ろ（<c>-</c> や <c>+</c> のあと）は版としては読まないが、何でも書けるわけではない。書いた字は「アプリを更新して」の文にそのまま入るので、build が通すのと同じ字だけ。</summary>
    [Theory]
    [InlineData("2026.11.0-\u202Eevil")]
    [InlineData("2026.11.0+a b")]
    [InlineData("2026.11.0-x\ny")]
    public void AnSdkValueWithOtherCharactersAfterTheVersionIsRefused(string sdk)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { id = "sample", name = "Sample", version = "1.0.0", sdk });

        var refused = Assert.Throws<InvalidDataException>(() => ModuleManifest.Parse(json));

        Assert.StartsWith("module.json: \"sdk\" must be the version of the SpoitableToolKit SDK the module was built with", refused.Message);
        Assert.DoesNotContain("\n", refused.Message);
    }

    /// <summary>版は、確認の板・一覧・ログにそのまま出る。build が通すのと同じ字（英数字と <c>.</c> <c>-</c> <c>+</c>、64 字まで）だけを通す — 手で作った zip は build の決まりを通っていないので、本体でも見る。断る文に入れる字は、制御文字と書式の字（字の向きを変える字など）を <c>?</c> にする。</summary>
    [Theory]
    [InlineData("1.0\n\nVerified by the app", "1.0??Verified by the app")]
    [InlineData("1.0 beta", "1.0 beta")]
    [InlineData("1.0\u202E0.9", "1.0?0.9")]
    [InlineData("11111111111111111111111111111111111111111111111111111111111111111", "111111111111111111111111111111111111111111111111111111111111…")]
    public void AManifestWhoseVersionHasOtherCharactersIsRefused(string version, string shown)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { id = "sample", name = "Sample", version });

        var refused = Assert.Throws<InvalidDataException>(() => ModuleManifest.Parse(json));

        Assert.Equal(
            "module.json: \"version\" must be 1-64 characters of letters, digits, '.', '-' and '+', such as \"1.0.0\" " + $"(got \"{shown}\")",
            refused.Message);
    }

    /// <summary>名前は 1 行。改行や行の区切りが入っていたら断る（確認の板に、別の行を書き足せてしまう）。</summary>
    [Theory]
    [InlineData("Official\n\nVerified by the app", "Official??Verified by the app")]
    [InlineData("Line\u2028separator", "Line?separator")]
    public void AManifestWhoseNameIsNotOneLineIsRefused(string name, string shown)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { id = "sample", name, version = "1.0.0" });

        var refused = Assert.Throws<InvalidDataException>(() => ModuleManifest.Parse(json));

        Assert.Equal($"module.json: \"name\" must be one line of at most 128 characters, with no control or invisible formatting characters (got \"{shown}\")", refused.Message);
    }

    /// <summary>見えないのに後ろの字の並びを変える字（字の向きを変える字）と、幅の無い字も断る。確認の板は「名前・版・?」を 1 行に並べるので、名前に U+202E があると後ろの版が違って読める。</summary>
    [Theory]
    [InlineData("Sample \u202E0.9", "Sample ?0.9")]
    [InlineData("\u2066Sample", "?Sample")]
    [InlineData("Sam\u200Bple", "Sam?ple")]
    [InlineData("Sample\uFEFF", "Sample?")]
    public void AManifestWhoseNameHasInvisibleFormattingCharactersIsRefused(string name, string shown)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { id = "sample", name, version = "1.0.0" });

        var refused = Assert.Throws<InvalidDataException>(() => ModuleManifest.Parse(json));

        Assert.Equal(
            "module.json: \"name\" must be one line of at most 128 characters, with no control or invisible formatting characters " + $"(got \"{shown}\")",
            refused.Message);
    }

    [Theory]
    [InlineData("心拍計 ♥ 2")]
    [InlineData("\u05E9\u05DC\u05D5\u05DD")]
    [InlineData("Heart \U0001F600 rate")]
    [InlineData("Name; with \"quotes\" 'and' %percent%")]
    public void ANameOfVisibleCharactersInAnyScriptIsAccepted(string name)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { id = "sample", name, version = "1.0.0" });

        Assert.Equal(name, ModuleManifest.Parse(json).Name);
    }

    /// <summary>断る文に入れる字を 60 字で切るとき、2 つ組の字（絵文字など）を半分にしない。</summary>
    [Theory]
    [InlineData(59, 59, "")]
    [InlineData(58, 58, "\U0001F600")]
    public void ALongValueIsNotCutInTheMiddleOfACharacter(int before, int kept, string keptPair)
    {
        var name = new string('x', before) + "\U0001F600" + new string('x', 80);
        var json = System.Text.Json.JsonSerializer.Serialize(new { id = "sample", name, version = "1.0.0" });

        var refused = Assert.Throws<InvalidDataException>(() => ModuleManifest.Parse(json));

        Assert.Equal(
            "module.json: \"name\" must be one line of at most 128 characters, with no control or invisible formatting characters " +
            $"(got \"{new string('x', kept)}{keptPair}…\")",
            refused.Message);
    }

    [Fact]
    public void AManifestWhoseNameIsLongerThanTheLimitIsRefused()
    {
        static string Json(int length) =>
            System.Text.Json.JsonSerializer.Serialize(new { id = "sample", name = new string('x', length), version = "1.0.0" });

        Assert.Equal(ModuleManifest.MaxNameLength, ModuleManifest.Parse(Json(ModuleManifest.MaxNameLength)).Name.Length);

        var refused = Assert.Throws<InvalidDataException>(() => ModuleManifest.Parse(Json(ModuleManifest.MaxNameLength + 1)));
        Assert.Equal($"module.json: \"name\" must be one line of at most 128 characters, with no control or invisible formatting characters (got \"{new string('x', 60)}…\")", refused.Message);
    }

    /// <summary>展開の上限は、項目が名乗る大きさを足さずに、残りの予算から引いて数える。zip64 の項目は long の上限まで名乗れるので、足すと桁あふれして負になり、上限をすり抜ける（確認の板より前に disk を埋められる）。</summary>
    [Fact]
    public void ASizeThatWouldOverflowTheTotalDoesNotSlipPastTheLimit()
    {
        var remaining = ModulePackage.MaxExpandedBytes;

        Assert.True(ModulePackage.Fits(1, ref remaining));
        Assert.False(ModulePackage.Fits(long.MaxValue, ref remaining));
        Assert.False(ModulePackage.Fits(-1, ref remaining));
        Assert.Equal(ModulePackage.MaxExpandedBytes - 1, remaining);

        Assert.True(ModulePackage.Fits(ModulePackage.MaxExpandedBytes - 1, ref remaining));
        Assert.Equal(0, remaining);
        Assert.False(ModulePackage.Fits(1, ref remaining));
        Assert.True(ModulePackage.Fits(0, ref remaining));
    }

    /// <summary>版として読める頭のあとに長い字を続けたものも断る。通すと、その字がそのまま「アプリを更新して」の文に入り、ログと画面に出る。</summary>
    [Fact]
    public void AnSdkValueLongerThanAnyVersionIsRefused()
    {
        var json = $$"""{ "id": "sample", "name": "Sample", "version": "1.0.0", "sdk": "9999.1.0-{{new string('x', 5000)}}" }""";

        var refused = Assert.Throws<InvalidDataException>(() => ModuleManifest.Parse(json));

        Assert.EndsWith($"got \"9999.1.0-{new string('x', 50)}…)", refused.Message);
        Assert.NotNull(ModuleManifest.Parse(
            $$"""{ "id": "sample", "name": "Sample", "version": "1.0.0", "sdk": "2026.11.0-{{new string('x', ModuleManifest.MaxSdkLength - 10)}}" }"""));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("host.")]
    public void ALongIdIsCutInTheMessage(string start)
    {
        var json = $$"""{ "id": "{{start}}{{new string('x', 5000)}}", "name": "Sample", "version": "1.0.0" }""";

        var refused = Assert.Throws<InvalidDataException>(() => ModuleManifest.Parse(json));

        Assert.Contains(start + new string('x', 60 - start.Length) + "…", refused.Message);
        Assert.True(refused.Message.Length < 400, $"文が長い: {refused.Message.Length} 字");
    }

    /// <summary><c>module.json</c> そのものが大きすぎるものは、読まずに断る。</summary>
    [Fact]
    public void AManifestFarLargerThanItNeedsToBeIsRefused()
    {
        var staging = Path.Combine(_root, "staging");
        var large = $$"""{ "id": "sample", "name": "{{new string('x', ModuleManifest.MaxBytes)}}", "version": "1.0.0" }""";
        using var zip = Zip(("module.json", large), ("sample.dll", "x"));

        var refused = Assert.Throws<InvalidDataException>(() => ModulePackage.Extract(zip, staging));

        Assert.Equal("module.json is larger than 64 KB; it only holds the module's id, name, version and sdk", refused.Message);
    }

    /// <summary>外から来た字なので、文に入れるのは頭の 60 字まで。</summary>
    [Fact]
    public void ALongSdkValueIsCutInTheMessage()
    {
        var json = $$"""{ "id": "sample", "name": "Sample", "version": "1.0.0", "sdk": "{{new string('x', 500)}}" }""";

        var refused = Assert.Throws<InvalidDataException>(() => ModuleManifest.Parse(json));

        Assert.EndsWith($"got \"{new string('x', 59)}…)", refused.Message);
    }

    /// <summary>本体より新しい SDK で作ったモジュールには「アプリを更新して」と言う。数で比べる（字で比べると 2026.2 が 2026.11 より新しくなる）。桁の数と、版の後ろの印では変わらない。</summary>
    [Theory]
    [InlineData("2026.11.0", "2026.11.0", false)]
    [InlineData("2026.11.0", "2026.11.0.0", false)]
    [InlineData("2026.11.0.0", "2026.11.0", false)]
    [InlineData("2026.11", "2026.11.0", false)]
    [InlineData("2026.2.0", "2026.11.0", false)]
    [InlineData("2025.12.9", "2026.11.0", false)]
    [InlineData("2026.11.0-beta.1", "2026.11.0", false)]
    [InlineData("2026.11.1", "2026.11.0", true)]
    [InlineData("2026.12.0-beta.1+abc", "2026.11.0", true)]
    [InlineData("2026.12.0+abc", "2026.11.0", true)]
    [InlineData("2026.11.0+abc", "2026.11.0", false)]
    [InlineData("2027.1.0", "2026.11.0", true)]
    public void AModuleBuiltWithANewerSdkThanTheAppIsToldToUpdateTheApp(string sdk, string app, bool refused)
    {
        var manifest = new ModuleManifest("sample", "Sample", "1.0.0", sdk);

        var problem = manifest.NewerSdkProblem(Version.Parse(app));

        Assert.Equal(
            refused
                ? $"The module was built with SpoitableToolKit SDK {sdk}, which is newer than this app's (2026.11.0). " +
                  "Update the app first, then add the module again."
                : null,
            problem);
    }

    [Fact]
    public void AModuleThatDoesNotSayItsSdkIsNotRefusedForIt()
    {
        Assert.Null(new ModuleManifest("sample", "Sample", "1.0.0").NewerSdkProblem(new Version(1, 0)));
        Assert.Null(new ModuleManifest("sample", "Sample", "1.0.0").OlderSdkProblem(new Version(9999, 1)));
    }

    [Theory]
    [InlineData("2026.11.0", "2026.11.0", false)]
    [InlineData("2026.11.0.0", "2026.11.0", false)]
    [InlineData("2026.11", "2026.11.0", false)]
    [InlineData("2026.11.0-beta.1", "2026.11.0", false)]
    [InlineData("2026.12.0", "2026.11.0", false)]
    [InlineData("2026.10.9", "2026.11.0", true)]
    [InlineData("2025.12.0+abc", "2026.11.0", true)]
    [InlineData("2026.11.0", "2026.11.1", true)]
    public void AModuleBuiltWithAnOlderSdkThanTheAppCanLoadIsToldToBeBuiltAgain(string sdk, string minimum, bool refused)
    {
        var problem = new ModuleManifest("sample", "Sample", "1.0.0", sdk).OlderSdkProblem(Version.Parse(minimum));

        Assert.Equal(
            refused
                ? $"The module was built with SpoitableToolKit SDK {sdk}, which is older than this app can load (SDK {Version.Parse(minimum).ToString(3)} or newer): " +
                  "the SDK has changed in a way that modules built with older versions cannot load. Ask its developer to build it again with a newer SDK."
                : null,
            problem);
    }

    /// <summary>下限だけ上げて本体の版を上げ忘れると、同梱のモジュールまで読めなくなる。</summary>
    [Fact]
    public void TheOldestSdkTheAppLoadsIsNotNewerThanItsOwn() =>
        Assert.True(ModuleManifest.MinimumSdk <= ModuleManifest.AppSdk, $"MinimumSdk {ModuleManifest.MinimumSdk} > AppSdk {ModuleManifest.AppSdk}");

    /// <summary>本体の SDK の版は、本体が持っている API の dll（<c>SpoitableToolKit.Abstractions</c>）の版。</summary>
    [Fact]
    public void TheAppsSdkVersionIsTheVersionOfItsApiAssembly()
    {
        var api = typeof(SpoitableToolKit.Abstractions.ISpoitableModule).Assembly.GetName().Version!;

        Assert.Equal(new Version(api.Major, api.Minor, api.Build, api.Revision), ModuleManifest.AppSdk);
        Assert.True(ModuleManifest.AppSdk > new Version(2026, 1), "版が入っていない（0.0 のまま）");
    }

    /// <summary>本体より新しい SDK で作った zip は、聞く前に断る（入れても読み込めない）。同じ版で作ったものは通る。</summary>
    [Fact]
    public async Task APackageBuiltWithAnOlderSdkIsRefusedBeforeTheUserIsAsked()
    {
        var (installer, github) = Installer();
        github.Zip = Zip(
            ("module.json", """{ "id": "sample", "name": "Sample", "version": "1.0.0", "sdk": "2000.1.0" }"""),
            ("sample.dll", "x")).ToArray();

        var problem = await Assert.ThrowsAsync<InvalidDataException>(() => installer.StageFromGitHubAsync("https://github.com/someone/sample", default));

        Assert.StartsWith(
            $"The module was built with SpoitableToolKit SDK 2000.1.0, which is older than this app can load (SDK {ModuleManifest.MinimumSdk.ToString(3)} or newer)",
            problem.Message);
        Assert.Empty(Directory.GetDirectories(Path.Combine(Modules, ModuleInstaller.StagingDirectoryName)));
    }

    [Fact]
    public async Task APackageBuiltWithANewerSdkIsRefusedBeforeTheUserIsAsked()
    {
        var (installer, github) = Installer();
        github.Zip = Zip(
            ("module.json", """{ "id": "sample", "name": "Sample", "version": "1.0.0", "sdk": "9999.1.0" }"""),
            ("sample.dll", "x")).ToArray();

        var problem = await Assert.ThrowsAsync<InvalidDataException>(() => installer.StageFromGitHubAsync("https://github.com/someone/sample", default));

        Assert.Equal(
            $"The module was built with SpoitableToolKit SDK 9999.1.0, which is newer than this app's ({ModuleManifest.AppSdk.ToString(3)}). " +
            "Update the app first, then add the module again.",
            problem.Message);
        Assert.Empty(Directory.GetDirectories(Path.Combine(Modules, ModuleInstaller.StagingDirectoryName)));

        github.Zip = Zip(
            ("module.json", $$"""{ "id": "sample", "name": "Sample", "version": "1.0.0", "sdk": "{{ModuleManifest.AppSdk.ToString(3)}}" }"""),
            ("sample.dll", "x")).ToArray();

        var staged = await installer.StageFromGitHubAsync("https://github.com/someone/sample", default);

        Assert.Equal(ModuleManifest.AppSdk.ToString(3), staged.Manifest.Sdk);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("hrs")]
    [InlineData("a.b-c_d")]
    [InlineData("console")]
    [InlineData("hostess")]
    [InlineData("commons")]
    [InlineData("my.host")]
    public void AManifestIdMadeOfLowercaseLettersDigitsAndSeparatorsIsAccepted(string id) => Assert.True(ModuleManifest.IsValidId(id));

    /// <summary>本体が自分のために使っている名前（設定・翻訳キーの頭・<c>spoitable://host/</c>）は Id にできない。</summary>
    [Theory]
    [InlineData("host")]
    [InlineData("common")]
    [InlineData("host.extra")]
    [InlineData("common.ui")]
    public void AManifestIdThatBelongsToTheAppIsRefused(string id)
    {
        Assert.True(ModuleManifest.IsReservedId(id));
        Assert.False(ModuleManifest.IsValidId(id));

        var json = JsonSerializer.Serialize(new { id, name = "x", version = "1" });
        var refused = Assert.Throws<InvalidDataException>(() => ModuleManifest.Parse(json));
        Assert.Equal(
            $"module.json: The Id '{id}' is reserved for the app itself ('host' and 'common', and any Id that starts with 'host.' or 'common.'). " +
            "Give the module an Id of its own, for example its name in lower case.",
            refused.Message);
    }

    /// <summary>
    /// GitHub の差し替え（<c>SPOITABLE_GITHUB_API</c>）を受けるのは、自分の機械を指すときだけ。よその場所を指していたら、本物の GitHub へ聞く
    /// （よそを指せると、GitHub 以外からモジュールを落とさせる口になる）。
    /// </summary>
    [Theory]
    [InlineData("http://127.0.0.1:5000/", "http://127.0.0.1:5000/")]
    [InlineData("http://localhost:5000/", "http://localhost:5000/")]
    [InlineData("https://evil.example/", "https://api.github.com/")]
    [InlineData("not a url", "https://api.github.com/")]
    [InlineData("", "https://api.github.com/")]
    [InlineData(null, "https://api.github.com/")]
    public void TheGitHubStandInIsOnlyTakenWhenItPointsAtThisMachine(string? value, string expected) =>
        Assert.Equal(expected, GitHubReleases.ApiFrom(value).ToString());

    [Fact]
    public async Task AnAnswerThatIsNotAReleaseIsRefusedWithAReason()
    {
        var (installer, github) = Installer();
        github.Garbage = true;

        var problem = await Assert.ThrowsAsync<InvalidDataException>(() => installer.StageFromGitHubAsync("https://github.com/someone/sample", default));

        Assert.Contains("not a GitHub release", problem.Message);
    }

    /// <summary>本体が印に使う名前のファイル（外す・OFF）を持ち込む zip は、聞く前に断る。入れてしまうと、入れた直後に自分で消える（前の版も一緒に）か、黙って止まる。</summary>
    [Theory]
    [InlineData(".remove")]
    [InlineData(".disabled")]
    public async Task APackageThatBringsTheAppsOwnMarkerFilesIsRefused(string marker)
    {
        var (installer, github) = Installer();
        github.Zip = Zip(("module.json", Manifest), ("sample.dll", "x"), (marker, string.Empty)).ToArray();

        var problem = await Assert.ThrowsAsync<InvalidDataException>(() => installer.StageFromGitHubAsync("https://github.com/someone/sample", default));

        Assert.Contains(marker, problem.Message);
        Assert.Empty(Directory.GetDirectories(Path.Combine(Modules, ModuleInstaller.StagingDirectoryName)));
    }

    /// <summary>ユーザー側の置き場が同梱の場所と同じなら、落とす前に断る（起動の頭の入れ替えはそこでは何もしないので、聞いても入らない）。</summary>
    [Fact]
    public async Task NothingIsFetchedWhenTheUserFolderIsTheBundledOne()
    {
        var (installer, github) = Installer(bundled: User);

        var problem = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.StageFromGitHubAsync("https://github.com/someone/sample", default));

        Assert.Contains("SPOITABLE_DATA_DIR", problem.Message);
        Assert.Empty(github.Asked);
        Assert.False(Directory.Exists(Path.Combine(Modules, ModuleInstaller.StagingDirectoryName)));
    }

    /// <summary>新しい版を置けなかったら、前の版を元の場所へ戻す。戻さないと、このあとの片付けが .trash ごと消して、何も入っていない状態になる。</summary>
    [Fact]
    public async Task WhenTheNewVersionCannotBePutInPlaceThePreviousOneStays()
    {
        var old = Path.Combine(Modules, "sample");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "old.dll"), "old");
        var (installer, _) = Installer();
        Assert.True(installer.Confirm((await installer.StageFromGitHubAsync("https://github.com/someone/sample", default)).Token));
        var queued = Path.Combine(Modules, ModuleInstaller.PendingDirectoryName, "sample");

        using (File.Open(Path.Combine(queued, "sample.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Empty(PendingInstalls.Apply(User, Path.Combine(_root, "app"), NullLogger.Instance));

        Assert.True(File.Exists(Path.Combine(old, "old.dll")), "前の版が元の場所に無い");
        Assert.Empty(Directory.GetDirectories(Path.Combine(Modules, PendingRemovals.TrashDirectoryName)));
        Assert.True(Directory.Exists(queued), "入れかけは残して、次の起動でまた試す");

        // 掴んでいたものが離れたら、次の起動で入る。
        Assert.Equal(["sample"], PendingInstalls.Apply(User, Path.Combine(_root, "app"), NullLogger.Instance));
        Assert.True(File.Exists(Path.Combine(old, "sample.dll")));
    }

    [Fact]
    public async Task AReleaseIsStagedAndOnlyInstalledAtTheNextStartAfterItIsConfirmed()
    {
        var (installer, github) = Installer();

        var staged = await installer.StageFromGitHubAsync("https://github.com/someone/sample", default);

        Assert.Equal("sample", staged.Manifest.Id);
        Assert.Equal("someone/sample", staged.Source.Repository);
        Assert.Equal("v1.2.0", staged.Source.Tag);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(github.Zip)), staged.Source.Sha256);
        Assert.Contains(github.Asked, u => u.AbsolutePath == "/repos/someone/sample/releases/latest");
        Assert.False(Directory.Exists(Path.Combine(Modules, "sample")), "nothing is installed before it is confirmed");
        Assert.Empty(installer.Pending());

        Assert.True(installer.Confirm(staged.Token));
        Assert.Equal(["sample"], installer.Pending().Select(p => p.Manifest.Id));
        Assert.Equal("v1.2.0", installer.Pending()[0].Source!.Tag);
        Assert.False(Directory.Exists(Path.Combine(Modules, "sample")), "nothing is installed before the next start");
        Assert.False(installer.Confirm(staged.Token), "the same staging cannot be confirmed twice");

        Assert.Equal(["sample"], PendingInstalls.Apply(User, Path.Combine(_root, "app"), NullLogger.Instance));
        Assert.True(File.Exists(Path.Combine(Modules, "sample", "sample.dll")));
        Assert.Equal("v1.2.0", ModuleSource.ReadFrom(Path.Combine(Modules, "sample"))!.Tag);
        Assert.Empty(installer.Pending());
    }

    [Fact]
    public async Task AnUpdateReplacesTheInstalledOneAtTheNextStart()
    {
        var old = Path.Combine(Modules, "sample");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "old.dll"), "old");

        var (installer, _) = Installer();
        Assert.True(installer.Confirm((await installer.StageFromGitHubAsync("https://github.com/someone/sample", default)).Token));
        Assert.True(File.Exists(Path.Combine(old, "old.dll")), "the running copy is not touched until the next start");

        PendingInstalls.Apply(User, Path.Combine(_root, "app"), NullLogger.Instance);

        Assert.False(File.Exists(Path.Combine(old, "old.dll")));
        Assert.True(File.Exists(Path.Combine(old, "sample.dll")));
    }

    [Fact]
    public async Task CancellingDeletesTheStaging()
    {
        var (installer, _) = Installer();
        var staged = await installer.StageFromGitHubAsync("https://github.com/someone/sample", default);

        Assert.True(installer.Cancel(staged.Token));

        Assert.False(Directory.Exists(staged.Directory));
        Assert.False(installer.Confirm(staged.Token));
        Assert.Empty(installer.Pending());
    }

    [Fact]
    public async Task APendingInstallCanBeTakenBack()
    {
        var (installer, _) = Installer();
        Assert.True(installer.Confirm((await installer.StageFromGitHubAsync("https://github.com/someone/sample", default)).Token));

        Assert.True(installer.CancelPending("sample"));
        Assert.Empty(installer.Pending());
        Assert.False(installer.CancelPending("../user"));
    }

    [Fact]
    public async Task ADownloadThatDoesNotMatchThePublishedHashIsRefused()
    {
        var (installer, github) = Installer();
        github.Digest = "sha256:" + new string('0', 64);

        var problem = await Assert.ThrowsAsync<InvalidDataException>(() => installer.StageFromGitHubAsync("https://github.com/someone/sample", default));

        Assert.Contains("SHA-256", problem.Message);
        Assert.Empty(Directory.Exists(Path.Combine(Modules, ModuleInstaller.StagingDirectoryName))
            ? Directory.GetDirectories(Path.Combine(Modules, ModuleInstaller.StagingDirectoryName)) : []);
    }

    /// <summary>release の tag は repo の持ち主が選べて、確認の板・一覧・断る文・ログにそのまま出る。module.json の名前と同じ決まり（1 行・見える字だけ・長さに上限）を掛けて、落とす前に断る。</summary>
    [Theory]
    [InlineData("v1.0\u202Egpj.exe", "v1.0?gpj.exe")]
    [InlineData("v1.0\nVerified by the app", "v1.0?Verified by the app")]
    [InlineData("", "")]
    [InlineData("vvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvv",
        "vvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvv…")]
    public async Task AReleaseWhoseTagIsNotOneLineOfVisibleTextIsRefused(string tag, string shown)
    {
        var (installer, github) = Installer();
        github.Tag = tag;

        var problem = await Assert.ThrowsAsync<InvalidDataException>(() => installer.StageFromGitHubAsync("https://github.com/someone/sample", default));

        Assert.Equal(
            $"The latest release of someone/sample has a tag the app does not accept (\"{shown}\"): " +
            "a tag must be one line of at most 128 characters, with no control or invisible formatting characters",
            problem.Message);
        Assert.DoesNotContain(github.Asked, u => u.AbsolutePath.Contains("/download/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ATagOfExactlyTheLongestLengthIsAccepted()
    {
        var (installer, github) = Installer();
        github.Tag = new string('v', GitHubReleases.MaxTagLength);

        var staged = await installer.StageFromGitHubAsync("https://github.com/someone/sample", default);

        Assert.Equal(github.Tag, staged.Source.Tag);
    }

    [Theory]
    [InlineData("sample\u202Efdp.zip", "sample?fdp.zip")]
    [InlineData("sample\u200B-module.zip", "sample?-module.zip")]
    public async Task AReleaseWithAFileWhoseNameIsNotOneLineOfVisibleTextIsRefused(string name, string shown)
    {
        var (installer, github) = Installer();
        github.AssetName = name;

        var problem = await Assert.ThrowsAsync<InvalidDataException>(() => installer.StageFromGitHubAsync("https://github.com/someone/sample", default));

        Assert.Equal(
            $"The release v1.2.0 of someone/sample has an attached file whose name the app does not accept (\"{shown}\"): " +
            "a file name must be one line of at most 255 characters, with no control or invisible formatting characters",
            problem.Message);
        Assert.DoesNotContain(github.Asked, u => u.AbsolutePath.Contains("/download/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFileNameLongerThanTheLimitIsRefused()
    {
        var (installer, github) = Installer();

        github.AssetName = new string('a', GitHubReleases.MaxAssetNameLength - 4) + ".zip";
        var staged = await installer.StageFromGitHubAsync("https://github.com/someone/sample", default);
        Assert.Equal(github.AssetName, staged.Source.Asset);
        Assert.True(installer.Cancel(staged.Token));

        github.AssetName = new string('a', GitHubReleases.MaxAssetNameLength - 3) + ".zip";
        var problem = await Assert.ThrowsAsync<InvalidDataException>(() => installer.StageFromGitHubAsync("https://github.com/someone/sample", default));
        Assert.Contains("a file name must be one line of at most 255 characters", problem.Message);
    }

    /// <summary>GitHub が出したハッシュは 16 進 64 桁だけを受ける。その形でなければ「出していない」として扱い、合わないときの文に外から来た字を入れない。</summary>
    [Fact]
    public async Task APublishedHashThatIsNotASha256IsLeftOut()
    {
        var (installer, github) = Installer();
        github.Digest = "sha256:<b>trust me</b>\u202E";

        var staged = await installer.StageFromGitHubAsync("https://github.com/someone/sample", default);

        Assert.Equal("sample", staged.Manifest.Id);
    }

    private static readonly ModuleBuildTools NoTools = new("no-such-dotnet", "no-feed", null);

    /// <summary>聞くだけで、何も落とさない。commit は id だけを返してもらう（branch の名前は repo の持ち主が選べる字なので、扱わない）。</summary>
    [Fact]
    public async Task AnOfferToBuildNamesTheHeadCommitAndDownloadsNothing()
    {
        var (installer, github) = Installer();

        var offer = await installer.OfferSourceAsync("https://github.com/someone/sample", default);

        Assert.Equal(("someone/sample", github.Commit), (offer.Repository.ToString(), offer.Commit));
        var asked = Assert.Single(github.Asked);
        Assert.Equal("/repos/someone/sample/commits/HEAD", asked.AbsolutePath);
        Assert.Equal("application/vnd.github.sha", Assert.Single(github.Accepts));
    }

    /// <summary>commit の id は 40 桁の 16 進だけを受ける（URL・入れ元の記録・画面へ渡る）。後ろの改行は落とす。</summary>
    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef01234567\n", true)]
    [InlineData("0123456789ABCDEF0123456789ABCDEF01234567", false)]
    [InlineData("0123456789abcdef0123456789abcdef0123456", false)]
    [InlineData("0123456789abcdef0123456789abcdef01234567/../../x", false)]
    [InlineData("main", false)]
    [InlineData("{\"sha\":\"0123456789abcdef0123456789abcdef01234567\"}", false)]
    [InlineData("", false)]
    public async Task OnlyACommitIdIsAcceptedAsTheHeadCommit(string answer, bool accepted)
    {
        var (installer, github) = Installer();
        github.CommitAnswer = answer;

        if (accepted)
        {
            Assert.Equal(answer.Trim(), (await installer.OfferSourceAsync("https://github.com/someone/sample", default)).Commit);
            return;
        }

        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => installer.OfferSourceAsync("https://github.com/someone/sample", default));
        Assert.Equal("The answer for someone/sample is not a commit id", refused.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task ARepositoryWithoutCodeSaysSo(HttpStatusCode status)
    {
        var (installer, github) = Installer();
        github.CommitStatus = status;

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.OfferSourceAsync("https://github.com/someone/sample", default));

        Assert.Equal("someone/sample has no code to build (or the repository is private or does not exist)", refused.Message);
    }

    /// <summary>モジュールの project が無い repo は、build を始める前に断る（道具が無くても同じ文になる = dotnet を起動していない）。作業場は残さない。</summary>
    [Fact]
    public async Task ARepositoryWithoutAModuleProjectIsRefusedBeforeAnythingIsBuilt()
    {
        var (installer, github) = Installer();
        github.Source = Zip(("someone-sample-0123456/README.md", "# nothing to build")).ToArray();
        var offer = await installer.OfferSourceAsync("https://github.com/someone/sample", default);

        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => installer.StageFromSourceAsync(offer, NoTools, default));

        Assert.Equal(
            "someone/sample at 0123456 could not be built: no module project was found: the app builds the one .csproj that has <SpoitableModuleId> in it " +
            "(a repository that only holds a contract or other code cannot be installed)",
            refused.Message);
        Assert.Contains(github.Asked, u => u.AbsolutePath == $"/repos/someone/sample/zipball/{github.Commit}");
        Assert.Empty(Directory.GetDirectories(Path.Combine(User, ModuleInstaller.BuildDirectoryName)));
    }

    /// <summary>ソースの zip も、モジュールの zip と同じ決まりで展開する。断る文に入れる項目の名前は、zip を作った側が好きに書ける字なので絞る。</summary>
    [Fact]
    public async Task ASourceArchiveIsUnpackedUnderTheSameRulesAsAPackage()
    {
        var (installer, github) = Installer();
        github.Source = Zip(("repo/ok.txt", "x"), ("../out\u202Eside.txt", "x")).ToArray();
        var offer = await installer.OfferSourceAsync("https://github.com/someone/sample", default);

        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => installer.StageFromSourceAsync(offer, NoTools, default));

        Assert.Equal("The source has an entry that points outside its folder: ../out?side.txt", refused.Message);
        Assert.False(File.Exists(Path.Combine(User, "out\u202Eside.txt")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(User, ModuleInstaller.BuildDirectoryName)));
    }

    /// <summary>起動の頭で、build の作業場の残りを消す（前のアプリが build の途中で閉じた）。消すのは自分が作った形の名前（16 進 8 桁）のフォルダだけ — データの置き場は利用者が好きな所へ向けられるので、ほかのものには触らない。</summary>
    [Fact]
    public void LeftoverBuildFoldersAreRemovedAtTheNextStart()
    {
        var build = Path.Combine(User, ModuleInstaller.BuildDirectoryName);
        var leftover = Path.Combine(build, "abcd1234", "s");
        Directory.CreateDirectory(leftover);
        File.WriteAllText(Path.Combine(leftover, "file.txt"), "x");
        var someoneElses = Path.Combine(build, "my notes");
        Directory.CreateDirectory(someoneElses);
        File.WriteAllText(Path.Combine(someoneElses, "keep.txt"), "x");

        PendingInstalls.Apply(User, Path.Combine(_root, "app"), NullLogger.Instance);

        Assert.False(Directory.Exists(Path.Combine(build, "abcd1234")));
        Assert.True(File.Exists(Path.Combine(someoneElses, "keep.txt")));
    }

    /// <summary>作業場の名前は、利用者のフォルダと取り違えないもの（<c>build</c> のような、どこにでもある名前にしない）。</summary>
    [Fact]
    public void TheBuildFolderHasANameOfTheAppsOwn() =>
        Assert.Equal(".module-build", ModuleInstaller.BuildDirectoryName);

    /// <summary>Windows で書けない項目は、理由つきで断る。.NET の文（作った側が好きに書ける名前の入った path）は、そのまま出さない。</summary>
    [Theory]
    [InlineData("repo/Readme.md", "repo/README.MD", "repo/README.MD")]
    [InlineData("repo/a", "repo/a/b.txt", "repo/a/b.txt")]
    [InlineData("repo/ok.txt", "repo/what\u202E?.txt", "repo/what??.txt")]
    public async Task ASourceEntryThatCannotBeWrittenOnWindowsIsRefusedInTheAppsOwnWords(string first, string second, string shown)
    {
        var (installer, github) = Installer();
        github.Source = Zip((first, "x"), (second, "x")).ToArray();
        var offer = await installer.OfferSourceAsync("https://github.com/someone/sample", default);

        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => installer.StageFromSourceAsync(offer, NoTools, default));

        Assert.Equal(
            $"The source has an entry that cannot be written on this PC: {shown} " +
            "(a name Windows does not allow, the same name twice — names that differ only in upper and lower case are the same here — " +
            "or a file and a folder with one name)",
            refused.Message);
        Assert.Empty(Directory.GetDirectories(Path.Combine(User, ModuleInstaller.BuildDirectoryName)));
    }

    [Fact]
    public void APackageEntryThatCannotBeWrittenOnWindowsIsRefusedInTheAppsOwnWords()
    {
        using var zip = Zip(("sample/module.json", Manifest), ("sample/Sample.dll", "x"), ("sample/sample.dll", "x"));

        var refused = Assert.Throws<InvalidDataException>(() => ModulePackage.Extract(zip, Path.Combine(_root, "twice")));

        Assert.StartsWith("The package has an entry that cannot be written on this PC: sample/sample.dll (", refused.Message);
    }

    /// <summary>build は並べて走らせてよい（作業場は build ごと）。</summary>
    [Fact]
    public async Task TwoBuildsRunSideBySide()
    {
        var (installer, github) = Installer();
        github.Source = Zip(("repo/README.md", "x")).ToArray();
        var release = new TaskCompletionSource();
        var both = new TaskCompletionSource();
        var reached = 0;
        github.BeforeSource = () =>
        {
            if (Interlocked.Increment(ref reached) == 2) both.TrySetResult();
            return release.Task;
        };
        var work = Path.Combine(User, ModuleInstaller.BuildDirectoryName);

        var first = await installer.OfferSourceAsync("https://github.com/someone/sample", default);
        var second = await installer.OfferSourceAsync("https://github.com/someone/other", default);
        Task<StagedModule>[] running = [installer.StageFromSourceAsync(first, NoTools, default), installer.StageFromSourceAsync(second, NoTools, default)];
        await both.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(2, Directory.GetDirectories(work).Length);

        release.SetResult();
        foreach (var build in running) await Assert.ThrowsAsync<InvalidDataException>(() => build);
        Assert.Empty(Directory.GetDirectories(work));
    }

    /// <summary>build するのは、渡された offer の commit（本体が、利用者に見せたものをそのまま持って来る）。モジュールの project が無い repo は、ソースを落とした所で止まる（build には進まない）。</summary>
    [Fact]
    public async Task ABuildFromAnOfferDownloadsThatCommitAndSaysWhichStepItIsAt()
    {
        var (installer, github) = Installer();
        github.Source = Zip(("repo/README.md", "x")).ToArray();
        var offer = await installer.OfferSourceAsync("https://github.com/someone/sample", default);
        github.Commit = "fedcba9876543210fedcba9876543210fedcba98";   // 聞いたあとで、branch の先頭が動いた
        List<SourceBuildPhase> phases = [];

        await Assert.ThrowsAsync<InvalidDataException>(
            () => installer.StageFromSourceAsync(offer, NoTools, default, phase: new DirectProgress<SourceBuildPhase>(phases.Add)));

        Assert.Equal([SourceBuildPhase.Source], phases);
        Assert.Contains(github.Asked, u => u.AbsolutePath == "/repos/someone/sample/zipball/0123456789abcdef0123456789abcdef01234567");
    }

    /// <summary>止めたことは失敗とは別に伝わる（<see cref="OperationCanceledException"/>。呼ぶ側は、止めたカードは消し、失敗したカードは理由を出して残す）。</summary>
    [Fact]
    public async Task ABuildThatIsCancelledLeavesNothingBehind()
    {
        var (installer, github) = Installer();
        github.Source = Zip(("repo/README.md", "x")).ToArray();
        using var stop = new CancellationTokenSource();
        var reached = new TaskCompletionSource();
        github.BeforeSource = () =>
        {
            reached.TrySetResult();
            return Task.Delay(Timeout.Infinite, stop.Token);
        };
        var work = Path.Combine(User, ModuleInstaller.BuildDirectoryName);

        var offer = await installer.OfferSourceAsync("https://github.com/someone/sample", default);
        var running = installer.StageFromSourceAsync(offer, NoTools, stop.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(Directory.GetDirectories(work));

        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Empty(Directory.GetDirectories(work));
    }

    [Fact]
    public async Task DownloadingAReleaseReportsHowFarItIs()
    {
        var (installer, github) = Installer();
        var filler = new byte[700 * 1024];
        new Random(1).NextBytes(filler);
        using (var zip = new MemoryStream())
        {
            using (var archive = new System.IO.Compression.ZipArchive(zip, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var manifest = new StreamWriter(archive.CreateEntry("module.json").Open()))
                    manifest.Write("""{ "id": "sample", "name": "Sample module", "version": "1.2.0" }""");
                using var dll = archive.CreateEntry("sample.dll", System.IO.Compression.CompressionLevel.NoCompression).Open();
                dll.Write(filler);
            }

            github.Zip = zip.ToArray();
        }

        List<ModuleDownload> reports = [];
        await installer.StageFromGitHubAsync("https://github.com/someone/sample", default, new DirectProgress<ModuleDownload>(reports.Add));

        Assert.Equal(new ModuleDownload("v1.2.0", "sample-module.zip", 0, github.Zip.Length), reports[0]);
        Assert.Equal(new ModuleDownload("v1.2.0", "sample-module.zip", github.Zip.Length, github.Zip.Length), reports[^1]);
        Assert.True(reports.Count >= 4, $"途中の知らせが無い（{reports.Count} 回）");
        Assert.Equal(reports.Select(r => r.Bytes).Order(), reports.Select(r => r.Bytes));
    }

    [Fact]
    public async Task ARepositoryWithoutAReleaseSaysSo()
    {
        var (installer, github) = Installer();
        github.Status = HttpStatusCode.NotFound;

        var problem = await Assert.ThrowsAsync<NoReleaseException>(() => installer.StageFromGitHubAsync("https://github.com/someone/sample", default));
        Assert.Contains("no published release", problem.Message);
    }

    /// <summary>GitHub へは名乗って聞く（名乗らないと API は 403）。名前はアプリの名前と版だけ。</summary>
    [Fact]
    public async Task GitHubIsAskedWithTheAppsNameAndARefusalSaysSo()
    {
        var (installer, github) = Installer();

        await installer.StageFromGitHubAsync("https://github.com/someone/sample", default);

        Assert.Equal(2, github.Agents.Count);
        Assert.All(github.Agents, agent => Assert.Matches(@"^SpoitableToolKit/\d+\.\d+\.\d+$", agent));

        github.Status = HttpStatusCode.Forbidden;
        var problem = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.StageFromGitHubAsync("https://github.com/someone/sample", default));
        Assert.Contains("try again later", problem.Message);
    }

    /// <summary>展開したものが探索で見つかる形でなければ、入れてから「読み込めません」になるより前に、直し方つきで断る。</summary>
    [Fact]
    public async Task APackageWithoutAModuleDllIsRefusedBeforeItIsOffered()
    {
        var (installer, github) = Installer();
        github.Zip = Zip(("module.json", Manifest), ("readme.txt", "x"), ("other.dll", "x")).ToArray();

        var problem = await Assert.ThrowsAsync<InvalidDataException>(() => installer.StageFromGitHubAsync("https://github.com/someone/sample", default));

        Assert.Contains("sample.dll", problem.Message);
        Assert.Contains("shared/*.dll", problem.Message);
        Assert.Empty(Directory.GetDirectories(Path.Combine(Modules, ModuleInstaller.StagingDirectoryName)));
    }

    [Theory]
    [InlineData("sample.dll", null, "module")]
    [InlineData("Built.dll", "Built.deps.json", "module")]
    [InlineData("shared/Api.dll", null, "library")]
    public async Task ThePackageIsAModuleOrALibrary(string dll, string? extra, string kind)
    {
        var (installer, github) = Installer();
        github.Zip = (extra is null ? Zip(("module.json", Manifest), (dll, "x")) : Zip(("module.json", Manifest), (dll, "x"), (extra, "{}"))).ToArray();

        var staged = await installer.StageFromGitHubAsync("https://github.com/someone/sample", default);

        Assert.Equal(kind, staged.Kind);
    }

    /// <summary>外すと決めてあったものを入れ直すと、外す印は消える（残ると、どちらが効くかが起動の頭の順番しだいになる）。</summary>
    [Fact]
    public async Task ConfirmingAnInstallTakesBackARemoval()
    {
        var installed = Path.Combine(Modules, "sample");
        Directory.CreateDirectory(installed);
        File.WriteAllText(Path.Combine(installed, ModuleCatalog.RemoveMarker), string.Empty);
        var (installer, _) = Installer();

        Assert.True(installer.Confirm((await installer.StageFromGitHubAsync("https://github.com/someone/sample", default)).Token));

        Assert.False(File.Exists(Path.Combine(installed, ModuleCatalog.RemoveMarker)));
    }

    /// <summary>外す印を消せなくても、仮置きを .pending へ移せたら入れかけは成った（「できなかった」と返すと、利用者がやめても次の起動で入ってしまう）。</summary>
    [Fact]
    public async Task AnInstallIsQueuedEvenWhenTheRemovalCannotBeTakenBack()
    {
        var installed = Path.Combine(Modules, "sample");
        Directory.CreateDirectory(installed);
        var marker = Path.Combine(installed, ModuleCatalog.RemoveMarker);
        File.WriteAllText(marker, string.Empty);
        File.WriteAllText(Path.Combine(installed, "old.txt"), "the earlier copy");
        var (installer, _) = Installer();
        var staged = await installer.StageFromGitHubAsync("https://github.com/someone/sample", default);

        using (new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.True(installer.Confirm(staged.Token));

        Assert.False(installer.Cancel(staged.Token), "入れかけにしたのに、仮置きとして残った");
        Assert.Equal(["sample"], installer.Pending().Select(pending => pending.Manifest.Id));
        Assert.Equal(["sample"], PendingInstalls.Apply(User, Path.Combine(_root, "app"), NullLogger.Instance));
        Assert.False(File.Exists(Path.Combine(installed, "old.txt")));
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void APendingFolderWhoseNameIsNotItsIdIsNotCounted()
    {
        var odd = Path.Combine(Modules, ModuleInstaller.PendingDirectoryName, "other");
        Directory.CreateDirectory(odd);
        File.WriteAllText(Path.Combine(odd, "module.json"), Manifest);
        File.WriteAllText(Path.Combine(odd, "sample.dll"), "x");
        var (installer, _) = Installer();

        Assert.Empty(installer.Pending());
    }

    /// <summary>入れかけの置き場（<c>.pending</c>）がリンクなら使わない。そのまま使うと、リンクの先にあるフォルダをモジュールとして入れてしまう。</summary>
    [Fact]
    public async Task APendingFolderThatIsALinkIsNotUsed()
    {
        var outside = Path.Combine(_root, "outside");
        var planted = Path.Combine(outside, "planted");
        Directory.CreateDirectory(planted);
        File.WriteAllText(Path.Combine(planted, "module.json"), Manifest.Replace("sample", "planted"));
        File.WriteAllText(Path.Combine(planted, "planted.dll"), "x");
        Junction(Path.Combine(Modules, ModuleInstaller.PendingDirectoryName), outside);
        var (installer, _) = Installer();

        Assert.Empty(installer.Pending());
        Assert.False(installer.Confirm((await installer.StageFromGitHubAsync("https://github.com/someone/sample", default)).Token));
        Assert.Empty(PendingInstalls.Apply(User, Path.Combine(_root, "app"), NullLogger.Instance));

        Assert.False(Directory.Exists(Path.Combine(Modules, "planted")), "リンクの先のフォルダを入れた");
        Assert.True(Directory.Exists(planted));
        Assert.Equal(["planted"], Directory.GetDirectories(outside).Select(Path.GetFileName));
    }

    /// <summary>入れかけのフォルダそのものがリンクなら、入れない（入れると、置き場の外のフォルダがモジュールとして読まれる）。</summary>
    [Fact]
    public void APendingModuleThatIsALinkIsNotInstalled()
    {
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "module.json"), Manifest);
        File.WriteAllText(Path.Combine(outside, "sample.dll"), "x");
        Junction(Path.Combine(Modules, ModuleInstaller.PendingDirectoryName, "sample"), outside);

        Assert.Empty(PendingInstalls.Apply(User, Path.Combine(_root, "app"), NullLogger.Instance));

        Assert.False(Directory.Exists(Path.Combine(Modules, "sample")));
        Assert.True(File.Exists(Path.Combine(outside, "sample.dll")));
    }

    /// <summary>仮置きの置き場（<c>.staging</c>）がリンクなら、そこへは展開しない（置き場の外へ書くことになる）。</summary>
    [Fact]
    public async Task NothingIsUnpackedThroughAStagingFolderThatIsALink()
    {
        var outside = Path.Combine(_root, "outside");
        Junction(Path.Combine(Modules, ModuleInstaller.StagingDirectoryName), outside);
        var (installer, github) = Installer();

        var problem = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.StageFromGitHubAsync("https://github.com/someone/sample", default));

        Assert.Contains("is a link", problem.Message);
        Assert.Empty(Directory.GetFileSystemEntries(outside));
        Assert.DoesNotContain(github.Asked, u => u.AbsolutePath.Contains("/download/", StringComparison.Ordinal));
    }

    /// <summary>片付けの置き場（<c>.trash</c>）がリンクなら、更新を入れない（前の版をリンクの先へ移してしまうので）。</summary>
    [Fact]
    public async Task AnUpdateIsNotInstalledThroughATrashFolderThatIsALink()
    {
        var old = Path.Combine(Modules, "sample");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "old.dll"), "old");
        var outside = Path.Combine(_root, "outside");
        Junction(Path.Combine(Modules, PendingRemovals.TrashDirectoryName), outside);
        var (installer, _) = Installer();
        Assert.True(installer.Confirm((await installer.StageFromGitHubAsync("https://github.com/someone/sample", default)).Token));

        Assert.Empty(PendingInstalls.Apply(User, Path.Combine(_root, "app"), NullLogger.Instance));

        Assert.True(File.Exists(Path.Combine(old, "old.dll")));
        Assert.Empty(Directory.GetFileSystemEntries(outside));
    }

    [Fact]
    public void NothingIsInstalledIntoAnotherSpellingOfTheBundledFolder()
    {
        var bundled = Path.Combine(_root, "app");
        var pending = Path.Combine(bundled, "modules", ModuleInstaller.PendingDirectoryName, "sample");
        Directory.CreateDirectory(pending);
        File.WriteAllText(Path.Combine(pending, "module.json"), Manifest);
        var alias = Path.Combine(_root, "alias");
        Junction(alias, bundled);

        Assert.Empty(PendingInstalls.Apply(alias, bundled, NullLogger.Instance));
        Assert.True(Directory.Exists(pending));
        Assert.False(Directory.Exists(Path.Combine(bundled, "modules", "sample")));
    }

    /// <summary>http を許すのは、差し替えが loopback を指すときだけ（本物の GitHub へは https でしか出ない）。</summary>
    [Fact]
    public async Task PlainHttpIsRefusedUnlessTheApiIsALocalStandIn()
    {
        var (installer, _) = Installer(new Uri("http://example.com/"));

        var problem = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.StageFromGitHubAsync("https://github.com/someone/sample", default));
        Assert.Contains("only https", problem.Message);
    }

    [Fact]
    public void LeftoverStagingIsClearedAndNothingHappensWhenTheUserFolderIsTheBundledOne()
    {
        var leftover = Path.Combine(Modules, ModuleInstaller.StagingDirectoryName, "abc");
        Directory.CreateDirectory(leftover);
        var pending = Path.Combine(Modules, ModuleInstaller.PendingDirectoryName, "sample");
        Directory.CreateDirectory(pending);

        Assert.Empty(PendingInstalls.Apply(User, User, NullLogger.Instance));
        Assert.True(Directory.Exists(pending), "with the user folder being the bundled one, nothing is installed");

        PendingInstalls.Apply(User, Path.Combine(_root, "app"), NullLogger.Instance);
        Assert.False(Directory.Exists(leftover));
    }
}
