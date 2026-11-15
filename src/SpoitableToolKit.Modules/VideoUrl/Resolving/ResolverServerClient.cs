using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Modules.VideoUrl.Config;

namespace SpoitableToolKit.Modules.VideoUrl.Resolving;

/// <summary>
/// 解決を外のサーバーへ頼む口（設定の「サーバー」）。<b>返ってくるのは <c>yt-dlp -J</c> の生の JSON</b> なので、
/// 手元で走らせたとき（<see cref="YtDlpRunner"/>）と同じものがそのまま後段へ流れる。
/// </summary>
internal sealed class ResolverServerClient(ILocalizer text, ILogger logger)
{
    /// <summary>
    /// 外のサーバーへ聞く口。<b>gzip を自分で解く</b> — 生の JSON は 100KB を超えるのでサーバーは
    /// gzip で返すが、既定の <see cref="HttpClient"/> は <c>Accept-Encoding</c> すら付けない。
    /// <para>
    /// <b>待ち時間はここでは決めない。</b> 設定の秒数で毎回変わるので、呼ぶ側の token に任せる
    /// （ここに秒数を置くと、設定を変えても次の起動まで効かない）。
    /// </para>
    /// </summary>
    private static readonly HttpClient Api =
        new(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };

    /// <summary>
    /// 解決を頼む先。<b>打ち込まれた URL をそのまま使う</b>。
    /// <para>
    /// <c>/resolve</c> のような口の名前をこちらで足さない。<b>サーバーを建てるのは利用者自身</b>で、
    /// どの道に生やすかも前に何を挟むかもこちらの決めることではない。
    /// </para>
    /// </summary>
    public Uri EndpointOf(ServerSettings server)
    {
        var url = server.Url.Trim();

        if (url.Length == 0
            || !Uri.TryCreate(url, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(text["videourl.error.serverUrl"]);
        }

        return endpoint;
    }

    /// <summary>
    /// 外のサーバーに 1 回頼む。
    /// <para>
    /// 失敗しても投げずに null を返す。<b>2 本のうち片方だけ落ちる形が普通にある</b>ので、
    /// 片方の失敗で全部を諦めない（手元で走らせる道と同じ約束）。理由は <paramref name="problems"/> に残す。
    /// </para>
    /// </summary>
    public async Task<JsonDocument?> AskAsync(
        Uri endpoint, ServerSettings server, Uri videoUrl, string playerClient,
        List<string> problems, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(server.TimeoutSeconds));

        try
        {
            var body = JsonSerializer.Serialize(new { url = videoUrl.AbsoluteUri, client = playerClient });

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };

            // **空なら付けない。** token を要求しないサーバー（待ち受けを localhost に寄せて
            // SSH のトンネル越しに使う形）も、同じ設定のまま通るようにするため。
            if (!string.IsNullOrWhiteSpace(server.Token))
            {
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", server.Token.Trim());
            }

            using var response = await Api.SendAsync(request, timeout.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // **サーバーが何と言ったかをそのまま運ぶ。** 状態だけでは直しようがない。
                var said = await SaidAsync(response, timeout.Token).ConfigureAwait(false);

                logger.LogWarning("The resolver refused {Client}: {Said}", playerClient, said);

                // 401 だけ分けるのは、**打ち直す先が分かる唯一の失敗**だから（token の欄）。
                Note(problems, response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? text.Format("videourl.error.serverUnauthorized", said)
                    : text.Format("videourl.error.serverFailed", said));

                return null;
            }

            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not ask the resolver for {Client}", playerClient);

            // 待ち時間を使い切ったときは、秒数を伸ばせば済む話なのでそう分かる形にする。
            Note(problems, ex is OperationCanceledException
                ? text.Format("videourl.error.serverFailed", $"{server.TimeoutSeconds}s")
                : text.Format("videourl.error.serverFailed", ex.Message));

            return null;
        }
    }

    /// <summary>
    /// サーバーが何と言って断ったか。<b>状態に本文を添えて返す</b>。
    /// <para>
    /// 状態だけだと直しようがない。400 は「何かが形を満たしていない」としか言っておらず、
    /// <b>url なのか client なのかはサーバーしか知らない</b>。
    /// </para>
    /// <para>
    /// 仕様の形（<c>{"error": ..., "message": ...}</c>）なら中身を拾う。
    /// <b>そうでない返事もそのまま出す</b> — 手前のプロキシが返した HTML かもしれず、
    /// 「読めなかったので黙る」より「読めたものを見せる」方が切り分けに効く。
    /// </para>
    /// </summary>
    private static async Task<string> SaidAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = ((int)response.StatusCode).ToString();

        string body;

        try
        {
            body = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return status;
        }

        if (body.Length == 0) return status;

        try
        {
            using var json = JsonDocument.Parse(body);

            if (json.RootElement.ValueKind == JsonValueKind.Object
                && (JsonFields.Text(json.RootElement, "message") ?? JsonFields.Text(json.RootElement, "error")) is { Length: > 0 } said)
            {
                body = said;
            }
        }
        catch (JsonException)
        {
            // JSON でないならそのまま出す。
        }

        return $"{status}: {LogText.Trim(body)}";
    }

    /// <summary>
    /// 断り文句を溜める。<b>2 本が同時に書く</b>ので鍵を掛ける。
    /// 同じ理由は 1 回だけ（両方が同じ 401 を返したときに 2 行出しても仕方がない）。
    /// </summary>
    private static void Note(List<string> problems, string message)
    {
        lock (problems)
        {
            if (!problems.Contains(message)) problems.Add(message);
        }
    }
}
