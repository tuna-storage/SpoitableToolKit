using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Config;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Browser;
using SpoitableToolKit.Host.Ui.Push;
using SpoitableToolKit.Host.Ui.Shell;
using SpoitableToolKit.Host.Ui.Vr.Diagnostics;
using SpoitableToolKit.Host.Ui.Vr.Overlays;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Rendering;
using SpoitableToolKit.Host.Ui.Vr.Window;
using SpoitableToolKit.Host.Ui.Vr.Wrist;

namespace SpoitableToolKit.Host.Ui.Vr;

/// <summary>
/// <see cref="VrHost.Tick"/> を回す器。SteamVR が起動するまでは CEF を触らない（面は繋がってから作る — トレイに常駐しているだけの間は CEF プロセスが 0、という約束を壊さないため）。
/// <c>SPOITABLE_NO_VR</c> を入れると一切動かない（開発機に SteamVR が入っているので、これが無いと試験の最中にヘッドセットへ板が出る）。
/// 手首の板と空間の窓も同じ周で回す（<see cref="WristHost"/> / <see cref="VrWindowHost"/>）。窓の面は最初に開いたときに作る（開かない人の RAM を増やさない）。
/// 面は板と一緒に作り、一緒に捨てる: ダッシュボードの面はパネル（<see cref="VrHost.DashboardUp"/>）、手首と窓の面は <see cref="VrHost.OverlayUp"/> の変わり目で。設定で切った所の browser は残さない。
/// </summary>
internal sealed class VrLoop : IDisposable, IVrControls
{
    /// <summary>これだけ周が回らなければ止まったとみなす。繋がっていない間の周の間隔（2 秒）より長く、SteamVR が Quit の返事を待つ 5 秒より短く — 殺される前にログへ残したい。</summary>
    private const long StallAfterMs = 3_000;

    private readonly CancellationTokenSource _stopping = new();

    private readonly ILogger _logger;

    /// <summary>周を打つ係（<see cref="VrPacer"/>）。ヘッドセットの速さで打ち、周そのものは UI のスレッドで走らせる。</summary>
    private VrPacer? _pacer;

    private static readonly TimeSpan IdlePeriod = TimeSpan.FromSeconds(2);

    /// <summary>周を見張る側。UI のスレッドの外で回す（止まっているのが UI のスレッドなので）。</summary>
    private System.Threading.Timer? _watchdog;

    private VrHost? _host;

    private VrSurface? _surface;

    private readonly VrSharedTexture _shared;

    private WristHost? _wrist;

    private VrSurface? _wristSurface;

    /// <summary>手首の分。寸法が違うので別の 1 枚。デバイスもこちらで 1 つ持つ（<see cref="VrSharedTexture"/> がデバイスごと面倒を見る作りのため）ので、VRAM はその分増える。</summary>
    private readonly VrSharedTexture _wristShared;

    private VrWindowHost? _window;

    private VrSurface? _windowSurface;

    /// <summary>空間の窓の分。寸法はダッシュボードと同じだが、別のページを出すので別の 1 枚（デバイスも別）。</summary>
    private readonly VrSharedTexture _windowShared;

    /// <summary>画面から頼まれたこと（ショートカット）。CEF のスレッドから積まれ、周の中で片付ける — OpenVR も browser も周のスレッドでだけ触る。</summary>
    private readonly ConcurrentQueue<Action> _requests = new();

    private readonly VrStallWatch _watch = new(() => Environment.TickCount64, StallAfterMs);

    private VrLoop(ILogger logger)
    {
        _logger = logger;
        _shared = new VrSharedTexture(logger);
        _wristShared = new VrSharedTexture(logger, WristHost.Width, WristHost.Height);
        _windowShared = new VrSharedTexture(logger, VrWindowHost.Width, VrWindowHost.Height);
    }

    /// <summary>動かす。止め口が入っていれば <c>null</c> を返す。</summary>
    public static VrLoop? Start(
        UiBroadcast broadcast, UiBridge bridge, HostConfig config, ModuleConfigStore configs, ILogger logger)
    {
        if (Environment.GetEnvironmentVariable("SPOITABLE_NO_VR") is { Length: > 0 })
        {
            logger.LogInformation("SPOITABLE_NO_VR is set; the VR overlays stay off");
            return null;
        }

        var loop = new VrLoop(logger);
        loop.Run(broadcast, bridge, new ConfiguredWrist(config, configs), new ConfiguredWindow(config, configs), () => VrFeatures.Of(config));

        // 手首のボタンから頼まれる口。窓口は VR より先に出来ているので後から挿す。
        bridge.Vr = loop;

        return loop;
    }

    public void Dispose()
    {
        _stopping.Cancel();

        _watchdog?.Dispose();
        _pacer?.Dispose();

        // 繋いだところと同じ側で片付ける。ここは UI スレッド。
        _host?.Stop();
        _surface?.Dispose();
        _wristSurface?.Dispose();
        _windowSurface?.Dispose();
        _shared.Dispose();
        _wristShared.Dispose();
        _windowShared.Dispose();

        _stopping.Dispose();
    }

    private void Run(
        UiBroadcast broadcast, UiBridge bridge, IWristSettings wristSettings, IVrWindowSettings windowSettings, Func<VrFeatures> features)
    {
        var overlays = new WatchedOverlays(new OpenVrOverlays(_logger), _watch);

        // 開発用: 手首の板を頭の前に固定する。仮想ヘッドセットにはコントローラーが無いので、こうしないと合成済みの絵（vrctl mirror）で確かめられない。
        var onHead = Environment.GetEnvironmentVariable("SPOITABLE_VR_WRIST") is "hmd";

        // 手首と窓は互いを読む（窓は手首の板と Layout Mode を、手首は窓を掴んでいるかを）。読むのは周の中だけ。
        VrWindowHost? window = null;

        var wrist = new WristHost(
            overlays,
            wristSettings,
            use => Lend(_wristShared, "wrist", use),
            (kind, x, y, button, wheelX, wheelY) =>
                _watch.Inside("sending the pointer to the wrist", () => _wristSurface?.Send(kind, x, y, button, wheelX, wheelY)),
            shown => _watch.Inside(
                shown ? "waking the wrist browser" : "putting the wrist browser to sleep",
                () =>
                {
                    // 隠れる手首のタイルを押していたら、その押しはここで途切れたことにする（眠らせた browser に離しが届くとは限らない）。
                    if (!shown) bridge.Gestures.CutOff();
                    _wristSurface?.SetHidden(!shown);
                }),
            // 手首のページは取っ手と大きさを写す。窓のページにも送る（Layout Mode の間は中身を薄くして、押せないことを見せる）。
            // 窓のページは読み終わったときにも聞き直す（作った直後・読み込み中の push は捨てられる）。
            layout => _watch.Inside(
                "telling the wrist page and the VR window about the layout",
                () =>
                {
                    _wristSurface?.Push("wristLayout", new { on = layout.On, width = layout.Width });
                    _windowSurface?.Push("vrLayout", new { on = layout.On });
                }),
            _logger,
            onHead,
            clickable: () => _wristSurface?.OverClickable ?? false,
            windowHeld: () => window?.Dragging ?? false);

        window = new VrWindowHost(
            overlays,
            windowSettings,
            use => Lend(_windowShared, "window", use),
            (kind, x, y, button, wheelX, wheelY) =>
                _watch.Inside("sending the pointer to the VR window", () => _windowSurface?.Send(kind, x, y, button, wheelX, wheelY)),
            view => _watch.Inside(
                view is null ? "putting the VR window browser to sleep" : "showing a screen in the VR window",
                () => ShowInWindow(view, broadcast, bridge)),
            // 手首に付けた窓が手首と一緒に隠れた・出た。閉じたのとは別（画面はそのまま、眠らせるだけ）。
            seen => _watch.Inside(
                seen ? "waking the VR window browser" : "putting the VR window browser to sleep with the wrist",
                () => _windowSurface?.SetHidden(!seen)),
            () => wrist.Board,
            // Layout Mode は手首と共通（持ち主は手首）。窓を掴めるのも、中身を押せなくするのもこの間だけ。
            () => wrist.InLayout,
            _logger,
            clickable: () => _windowSurface?.OverClickable ?? false,
            // 掴む手（cursor: grab）の上で引いたら巻かない（設定のモーダルのつまみなど、ページが自分で掴んで動かすもの）。
            grabbable: () => _windowSurface?.OverGrab ?? false,
            // 手首のページにだけ送る（ショートカットの枠）。ページは押した瞬間に先に塗っていて、これは答え合わせ。
            viewChanged: view => _watch.Inside(
                "telling the wrist page which screen the VR window shows",
                () => _wristSurface?.Push("wristView", new { view })));

        var host = new VrHost(
            overlays,
            use => Lend(_shared, "VR", use),
            (kind, x, y, button, wheelX, wheelY) =>
                _watch.Inside("sending the pointer to CEF", () => _surface?.Send(kind, x, y, button, wheelX, wheelY)),
            _logger,
            // 手首が先（窓は手首の板の上に出すので、同じ周の手首の姿勢を読む）。
            // Layout Mode の間は入力を全部預かる（VRChat に届かない）。
            new VrScene(overlays, _logger, [wrist, window], captureAll: () => wrist.InLayout),
            features);

        // UI スレッドで回す。打つのは VrPacer のスレッドだが、周の中身はこの UI スレッドへ投げて走らせる
        // （窓が先に出来ているので、ここには WinForms の SynchronizationContext が入っている）。
        var ui = SynchronizationContext.Current as WindowsFormsSynchronizationContext ?? new WindowsFormsSynchronizationContext();

        var probe = new VrLoopProbe(_logger, repeat: Environment.GetEnvironmentVariable("SPOITABLE_VR_PROBE") is "1");

        void Tick()
        {
            if (_stopping.IsCancellationRequested) return;

            var started = System.Diagnostics.Stopwatch.GetTimestamp();

            try
            {
                _watch.Enter(VrStallWatch.InsideTick);

                // 画面から頼まれたことを先に片付ける。繋がっていなければ捨てる（窓が見る: 板が無ければ受けない）。
                while (_requests.TryDequeue(out var request)) _watch.Inside("doing what the wrist asked", request);

                var hadDashboard = host.DashboardUp;
                var hadOverlay = host.OverlayUp;

                host.Tick();

                // 繋ぎを返した周は、パネルも手首も下りている（両方の面を捨てる）。
                FollowDashboard(hadDashboard, host, broadcast, bridge);
                FollowOverlay(hadOverlay, host, wrist, broadcast, bridge);

                // 板に 1 枚描かせる。CEF は自分では描かない（こちらが打ったときだけ描く）ので、周の速さがそのまま板の絵の速さになる。
                // 手首は出ている間だけ（眠らせている間は打たない）。手首が出た周（wrist.Tick で起こした周）にも打つので、起こした直後の絵が古いまま残らない。
                _watch.Inside("asking CEF for a frame", () =>
                {
                    _surface?.BeginFrame();
                    _wristSurface?.BeginFrame();
                    _windowSurface?.BeginFrame();
                });

                if (_pacer is { } pacer) pacer.Period = host.Running ? TimeSpan.FromSeconds(1 / host.FrameRate) : IdlePeriod;

                _watch.Beat();
                probe.Note(host.Running, started, System.Diagnostics.Stopwatch.GetTimestamp(), _shared.Sequence);
            }
            catch (Exception ex)
            {
                // VR が転んでも本体は落とさない。窓は窓で動き続ける。
                _logger.LogError(ex, "The VR loop stopped");
                _pacer?.Dispose();

                // 板を出したまま止まらない。周が止まったまま出しっぱなしにすると、手首の板と自前の点が視界に残り続ける（見ていないのに隠れない）。
                // 畳めるだけ畳む（周から例外が漏れることは実際にある: browser が出来る前の呼び出し）。
                try
                {
                    host.Stop();
                }
                catch (Exception stopFailed)
                {
                    _logger.LogWarning(stopFailed, "Could not put the VR overlays away after the loop stopped");
                }

                // 止めたのはこちら。見張りを残すと「止まった」と報せてしまう。
                _watchdog?.Dispose();
            }
        }

        _pacer = new VrPacer(ui, Tick, TimeSpan.FromMilliseconds(16));
        _host = host;
        _wrist = wrist;
        _window = window;

        _watchdog = new System.Threading.Timer(_ => Watch(), null, 1_000, 1_000);
    }

    /// <summary>ダッシュボードのパネルが上がったら面を作り、下りたら捨てる（設定で切った・繋ぎを返した）。</summary>
    private void FollowDashboard(bool had, VrHost host, UiBroadcast broadcast, UiBridge bridge)
    {
        if (!had && host.DashboardUp)
        {
            _surface = _watch.Inside(
                "creating the VR browser",
                // surface=dashboard はシェルに「デスクトップの窓ではない」を伝える印。付けないと、最小化・最大化・✕ とタイトルバーの掴みがデスクトップの窓へ効く（✕ でアプリが閉じる）。
                () => new VrSurface(
                    $"{CefRuntime.UiOrigin}/index.html?surface=dashboard", broadcast, bridge, _shared, _logger,
                    frameRate: (int)MathF.Round(host.FrameRate)));
        }
        else if (had && !host.DashboardUp)
        {
            _watch.Inside("closing the VR browser", () => _surface?.Dispose());
            _surface = null;
        }
    }

    /// <summary>手首の帯と VR ウィンドウが立ったら手首の面を作り、畳まれたら手首と窓の面を捨てる（窓の面は次に開いたときに作り直す: <see cref="ShowInWindow"/>）。</summary>
    private void FollowOverlay(bool had, VrHost host, WristHost wrist, UiBroadcast broadcast, UiBridge bridge)
    {
        if (!had && host.OverlayUp)
        {
            // 手首の browser は立てた時点で作る（見たときに作るのではなく）。
            // 答え待ちの通知は getStatus で拾い直せないので、後から作ると届いた分を取りこぼす。見ていない間は眠らせておくので、描く手間は掛からない。
            if (!wrist.Running) return;

            _wristSurface = _watch.Inside(
                "creating the wrist browser",
                () => new VrSurface(
                    $"{CefRuntime.UiOrigin}/wrist.html", broadcast, bridge, _wristShared, _logger,
                    WristHost.Width, WristHost.Height, WristHost.Channels, (int)MathF.Round(host.FrameRate)));
            _wristSurface.SetHidden(!wrist.Shown);
        }
        else if (had && !host.OverlayUp)
        {
            _watch.Inside("closing the wrist and VR window browsers", () =>
            {
                // 手首のタイルを押したまま畳まれた（SteamVR が閉じた・設定で切った）。離しはもう届かない。
                bridge.Gestures.CutOff();
                _wristSurface?.Dispose();
                _windowSurface?.Dispose();
            });
            _wristSurface = null;
            _windowSurface = null;
        }
    }

    /// <summary>借りて渡す。錠を待っている間と、借りている間とを分けて名札を付ける。借りている間の外への呼び出し（SetOverlayTexture）は <c>WatchedOverlays</c> が名札を付け直す。</summary>
    private void Lend(VrSharedTexture shared, string which, Action<VrFrame> use)
    {
        _watch.Enter($"waiting for the {which} texture lock");
        shared.Lend(frame =>
        {
            _watch.Enter($"holding the {which} texture lock");
            use(frame);
        });
        _watch.Enter(VrStallWatch.InsideTick);
    }

    public void ToggleWindow(string view) => _requests.Enqueue(() => _window?.Toggle(view));

    public void CloseWindow() => _requests.Enqueue(() => _window?.Close());

    public void WindowViewing(string view) => _requests.Enqueue(() => _window?.Viewing(view));

    public string? WindowView => _window?.View;

    /// <summary>
    /// 窓に画面を出す / 眠らせる（<see cref="VrWindowHost"/> から、周の中で）。
    /// browser は最初に開いたときに作る（開かない人の RAM を増やさない）。閉じたら捨てずに眠らせる — 作り直すと読み込みを待つことになり、画面の中の状態（巻いた位置など）も飛ぶ。
    /// ⚠ 最初の画面は URL で渡す。作った直後のページは読み込みの途中で、push（<c>showPanel</c>）は届かずに捨てられる。
    /// <c>surface=window</c> はシェルに「空間の窓で動いている」を伝える印（✕ を出す・VR 専用のページを優先する・切り替えを知らせる）。
    /// </summary>
    /// </summary>
    private void ShowInWindow(string? view, UiBroadcast broadcast, UiBridge bridge)
    {
        if (view is null)
        {
            _windowSurface?.SetHidden(true);
            return;
        }

        if (_windowSurface is null)
        {
            _windowSurface = new VrSurface(
                $"{CefRuntime.UiOrigin}/index.html?surface=window&view={Uri.EscapeDataString(view)}", broadcast, bridge, _windowShared, _logger,
                VrWindowHost.Width, VrWindowHost.Height, frameRate: (int)MathF.Round(_host?.FrameRate ?? VrHost.FallbackFrameRate));
            return;
        }

        _windowSurface.SetHidden(false);
        _windowSurface.Show(view);
    }

    public void ResetWristPosition() => _wrist?.RequestReset();

    public void SetWristLayout(bool on) => _wrist?.RequestLayout(on);

    public bool InLayout => _wrist?.InLayout ?? false;

    public void ResizeWrist(int steps) => _wrist?.RequestResize(steps);

    /// <summary>周が止まっていないかを見る。止まった 1 回と、また回り出した 1 回だけ残す。ここは UI のスレッドの外で、ログはファイルへその場で書かれる（<c>AppLogFile</c> は 1 行ごとに flush する）ので、このあと SteamVR に殺されても残る。</summary>
    private void Watch()
    {
        if (_stopping.IsCancellationRequested) return;

        if (_watch.Check() is not { } stall) return;

        if (stall.Stuck)
        {
            _logger.LogWarning(
                "The VR loop has not come round for {Seconds:0.0}s; last seen at: {Where}",
                stall.For.TotalSeconds, stall.Where);
        }
        else
        {
            _logger.LogWarning("The VR loop came round again after {Seconds:0.0}s", stall.For.TotalSeconds);
        }
    }
}
