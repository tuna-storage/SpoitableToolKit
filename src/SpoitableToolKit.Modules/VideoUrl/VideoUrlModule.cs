using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Events.Video;
using SpoitableToolKit.Abstractions.Notifications;
using SpoitableToolKit.Abstractions.Ui;
using SpoitableToolKit.Modules.VideoUrl.Config;
using SpoitableToolKit.Modules.VideoUrl.Failures;
using SpoitableToolKit.Modules.VideoUrl.Resolving;
using SpoitableToolKit.Modules.VideoUrl.Shortcuts;

namespace SpoitableToolKit.Modules.VideoUrl;

/// <summary>
/// 再生失敗した動画のストリーム URL 抽出。
/// <para>
/// 2 つの入口がある。パネルに URL を貼って押す手動の経路と、
/// ログから再生失敗を拾って聞きに行く経路。
/// </para>
/// <code>
/// 再生失敗を検出 → 「解決してコピーする?」 → はい → yt-dlp で解決 → クリップボードへ
/// </code>
/// <para>
/// <b>聞く前に解決しないこと。</b> VRChat はリトライするので 1 本の動画で 4〜5 回
/// 失敗が飛んでくる。先回りして解決していると（実測 1 回 2〜3 秒）、見てもいない動画や
/// 他人が流した動画でも毎回 yt-dlp が走る。<see cref="VideoPlaybackFailedEventParams.IsRepeatedFail"/> で 1 本につき 1 回に絞り、
/// さらにユーザーが承諾したときだけ動かす。
/// </para>
/// </summary>
public sealed class VideoUrlModule : ISpoitableModule, IUiEndpoint, IVideoEventListener
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>パネルに出す失敗の一覧。<b>正はこちら</b>で、パネルは開いたときに取りに来る。</summary>
    private readonly FailureLog _failures = new();

    private readonly VideoUrlConfig _config = new();

    private YouTubeResolver? _resolver;
    private IModuleHost? _host;
    private ILogger _logger = NullLogger.Instance;

    public string? Author => "fltuna";

    public string? Icon => "🎞";

    public string? IconPath => "M5.5 5h13a3 3 0 0 1 3 3v8a3 3 0 0 1-3 3h-13a3 3 0 0 1-3-3V8a3 3 0 0 1 3-3ZM10 9.2l5 2.8-5 2.8Z";

    /// <summary>
    /// サイドバーの行の右に「失敗 N」（このセッションで再生に失敗した動画の数）。<b>失敗が無ければ出さない</b>
    /// （「失敗 0」を常に出すと、何も起きていないのに目に入る）。OnInit で登録する。
    /// </summary>
    private readonly ModuleStatus _status = new();

    internal static readonly NotifyKind FailedKind = new("videourl.failed", NotifyTone.Red,
        "M5.5 5h13a3 3 0 0 1 3 3v8a3 3 0 0 1-3 3h-13a3 3 0 0 1-3-3V8a3 3 0 0 1 3-3ZM10 9.2l5 2.8-5 2.8ZM3 3l18 18");

    internal static readonly NotifyKind CopiedKind = new("videourl.copied", NotifyTone.Teal,
        "M10 14a4.5 4.5 0 0 0 6.4 0l3-3a4.5 4.5 0 0 0-6.4-6.4l-1 1M14 10a4.5 4.5 0 0 0-6.4 0l-3 3a4.5 4.5 0 0 0 6.4 6.4l1-1");

    /// <summary>インスタンスをホストと共有する。画面で変えた値はここへ直接書かれる。</summary>
    public IModuleConfig? Config => _config;

    private readonly ModuleStatus _resolving = new() { State = StatusState.Ok };

    /// <summary>
    /// いちばん新しい失敗のうち、解ける宛先のもの（<see cref="IsResolvable"/>）を解いてコピーする。無ければそう知らせる。
    /// 手首のタイル（<see cref="ResolveLatestShortcut"/>）から呼ばれる。
    /// </summary>
    internal async Task ResolveLatestAsync(CancellationToken cancellationToken)
    {
        if (_host is not { } host) return;

        if (_failures.Snapshot().FirstOrDefault(failure => IsResolvable(failure.Url)) is not { } latest)
        {
            host.Notifications.PushToast(NotifyKind.Info, host.Text["videourl.shortcut.resolveLatest.none"]);
            return;
        }

        _resolving.Text = host.Text["videourl.shortcut.resolving"];
        try
        {
            await ResolveAndCopyAsync(host, latest.Url).ConfigureAwait(false);
        }
        finally
        {
            _resolving.Text = "";
        }
    }

    public bool OnInit(IModuleHost host)
    {
        _host = host;
        _logger = host.Logger;

        // 画面まわりの登録（OnInit の中だけ）: サイドバーのページ・そのページから呼ばれる口（このクラス）・札。
        host.Ui.RegisterPanel("index.html");
        host.Ui.RegisterEndpoint(this);
        host.Ui.RegisterStatus(_status);

        // 手首のショートカット。パネルを開くもの（本体が用意する）のほかに、いちばん新しい失敗を解いてコピーするもの。
        host.Shortcuts.Register(new ResolveLatestShortcut(this, _resolving));

        _resolver = new YouTubeResolver(host.ExternalTools, host.Text, _logger, _config);

        // 生のログ行ではなくドメインイベントを聞く。リトライの回数合わせはホスト側が済ませてある。
        // 持ち主を明示で渡す。外すのはホストの仕事なので OnShutdown では何もしない。
        host.Videos.AddListener<IVideoEventListener>(this, this);
        return true;
    }

    public void OnShutdown()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    /// <summary>
    /// ログから再生失敗を拾ったとき。<b>ここでは聞くだけ</b>で、解決はユーザーが承諾してから。
    /// </summary>
    public void OnVideoPlaybackFailed(VideoPlaybackFailedEventParams e)
    {
        var (info, isRepeatedFail) = (e.Video, e.IsRepeatedFail);
        if (_host is not { } host) return;

        // 一覧はリトライも含めて全部数える（「4 回失敗している」と出したい）。
        // パネルが開いていなければ push は捨てられるが、一覧そのものは _failures に残る。
        host.Ui.Push(FailuresChannel,JsonSerializer.Serialize(_failures.Record(info), Json));

        // 数えるのは動画の本数（リトライは 1 本に畳まれている）。
        var failed = _failures.Snapshot().Length;
        _status.Text = host.Text.Format("videourl.status.failed", failed);
        _status.State = StatusState.Bad;

        // VRChat は同じ URL で 4〜5 回やり直す。聞くのは最初の 1 回だけ。
        if (isRepeatedFail)
        {
            _logger.LogDebug("Video: VRChat failed {Url} again ({Reason})", info.Url, info.Reason);
            return;
        }

        // **聞いたかどうかと、聞かなかった理由を残す。** 「失敗したのに何も出ない」と言われたときの切り分けに要る。
        var why = !_config.AskOnFailure ? "not asking: turned off in the settings"
            : !IsResolvable(info.Url) ? "not asking: not a site we resolve"
            : "asking whether to resolve it";
        _logger.LogInformation("Video: VRChat could not play {Url} ({Reason}); {Why}", info.Url, info.Reason, why);

        if (!_config.AskOnFailure) return;

        // **確かめた宛先だけ聞く。** 解決の選別そのものはサイトを問わない作りなので
        // （<see cref="YouTubeResolver"/> は vcodec / acodec と protocol しか見ていない）、
        // 他のサイトでも通る見込みはある。それでも絞るのは、確かめていない宛先で聞くと、
        // 外れたときに「解決できませんでした」を出すために人の手を止めるから。
        // 一覧（上の Record）には種類を問わず残るので、何が落ちたかは分かる。
        if (!IsResolvable(info.Url)) return;

        // イベントの配り先を止めない。ここで待つと後続の listener まで詰まる。
        _ = AskThenResolveAsync(host, info.Url, info.Reason);
    }

    /// <summary>
    /// 解決を持ちかけてよい宛先か。<b>YouTube だけ。</b>
    /// <para>
    /// サブドメインは何でも通す（<c>music.youtube.com</c> / <c>m.youtube.com</c> /
    /// <c>www.youtube.com</c>）。<b>後ろに <c>.</c> を付けて見る</b>ので、
    /// <c>notyoutube.com</c> や <c>youtube.com.example.test</c> は通らない
    /// — 前方一致や <c>Contains</c> で書くと、名前が似ているだけの別ドメインまで拾う。
    /// </para>
    /// <para>
    /// <b>一覧には残す。</b> ここで弾くのは「聞くかどうか」だけで、
    /// 何が落ちたかはパネルの一覧に出る（種類を問わず数える）。
    /// </para>
    /// </summary>
    internal static bool IsResolvable(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
        && ResolvableHosts.Any(domain =>
            parsed.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)
            || parsed.Host.EndsWith('.' + domain, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 解決を持ちかけるドメイン。<b>yt-dlp の対応表ではなく、こちらが確かめた分だけ</b>を並べる。
    /// <para>
    /// <b>能力の問題ではない。</b> <see cref="YouTubeResolver"/> の選別はサイトを問わない
    /// （<c>vcodec</c> / <c>acodec</c> と protocol しか見ておらず、YouTube 固有なのは
    /// <c>--extractor-args youtube:player_client=</c> の 1 行だけで、他のサイトでは
    /// yt-dlp が無視する）。それでも確かめた分に留めるのは、外れたときに人の手を止めるため。
    /// <b>増やすときは実際に 1 本通してから足す。</b>
    /// </para>
    /// </summary>
    private static readonly string[] ResolvableHosts = ["youtube.com", "youtu.be"];

    private async Task AskThenResolveAsync(IModuleHost host, string url, string? reason)
    {
        try
        {
            var accepted = await host.Notifications.PushModalAsync(new NotifyRequest
            {
                Kind = FailedKind,
                Title = host.Text["videourl.notify.failed.title"],
                Body = reason is { Length: > 0 } ? $"{url}（{reason}）" : url,
                ConfirmLabel = host.Text["videourl.notify.failed.confirm"],

                // 「今後は聞かない」= 設定の「再生失敗のたびに聞く」を切る。戻すのは設定画面から。
                OptOut = new NotifyAction(host.Text["videourl.notify.failed.optOut"], () =>
                {
                    _config.AskOnFailure = false;
                    host.SaveConfig();
                }),

                // 時間切れで答えそびれたら、通知の履歴から後で解決できる。
                Later = new NotifyAction(host.Text["videourl.notify.failed.confirm"],
                    () => _ = ResolveAndCopyAsync(host, url)),
            }, _shutdown.Token).ConfigureAwait(false);

            if (!accepted)
            {
                _logger.LogInformation("Video: left {Url} unresolved (declined or timed out)", url);
                return;
            }

            await ResolveAndCopyAsync(host, url).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // アプリを畳んでいる最中。
        }
    }

    /// <summary>解いて、いちばん良い 1 本をクリップボードへ。結果はトーストで知らせる。</summary>
    private async Task ResolveAndCopyAsync(IModuleHost host, string url)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Video: resolving {Url} ({Via})", url,
                _config.Server.Enabled ? "through the resolver server" : "with yt-dlp on this PC");

            var result = await _resolver!.ResolveAsync(url, _shutdown.Token).ConfigureAwait(false);

            // 渡して意味があるのは音付きの 1 本。HLS が取れていればそちらの方が画質が上。
            var best = result.Streams.FirstOrDefault(s => s.Kind == "hls") ?? result.Streams[0];

            var copied = host.Clipboard.SetText(best.Url);

            _logger.LogInformation(
                "Video: resolved {Url} in {Ms} ms ({Count} playable: {Streams}); {Copied} {Best}",
                url, watch.ElapsedMilliseconds, result.Streams.Count,
                string.Join(", ", result.Streams.Select(s => s.Kind)),
                copied ? "copied" : "could not copy", best.Kind);
            host.Notifications.PushToast(
                copied ? CopiedKind : NotifyKind.Error,
                host.Text[copied ? "videourl.notify.copied" : "videourl.notify.copyFailed"],
                $"{best.Label}｜{result.Title}");
        }
        catch (OperationCanceledException)
        {
            // アプリを畳んでいる最中。
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Video: could not resolve {Url} after {Ms} ms", url, watch.ElapsedMilliseconds);
            host.Notifications.PushToast(NotifyKind.Error, host.Text["videourl.notify.resolveFailed"], ex.Message);
        }
    }

    public async Task<string> HandleUiCallAsync(string method, string payload, CancellationToken cancellationToken = default)
    {
        switch (method)
        {
            // パネルが開かれた直後に呼ばれる。開いていなかった間の push は届いていないので、
            // ここで現在値に追いつく（シェルの getStatus() と同じ規律）。
            case "getFailures":
                return JsonSerializer.Serialize(_failures.Snapshot(), Json);

            // どちらの道で解決するか。**押した時点で聞かれる** — 設定はパネルを
            // 開いている間にも変わるので、開いたときの値を覚えていると嘘になる。
            case "getMode":
                return JsonSerializer.Serialize(new { server = _config.Server.Enabled }, Json);

            case "resolve":
                var request = JsonSerializer.Deserialize<ResolveRequest>(payload, Json);
                if (request?.Url is not { Length: > 0 } url)
                    throw new ArgumentException(_host?.Text["videourl.error.emptyUrl"] ?? "videourl.error.emptyUrl");

                var result = await _resolver!.ResolveAsync(url, cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, Json);

            default:
                throw new ArgumentException($"Unknown method: {method}");
        }
    }

    private sealed record ResolveRequest(string? Url);

    /// <summary>パネルへ一覧を流すチャンネル名。中身は <see cref="VideoFailure"/> の配列。</summary>
    private const string FailuresChannel = "failures";
}
