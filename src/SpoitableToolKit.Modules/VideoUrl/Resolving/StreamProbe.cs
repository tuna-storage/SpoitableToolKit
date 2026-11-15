using Microsoft.Extensions.Logging;

namespace SpoitableToolKit.Modules.VideoUrl.Resolving;

/// <summary>
/// 渡す前に、<b>素の GET で本当に取れる URL か</b>を確かめて落とす。
/// <para>
/// yt-dlp が返した時点では成否が分からない。クライアント文脈に紐付いた URL は yt-dlp 自身なら
/// 落とせるので手元では気付けず、URL だけ渡す VRChat のプレイヤーで初めて 403 になる。
/// ここで 1 回だけ小さく叩いておけば、UI に出るのは実際に再生できるものだけになる。
/// </para>
/// </summary>
internal sealed class StreamProbe(ILogger logger)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>取れたもの（200 か 206）だけを、渡された順で返す。</summary>
    public async Task<List<ResolvedStream>> KeepPlayableAsync(
        List<ResolvedStream> streams, CancellationToken cancellationToken)
    {
        var checks = await Task.WhenAll(streams.Select(async stream =>
        {
            var status = await ProbeAsync(stream.Url, cancellationToken).ConfigureAwait(false);
            var playable = status is System.Net.HttpStatusCode.OK or System.Net.HttpStatusCode.PartialContent;

            if (!playable)
                logger.LogWarning("Not returning the {Kind} URL: a plain GET gets {Status}", stream.Kind, status);

            return (stream, playable);
        })).ConfigureAwait(false);

        return [.. checks.Where(c => c.playable).Select(c => c.stream)];
    }

    private async Task<System.Net.HttpStatusCode?> ProbeAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1023);

            using var response = await Http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            return response.StatusCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug("Could not verify the URL: {Message}", ex.Message);
            return null;
        }
    }
}
