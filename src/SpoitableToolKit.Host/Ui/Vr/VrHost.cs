using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Ui.Vr.Overlays;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Pointing;
using SpoitableToolKit.Host.Ui.Vr.Rendering;

namespace SpoitableToolKit.Host.Ui.Vr;

/// <summary>
/// 「いつ繋ぐか・いつ諦めるか・いつ絵を上げるか」を決めるところ。OpenVR も CEF も直に触らず（触るのは <see cref="IVrOverlays"/> と <see cref="TextureSource"/> だけ）、ヘッドセットを繋がずに試験が書ける。
/// 1 周ぶんが <see cref="Tick"/> で、回すのは呼ぶ側。こうしておくと、「起動していない間は何もしない」「閉じられたら畳む」を時間を待たずに確かめられる。
/// </summary>
/// </summary>
internal sealed class VrHost(
    IVrOverlays overlays, VrHost.TextureSource textures, VrHost.PointerSink pointers, ILogger logger,
    VrScene? scene = null)
{
    /// <summary>レーザーの 1 手を画面へ渡す。座標はもう画素（向きも直してある）。</summary>
    public delegate void PointerSink(VrPointerKind kind, int x, int y, VrPointerButton button, int wheelX, int wheelY);

    private const float DashboardWidthInMeters = 2.0f;

    public const string DashboardKey = "dev.f2a.spoitable.dashboard";

    private const string DashboardName = "SpoitableToolKit";

    /// <summary>
    /// いま出すべき絵を貸してもらう。まだ 1 枚も無ければ <see cref="VrFrame.None"/> が来る。絵は向こうが持っているので、溜めずに借りに行くだけ。
    /// OpenVR へ渡すのは借りている間に済ませる — 返したあとは、向こうがそのテクスチャを捨てているかもしれない（捨てた先を渡して落ちた）。
    /// </summary>
    public delegate void TextureSource(Action<VrFrame> use);

    /// <summary>SteamVR が閉じると言ってきたあと、居なくなるのを見届けずに繋ぎ直しに行く回数の上限（繋ぎ直しは 2 秒おきなので、およそ 30 秒）。閉じると言ったきり居座り続けたときに、二度と繋がらなくなるのを避ける逃げ道。</summary>
    private const int TriesBeforeReconnectingAnyway = 15;

    private ulong _dashboard;

    /// <summary>
    /// SteamVR が閉じると言ってきて、まだ居なくなるのを見ていない。この間に繋ぎに行くと、終わりかけの SteamVR を起動し直してしまう:
    /// <c>OpenVR.Init</c> は vrserver の口が閉じていると vrserver を起動する（vrclient のログ: <c>Starting vrserver process</c>）。
    /// 閉じる途中は vrserver のプロセスだけが数秒残るので、「プロセスが居る」を見て繋ぎに行くと、ちょうど口が閉じたところへ当たる。
    /// </summary>
    private bool _leaving;

    private int _triesWhileLeaving;

    private readonly OverlayFeed _feed = new(overlays, textures);

    private int _lastX;

    private int _lastY;

    public bool Running { get; private set; }

    /// <summary>周を回す速さ（Hz）。繋いだときにヘッドセットのリフレッシュレートを読んで決める（<see cref="FrameRateFor"/>）。板の絵（CEF）も点の動きも、この速さで差し替わる。</summary>
    public float FrameRate { get; private set; } = FallbackFrameRate;

    public const float FallbackFrameRate = 60f;

    public const float MinFrameRate = 30f;

    public const float MaxFrameRate = 240f;

    /// <summary>ヘッドセットが言うリフレッシュレートから、周の速さを決める。読めない・数でない・0 以下は既定、それ以外は 30〜240 Hz に収める（壊れた値で周が止まったり、UI のスレッドを埋めたりしない）。</summary>
    public static float FrameRateFor(float? reported) =>
        reported is { } hz && float.IsFinite(hz) && hz > 0f ? Math.Clamp(hz, MinFrameRate, MaxFrameRate) : FallbackFrameRate;

    public int Uploads => _feed.Uploads;

    public void Tick()
    {
        if (!Running)
        {
            TryStart();
            return;
        }

        // 閉じるのに付き合うのが先。絵を差し替えてから抜けると 1 枚ぶん遅れる。
        if (overlays.QuitRequested())
        {
            logger.LogInformation("SteamVR asked us to quit; putting the overlays away");
            Stop();

            _leaving = true;
            _triesWhileLeaving = 0;
            return;
        }

        Deliver();

        _feed.Hand(_dashboard);

        // 手首と空間の窓はダッシュボードのあと。どちらが転んでもダッシュボードの絵は上がっている。
        scene?.Tick();
    }

    /// <summary>絵より先に配る。押した結果が次の 1 枚に乗るので、逆にすると 1 枚ぶん遅れて見える。</summary>
    private void Deliver()
    {
        foreach (var pointer in overlays.PollPointer(_dashboard))
        {
            if (pointer.Kind == VrPointerKind.Scroll)
            {
                var (wheelX, wheelY) = VrPointerMap.ToWheel(pointer.ScrollX, pointer.ScrollY);

                // 巻いた場所は最後に指していたところ。スクロールの報せに座標は入っていない。
                pointers(VrPointerKind.Scroll, _lastX, _lastY, pointer.Button, wheelX, wheelY);
                continue;
            }

            (_lastX, _lastY) = VrPointerMap.ToBrowser(pointer.X, pointer.Y, VrSurface.Width, VrSurface.Height);
            pointers(pointer.Kind, _lastX, _lastY, pointer.Button, 0, 0);
        }
    }

    /// <summary>2 回呼ばれても壊れない（終了の道と Quit の道の両方から来る）。</summary>
    public void Stop()
    {
        if (!Running) return;

        // 手首と空間の窓が先。繋ぎを返したあとに板を消しに行かない。
        scene?.Stop();

        if (_dashboard != 0) overlays.Destroy(_dashboard);

        _dashboard = 0;
        _feed.Forget();
        Running = false;
        overlays.Stop();
    }

    private void TryStart()
    {
        // 居ないのに繋ぎに行かない。繋ぎに行くと SteamVR が起動してしまう（ヘッドセットを使う気が無いのに VR が立ち上がる）。
        if (!overlays.IsAvailable())
        {
            // 居なくなったのを見た。次に現れたら、それは新しく立ち上がった SteamVR。
            _leaving = false;
            return;
        }

        // 閉じると言ったきりの SteamVR へは繋ぎに行かない。終わりかけを起こし直すことになる。
        if (_leaving && ++_triesWhileLeaving < TriesBeforeReconnectingAnyway) return;

        _leaving = false;

        if (!overlays.Start(out var error))
        {
            // 黙って諦める。次の周でまた試すので、毎回警告を出すとログが埋まる。
            logger.LogDebug("OpenVR is not ready yet: {Error}", error);
            return;
        }

        _dashboard = overlays.CreateDashboard(DashboardKey, DashboardName, DashboardWidthInMeters);

        if (_dashboard == 0)
        {
            logger.LogWarning("Could not create the dashboard overlay");
            overlays.Stop();
            return;
        }

        // 板が出来てから触れるようにする。先に呼ぶと宛先が無い。
        overlays.EnableInput(_dashboard, VrSurface.Width, VrSurface.Height);

        Running = true;
        logger.LogInformation("The VR dashboard overlay is up");

        var reported = overlays.DisplayFrequency();
        FrameRate = FrameRateFor(reported);
        logger.LogInformation(
            "The headset says {Reported} Hz; the VR loop runs at {Rate} Hz",
            reported is { } hz ? hz.ToString("0.#") : "nothing", FrameRate);

        // 手首や窓が作れなくてもダッシュボードは諦めない。作れなかった板は黙って休む。
        scene?.Start();
    }
}
