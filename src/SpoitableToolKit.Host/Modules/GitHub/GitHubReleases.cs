using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpoitableToolKit.Host.Modules.Packaging;

namespace SpoitableToolKit.Host.Modules.GitHub;

/// <summary>
/// GitHub の Releases を読む。<b>読むのは公開の repo だけ</b>（トークンは持たない）。
/// <para>
/// e2e は本物の GitHub へ出ない: <c>SPOITABLE_GITHUB_API=http://127.0.0.1:&lt;port&gt;/</c> で手元の偽物へ向ける。
/// <b>http を許すのはこの差し替えが自分の機械（loopback）を指すときだけ</b>。添付ファイルの URL も同じ決まりで確かめる。
/// </para>
/// </summary>
internal sealed class GitHubReleases(HttpClient http, Uri api)
{
    public static readonly Uri DefaultApi = new("https://api.github.com/");

    /// <summary>
    /// GitHub へ名乗る名前。<b>無いと API は 403 を返す</b>。アプリの名前と版だけ（使っている人のことは何も入れない）。
    /// </summary>
    public static readonly string UserAgent =
        $"SpoitableToolKit/{typeof(GitHubReleases).Assembly.GetName().Version?.ToString(3) ?? "0"}";

    /// <summary>落とす大きさの上限。モジュール 1 つでこれを超えるものは無いはず。</summary>
    public const long MaxDownloadBytes = 256L * 1024 * 1024;

    /// <summary>
    /// tag の長さの上限。<b>tag と添付ファイルの名前は repo の持ち主が選べて</b>、確認の板の「入れ元」の行・一覧・断る文・ログにそのまま出る。
    /// module.json の名前と同じ決まり（<see cref="ModuleManifest.IsPlainLine"/>: 1 行・見える字だけ）を掛けて、落とす前に断る。
    /// </summary>
    public const int MaxTagLength = 128;

    /// <summary>添付ファイルの名前の長さの上限（build が付ける名前は <c>&lt;Id&gt;-&lt;版&gt;-module.zip</c> で、いちばん長くて 140 字）。</summary>
    public const int MaxAssetNameLength = 255;

    private static readonly System.Text.RegularExpressions.Regex Sha256Hex =
        new(@"^[0-9A-Fa-f]{64}\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>commit の id（40 桁の 16 進）。URL・<c>.source.json</c>・画面へ渡す前に、この形だけを受ける。</summary>
    private static readonly System.Text.RegularExpressions.Regex CommitId =
        new(@"^[0-9a-f]{40}\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static bool IsCommitId(string text) => CommitId.IsMatch(text);

    public static Uri ApiFromEnvironment() => ApiFrom(Environment.GetEnvironmentVariable("SPOITABLE_GITHUB_API"));

    /// <summary>
    /// 差し替え（<c>SPOITABLE_GITHUB_API</c>）を受けるのは、<b>自分の機械（loopback）を指すときだけ</b>。それ以外は本物の GitHub。
    /// よその場所を指せると、GitHub 以外からモジュールを落とさせる口になる。
    /// </summary>
    internal static Uri ApiFrom(string? custom) =>
        custom is { Length: > 0 } && Uri.TryCreate(custom, UriKind.Absolute, out var uri) && uri.IsLoopback ? uri : DefaultApi;

    public async Task<GitHubRelease> LatestAsync(GitHubRepository repository, CancellationToken cancellationToken)
    {
        var url = new Uri(api, $"repos/{Uri.EscapeDataString(repository.Owner)}/{Uri.EscapeDataString(repository.Name)}/releases/latest");
        EnsureAllowed(url);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd(UserAgent);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new NoReleaseException($"{repository} has no published release (or the repository is private or does not exist)");
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException($"GitHub refused the request for {repository} ({(int)response.StatusCode}); it limits how often releases can be read, so try again later");
        response.EnsureSuccessStatusCode();

        ReleaseBody? body;
        try
        {
            body = await response.Content.ReadFromJsonAsync<ReleaseBody>(cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            // JSON でない（途中の proxy の案内のページなど）・壊れている。型が text/html でも .NET 10 は読もうとして、ここへ来る。
            throw new InvalidDataException($"The answer for {repository} is not a GitHub release ({ex.Message})");
        }

        if (body is null) throw new InvalidDataException("GitHub returned an empty release");

        var tag = body.Tag ?? throw new InvalidDataException("GitHub returned a release without a tag");
        if (!ModuleManifest.IsPlainLine(tag, MaxTagLength))
            throw new InvalidDataException(
                $"The latest release of {repository} has a tag the app does not accept (\"{ModuleManifest.Shown(tag)}\"): " +
                $"a tag must be one line of at most {MaxTagLength} characters, with no control or invisible formatting characters");

        var named = (body.Assets ?? []).Where(a => a.Name is { Length: > 0 }).ToArray();
        if (named.FirstOrDefault(a => !ModuleManifest.IsPlainLine(a.Name!, MaxAssetNameLength)) is { } odd)
            throw new InvalidDataException(
                $"The release {tag} of {repository} has an attached file whose name the app does not accept (\"{ModuleManifest.Shown(odd.Name!)}\"): " +
                $"a file name must be one line of at most {MaxAssetNameLength} characters, with no control or invisible formatting characters");

        var assets = named
            .Where(a => Uri.TryCreate(a.Url, UriKind.Absolute, out _))
            .Select(a => new ReleaseAsset(a.Name!, new Uri(a.Url!), a.Size, Sha256Of(a.Digest)))
            .ToArray();

        return new GitHubRelease(tag, assets);
    }

    /// <summary>
    /// GitHub が出したハッシュ（<c>sha256:&lt;16 進 64 桁&gt;</c>）。その形でなければ「出していない」として扱う
    /// （合わないときの文にそのまま入るので、16 進のほかは受けない。URL と同じ答えの中の値なので、無いものとして扱っても守りは変わらない）。
    /// </summary>
    private static string? Sha256Of(string? digest) =>
        digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && Sha256Hex.IsMatch(digest[7..]) ? digest[7..] : null;

    /// <summary>
    /// 既定の branch の先頭の commit（40 桁の 16 進）。<b>branch の名前は聞かない</b>（repo の持ち主が選べる字で、<c>/</c> も入る）:
    /// <c>commits/HEAD</c> は既定の branch の先頭を返し、<c>Accept: application/vnd.github.sha</c> なら id だけが返る。
    /// </summary>
    public async Task<string> HeadCommitAsync(GitHubRepository repository, CancellationToken cancellationToken)
    {
        var url = new Uri(api, $"repos/{Uri.EscapeDataString(repository.Owner)}/{Uri.EscapeDataString(repository.Name)}/commits/HEAD");
        EnsureAllowed(url);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/vnd.github.sha");
        request.Headers.UserAgent.ParseAdd(UserAgent);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        // 409 は、commit が 1 つも無い repo。
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
            throw new InvalidOperationException($"{repository} has no code to build (or the repository is private or does not exist)");
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException($"GitHub refused the request for {repository} ({(int)response.StatusCode}); it limits how often a repository can be read, so try again later");
        response.EnsureSuccessStatusCode();

        // 返るのは id だけのはず。それより長い答えは読まない。
        var buffer = new byte[64];
        var read = 0;
        await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            int got;
            while (read < buffer.Length && (got = await body.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false)) > 0)
                read += got;
        }

        var commit = System.Text.Encoding.ASCII.GetString(buffer, 0, read).Trim();
        if (!IsCommitId(commit)) throw new InvalidDataException($"The answer for {repository} is not a commit id");
        return commit;
    }

    /// <summary>
    /// その commit のソース（zip）を落とす。<b>branch ではなく commit を指して落とす</b>（利用者に見せた commit を build する。聞いたあとで branch が動いても変わらない）。
    /// </summary>
    /// <returns>落としたものの SHA-256（16 進、大文字）。</returns>
    public Task<string> DownloadSourceAsync(GitHubRepository repository, string commit, Stream target, CancellationToken cancellationToken)
    {
        if (!IsCommitId(commit)) throw new ArgumentException("not a commit id", nameof(commit));

        var url = new Uri(api, $"repos/{Uri.EscapeDataString(repository.Owner)}/{Uri.EscapeDataString(repository.Name)}/zipball/{commit}");
        return DownloadAsync(url, $"The source of {repository}", expected: null, target, cancellationToken);
    }

    /// <summary>
    /// 入れる zip を選ぶ。<b>zip が 1 つならそれ</b>。いくつもあるなら名前が <c>-module.zip</c> で終わるものが 1 つだけのときそれ。
    /// それ以外は選ばずに理由を投げる（勝手に選ぶと、別のものを入れうる）。
    /// </summary>
    public static ReleaseAsset ChooseAsset(GitHubRelease release)
    {
        var zips = release.Assets.Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (zips.Length == 1) return zips[0];

        var marked = zips.Where(a => a.Name.EndsWith("-module.zip", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (marked.Length == 1) return marked[0];

        throw new InvalidDataException(zips.Length == 0
            ? $"The release {release.Tag} has no .zip attached"
            : $"The release {release.Tag} has {zips.Length} .zip files; name the module's one '<something>-module.zip'");
    }

    /// <summary>落として <paramref name="target"/> へ書く。大きさの上限を守り、GitHub が SHA-256 を出していれば突き合わせる。</summary>
    /// <returns>落としたものの SHA-256（16 進、大文字）。</returns>
    /// <param name="progress">落とした量（バイト）。およそ 256 KB ごとと、落とし終えたときに知らせる（画面の進み表示用）。</param>
    public Task<string> DownloadAsync(ReleaseAsset asset, Stream target, CancellationToken cancellationToken, IProgress<long>? progress = null)
    {
        if (asset.Size > MaxDownloadBytes)
            throw new InvalidDataException($"{asset.Name} is {asset.Size / 1024 / 1024} MB; at most {MaxDownloadBytes / 1024 / 1024} MB is accepted");

        return DownloadAsync(asset.Download, asset.Name, asset.Sha256, target, cancellationToken, progress);
    }

    private const long ProgressStep = 256 * 1024;

    /// <summary><paramref name="what"/> は断る文での呼び名（添付ファイルの名前・「The source of …」）。</summary>
    private async Task<string> DownloadAsync(
        Uri url, string what, string? expected, Stream target, CancellationToken cancellationToken, IProgress<long>? progress = null)
    {
        EnsureAllowed(url);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // 転送の先（GitHub は別のホストへ回す）も、同じ決まりで確かめる。
        if (response.RequestMessage?.RequestUri is { } landed) EnsureAllowed(landed);

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        long reported = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaxDownloadBytes)
                throw new InvalidDataException($"{what} is larger than {MaxDownloadBytes / 1024 / 1024} MB");

            hash.AppendData(buffer, 0, read);
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

            if (total - reported < ProgressStep) continue;
            reported = total;
            progress?.Report(total);
        }

        if (reported != total) progress?.Report(total);

        var sha = Convert.ToHexString(hash.GetHashAndReset());
        if (expected is not null && !string.Equals(expected, sha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{what} does not match the SHA-256 GitHub published ({expected}); got {sha}");

        return sha;
    }

    /// <summary>https だけ。http は、差し替え（<see cref="api"/>）が loopback を指していて、行き先も loopback のときだけ。</summary>
    private void EnsureAllowed(Uri url)
    {
        if (url.Scheme == Uri.UriSchemeHttps) return;
        if (url.Scheme == Uri.UriSchemeHttp && api.IsLoopback && url.IsLoopback) return;
        throw new InvalidOperationException($"Refusing to fetch {url}: only https is allowed");
    }

    private sealed class ReleaseBody
    {
        [JsonPropertyName("tag_name")] public string? Tag { get; init; }
        [JsonPropertyName("assets")] public List<AssetBody>? Assets { get; init; }
    }

    private sealed class AssetBody
    {
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("browser_download_url")] public string? Url { get; init; }
        [JsonPropertyName("size")] public long Size { get; init; }
        [JsonPropertyName("digest")] public string? Digest { get; init; }
    }
}
