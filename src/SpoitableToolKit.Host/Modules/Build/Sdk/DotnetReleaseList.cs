using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpoitableToolKit.Host.Modules.Build.Sdk;

/// <summary>
/// 公式の一覧（release-metadata の JSON）を読み、<b>いま落とすことになる SDK</b> の zip の URL と SHA-512 を選ぶ。<b>ここでは落とさない</b>。
/// </summary>
/// <param name="http">一覧を読む口（zip の大きさも聞く）。</param>
/// <param name="metadata">一覧の場所（<see cref="DotnetSdks.MetadataFrom"/>）。</param>
internal sealed class DotnetReleaseList(HttpClient http, Uri metadata)
{
    /// <summary>一覧の JSON の上限（実物は 1.2 MB）。</summary>
    public const long MaxMetadataBytes = 8L * 1024 * 1024;

    public const string Rid = "win-x64";

    /// <summary>SDK の版の形（<c>10.0.404</c>）。落とす物を受け取る側（<see cref="DotnetSdkInstaller"/>）も、これで見直す。</summary>
    internal static readonly Regex VersionShape = new(@"^[0-9]{1,4}\.[0-9]{1,4}\.[0-9]{1,6}\z", RegexOptions.CultureInvariant);

    /// <summary>SHA-512 の形（16 進 128 桁）。</summary>
    internal static readonly Regex Sha512Hex = new(@"^[0-9A-Fa-f]{128}\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// いま落とすことになる SDK（一覧の <c>latest-sdk</c> の、<see cref="Rid"/> の zip）。<b>ここでは落とさない</b> — 利用者に版と大きさを見せて聞くため。
    /// </summary>
    public async Task<DotnetSdkDownload> LatestAsync(CancellationToken cancellationToken)
    {
        // 一覧そのものも、取りに行ってよい場所の決まりで見る。
        DotnetDownloadSites.EnsureAllowed(metadata, metadata);

        byte[] json;
        using (var response = await http.GetAsync(metadata, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();

            // 一覧は、決めた場所そのものから読む（転送でよそのホストへ回されたものは読まない。
            // zip の「一覧と同じホストだけ」の決まりは、一覧を返した相手が決めた場所であることに頼っている）。
            if (response.RequestMessage?.RequestUri is { } landed && !DotnetDownloadSites.IsSameSite(landed, metadata))
                throw new InvalidOperationException($"Refusing to read the list of .NET releases from {landed.Host}: it was redirected away from {metadata.Host}");

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxMetadataBytes)
                    throw new InvalidDataException($"The list of .NET releases is larger than {MaxMetadataBytes / 1024 / 1024} MB");
                buffer.Write(chunk, 0, read);
            }

            json = buffer.ToArray();
        }

        var download = Choose(json, metadata);

        // 大きさは、利用者に見せるためだけに聞く（守りは、落とすときの上限と SHA-512）。
        long bytes = 0;
        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, download.Url);
            using var response = await http.SendAsync(head, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) bytes = response.Content.Headers.ContentLength ?? 0;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // 大きさが分からないだけ（繋がらない・時間切れ）。利用者が止めたのなら、そのまま止める。
        }

        return download with { Bytes = bytes };
    }

    /// <summary>
    /// 一覧の JSON から、落とす zip を選ぶ。<c>latest-sdk</c> の版を持つ <c>sdk</c>（または <c>sdks[]</c>）の <c>files[]</c> のうち、
    /// <c>rid</c> が <see cref="Rid"/> で名前が <c>.zip</c> で終わるもの。URL は https で、一覧と同じホストだけ。<c>hash</c> は SHA-512（16 進 128 桁）。
    /// </summary>
    internal static DotnetSdkDownload Choose(byte[] json, Uri metadata)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (Text(root, "latest-sdk") is not { } latest || !VersionShape.IsMatch(latest))
                throw new InvalidDataException("The list of .NET releases does not name a latest SDK");

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("releases", out var releases) && releases.ValueKind == JsonValueKind.Array)
            {
                foreach (var sdk in releases.EnumerateArray().SelectMany(Sdks))
                {
                    if (Text(sdk, "version") != latest) continue;
                    if (!sdk.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array) continue;

                    foreach (var file in files.EnumerateArray())
                    {
                        if (Text(file, "rid") != Rid || Text(file, "name") is not { } name || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (Text(file, "hash") is not { } hash || !Sha512Hex.IsMatch(hash))
                            throw new InvalidDataException($"The list of .NET releases has no SHA-512 for the SDK {latest}");

                        if (!Uri.TryCreate(Text(file, "url"), UriKind.Absolute, out var url) || !DotnetDownloadSites.IsAllowed(url, metadata)
                            || !DotnetDownloadSites.IsSameSite(url, metadata))
                            throw new InvalidDataException($"The list of .NET releases points the SDK {latest} at a place the app does not download from");

                        return new DotnetSdkDownload(latest, url, hash.ToUpperInvariant(), 0);
                    }
                }
            }

            throw new InvalidDataException($"The list of .NET releases has no {Rid} zip for the SDK {latest}");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The list of .NET releases could not be read ({ex.Message})");
        }

        static IEnumerable<JsonElement> Sdks(JsonElement release)
        {
            if (release.ValueKind != JsonValueKind.Object) yield break;

            if (release.TryGetProperty("sdk", out var sdk) && sdk.ValueKind == JsonValueKind.Object) yield return sdk;
            if (!release.TryGetProperty("sdks", out var sdks) || sdks.ValueKind != JsonValueKind.Array) yield break;

            foreach (var other in sdks.EnumerateArray())
                if (other.ValueKind == JsonValueKind.Object) yield return other;
        }

        static string? Text(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
