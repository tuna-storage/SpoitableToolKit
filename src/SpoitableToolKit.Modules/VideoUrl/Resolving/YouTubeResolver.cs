using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Abstractions.Tools;
using SpoitableToolKit.Modules.VideoUrl.Config;

namespace SpoitableToolKit.Modules.VideoUrl.Resolving;

/// <summary>
/// yt-dlp で YouTube のストリーム URL を取る。
/// <para>
/// VRChat のプレイヤーに渡せるのは <b>URL 1 本</b>で、映像と音声を自前で合成することはできない。
/// したがって「1 本で音まで出る」ものだけが実用になる。候補は 2 系統:
/// </para>
/// <list type="number">
///   <item>
///     <b>HLS の muxed プレイリスト</b>（<c>…/api/manifest/hls_playlist/…/itag/96/…</c>、itag 91〜96 / 300 / 301）。
///     セグメント（.ts）に H.264 と AAC が両方入っているので、1 本で音付き・最大 1080p。
///     HLS を解釈できるプレイヤー限定（AVPro は可、Unity Video Player は不可）。
///   </item>
///   <item>
///     <b>progressive な muxed mp4</b>（itag 18）。360p 止まりだが、どちらのプレイヤーでも再生できる。
///   </item>
/// </list>
/// <para>
/// <b>マスタープレイリスト（<c>hls_variant</c>）は渡さない。</b> 1 本で 4K まで出そうに見えるが、
/// 音声が別レンディション（<c>EXT-X-MEDIA</c> の AUDIO グループ）になっていて、AVPro は読み込みに失敗する
/// （実測: <c>[AVProVideo] Error: Loading failed.</c>）。
/// VRChat 自身が成功している 203 件も、全て <c>hls_playlist</c> 側（itag 300/96/95/301/94）だった。
/// </para>
/// <para>
/// デコードはしないので ffmpeg は要らない。
/// </para>
/// <para>
/// ここが持つのは<b>順番だけ</b>: 取る（<see cref="ResolverServerClient"/> か <see cref="YtDlpRunner"/>）→ 選ぶ（<see cref="StreamPicker"/>）→
/// 確かめる（<see cref="StreamProbe"/>）→ 名前と注意書きを付けて返す。
/// </para>
/// </summary>
internal sealed class YouTubeResolver(IExternalToolProvider tools, ILocalizer text, ILogger logger, VideoUrlConfig config)
{
    private readonly ResolverServerClient _server = new(text, logger);
    private readonly YtDlpRunner _runner = new(logger);
    private readonly StreamProbe _probe = new(logger);

    public async Task<ResolveResult> ResolveAsync(string videoUrl, CancellationToken cancellationToken)
    {
        // yt-dlp の引数に流し込むので、URL の形をしていないものは弾く
        // （"-" で始まる文字列をそのまま渡すとオプションとして解釈される）。
        if (!Uri.TryCreate(videoUrl, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(text["videourl.error.notHttp"], nameof(videoUrl));
        }

        // 外へ頼んだときの断り文句。両方とも取れなかったときだけ、その理由を人に見せる。
        List<string> problems = [];

        // **どちらで取るかはここで決まり、以降は同じ道を通る。** 取り方が違うだけで、
        // 選別も探りも文言もまったく同じものが要る。
        Func<string, Task<JsonDocument?>> fetch;

        if (config.Server.Enabled)
        {
            // **外を使う間は、手元の道具を取りに行かない。** 手元に yt-dlp と deno を
            // 置かずに済むことがこの機能の値打ちそのものなので、取りに行った時点で意味が消える。
            var endpoint = _server.EndpointOf(config.Server);

            fetch = client => _server.AskAsync(endpoint, config.Server, parsed, client, problems, cancellationToken);
        }
        else
        {
            var ytDlp = await tools.GetAsync(ExternalTools.YtDlp, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidOperationException(text["videourl.error.noYtDlp"]);

            // nsig を解けないと、URL は返るのに再生時に HTTP 403 で弾かれる。
            // deno が無くても解決自体は「成功」してしまうので、ここで警告を残しておく。
            var deno = await tools.GetAsync(ExternalTools.Deno, cancellationToken).ConfigureAwait(false);
            if (deno is null)
                logger.LogWarning("No JS runtime; the returned URL will most likely be rejected with HTTP 403");

            fetch = client => _runner.RunJsonAsync(ytDlp, deno, parsed, client, cancellationToken);
        }

        // HLS と progressive は取れるクライアントが違うので、2 回叩く必要がある。
        // 直列だと待ち時間が倍になるだけなので同時に走らせる（手元で 1 回 3 秒前後）。
        //
        // **どちらを名乗るかは設定から来る**（既定は実測で決めたもの。<see cref="PlayerClients"/>）。
        // 空にした方は頼まない — 片方で足りる人が要求を半分にできる。
        var hls = config.Clients.Hls.Trim();
        var progressive = config.Clients.Progressive.Trim();

        if (hls.Length == 0 && progressive.Length == 0)
            throw new InvalidOperationException(text["videourl.error.noClients"]);

        var hlsTask = hls.Length > 0 ? fetch(hls) : Task.FromResult<JsonDocument?>(null);
        var progressiveTask = progressive.Length > 0 ? fetch(progressive) : Task.FromResult<JsonDocument?>(null);

        using var hlsInfo = await hlsTask.ConfigureAwait(false);
        using var progressiveInfo = await progressiveTask.ConfigureAwait(false);

        // 両方の待ち合わせが済んでいるので、断り文句はもう増えない。
        if (hlsInfo is null && progressiveInfo is null)
            throw new InvalidOperationException(problems.FirstOrDefault() ?? text["videourl.error.noInfo"]);

        List<ResolvedStream> streams = [];

        if (hlsInfo is not null) StreamPicker.AddHlsMuxed(StreamPicker.Formats(hlsInfo.RootElement), streams, text);

        if (progressiveInfo is not null) StreamPicker.AddMuxed(StreamPicker.Formats(progressiveInfo.RootElement), streams, text);

        // 音声のみは出していない。web_safari も android も audio-only を返さず（実測:
        // formats 11 本 / 5 本のいずれにも無し）、既定クライアントをもう 1 回叩くことになる。
        // 音楽用途で要るようになったら 3 本目として並列で足す。

        var found = streams.Count;
        streams = await _probe.KeepPlayableAsync(streams, cancellationToken).ConfigureAwait(false);

        // どちらのクライアントが何を返したか。「高画質が出ない」「音が出ない」の切り分けに要る。
        logger.LogInformation(
            "Video: {Hls} gave {HlsState}, {Progressive} gave {ProgressiveState}; {Found} stream(s) found, {Kept} kept after the GET check",
            hls.Length > 0 ? hls : "(no HLS client)", hlsInfo is null ? "nothing" : "formats",
            progressive.Length > 0 ? progressive : "(no progressive client)", progressiveInfo is null ? "nothing" : "formats",
            found, streams.Count);

        if (streams.Count == 0)
            throw new InvalidOperationException(text["videourl.error.noStream"]);

        var title = (hlsInfo is not null ? JsonFields.Text(hlsInfo.RootElement, "title") : null)
                    ?? (progressiveInfo is not null ? JsonFields.Text(progressiveInfo.RootElement, "title") : null);

        // 「高画質が出ない = 壊れている」と見えないように、動画側の事情だと分かる形で伝える。
        // HLS muxed を持たない動画は実在する（実測、PoogGcJjsFU は
        // web / web_safari / ios / tv / mweb / android / web_embedded のどれでも HLS muxed 無し）。
        var notice = streams.Any(s => s.Kind == "hls")
            ? null
            : text["videourl.note.noHls"];

        return new ResolveResult(title, streams, notice);
    }
}
