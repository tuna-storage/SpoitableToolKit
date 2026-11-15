using System.Security.Cryptography;
using System.Text.Json;

namespace SpoitableToolKit.E2E.Updates;

/// <summary>
/// <c>SPOITABLE_UPDATE_FEED</c> で、アプリはインストールした版（stable）のふりをして、そのフォルダの <c>releases.stable.json</c> を読み、
/// 「更新」では入れる代わりに <c>applied.txt</c> に版を書く（本物の GitHub にも Update.exe にも触らない）。
/// </summary>
public class UpdateFeedAppFixture : AppFixture
{
    public const string NewVersion = "2099.1.0";

    public const string JapaneseLine = "手首の板が速くなりました";

    public const string EnglishLine = "The wrist board is faster";

    private const string Notes = $"""
        <!-- lang: ja -->
        # SpoitableToolKit {NewVersion}

        ## VR
        - {JapaneseLine}
        - **設定**が増えました

        <!-- lang: en -->
        # SpoitableToolKit {NewVersion}

        ## VR
        - {EnglishLine}
        """;

    public string Feed => Path.Combine(DataDirectory, "update-feed");

    public string AppliedRecord => Path.Combine(Feed, "applied.txt");

    public string Packages => Path.Combine(DataDirectory, "update-e2e", "packages");

    protected override void Prepare()
    {
        Directory.CreateDirectory(Feed);

        // 中身は何でもよい（Velopack は大きさと hash で確かめる。入れはしない）。
        var package = $"SpoitableToolKit.App-{NewVersion}-stable-full.nupkg";
        var bytes = System.Text.Encoding.UTF8.GetBytes(new string('x', 64 * 1024));
        File.WriteAllBytes(Path.Combine(Feed, package), bytes);

        var feed = new
        {
            Assets = new[]
            {
                new
                {
                    PackageId = "SpoitableToolKit.App",
                    Version = NewVersion,
                    Type = "Full",
                    FileName = package,
                    SHA1 = Convert.ToHexString(SHA1.HashData(bytes)),
                    SHA256 = Convert.ToHexString(SHA256.HashData(bytes)),
                    Size = (long)bytes.Length,
                    NotesMarkdown = Notes,
                    NotesHtml = "",
                },
            },
        };
        File.WriteAllText(Path.Combine(Feed, "releases.stable.json"), JsonSerializer.Serialize(feed));

        ExtraEnvironment["SPOITABLE_UPDATE_FEED"] = Feed;
        ExtraEnvironment["SPOITABLE_UPDATE_FIRST_CHECK_SECONDS"] = "0";
    }
}
