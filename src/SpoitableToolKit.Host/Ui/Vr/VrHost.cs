using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Ui.Vr.Dashboard;
using SpoitableToolKit.Host.Ui.Vr.Overlays;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;

namespace SpoitableToolKit.Host.Ui.Vr;

/// <summary>
/// 「いつ繋ぐか・いつ諦めるか・いつ絵を上げるか」を決めるところ。OpenVR も CEF も直に触らず（触るのは <see cref="IVrOverlays"/> と <see cref="TextureSource"/> だけ）、ヘッドセットを繋がずに試験が書ける。
/// 1 周ぶんが <see cref="Tick"/> で、回すのは呼ぶ側。こうしておくと、「起動していない間は何もしない」「閉じられたら畳む」を時間を待たずに確かめられる。
/// 使う所（<see cref="VrFeatures"/>）は周ごとに聞く。ダッシュボードのパネル（<see cref="DashboardPanel"/>）と、手首の帯と VR ウィンドウ（<see cref="VrScene"/>）は互いに触らずに出し入れする。
/// 両方切ってあれば（繋いでいる間は 2 周続けて見たら）SteamVR から手を放し、切ってある間は SteamVR のことを何も見ない。
/// </summary>
/// <param name="features">使う所。周ごとに聞く。null なら両方使う。</param>
internal sealed class VrHost(
    IVrOverlays overlays, VrHost.TextureSource textures, VrHost.PointerSink pointers, ILogger logger,
    VrScene? scene = null, Func<VrFeatures>? features = null)
{
    /// <summary>レーザーの 1 手を画面へ渡す。座標はもう画素（向きも直してある）。</summary>
    public delegate void PointerSink(VrPointerKind kind, int x, int y, VrPointerButton button, int wheelX, int wheelY);

    /// <summary>
    /// いま出すべき絵を貸してもらう。まだ 1 枚も無ければ <see cref="VrFrame.None"/> が来る。絵は向こうが持っているので、溜めずに借りに行くだけ。
    /// OpenVR へ渡すのは借りている間に済ませる — 返したあとは、向こうがそのテクスチャを捨てているかもしれない（捨てた先を渡して落ちた）。
    /// </summary>
    public delegate void TextureSource(Action<VrFrame> use);

    /// <summary>SteamVR が閉じると言ってきたあと、居なくなるのを見届けずに繋ぎ直しに行く回数の上限（繋ぎ直しは 2 秒おきなので、およそ 30 秒）。閉じると言ったきり居座り続けたときに、二度と繋がらなくなるのを避ける逃げ道。</summary>
    private const int TriesBeforeReconnectingAnyway = 15;

    private readonly Func<VrFeatures> _features = features ?? (() => VrFeatures.All);

    private readonly DashboardPanel _dashboard = new(overlays, textures, pointers, logger);

    /// <summary>
    /// SteamVR が閉じると言ってきて、まだ居なくなるのを見ていない。この間に繋ぎに行くと、終わりかけの SteamVR を起動し直してしまう:
    /// <c>OpenVR.Init</c> は vrserver の口が閉じていると vrserver を起動する（vrclient のログ: <c>Starting vrserver process</c>）。
    /// 閉じる途中は vrserver のプロセスだけが数秒残るので、「プロセスが居る」を見て繋ぎに行くと、ちょうど口が閉じたところへ当たる。
    /// 設定で両方切って手放したときは立てない（SteamVR は居続けるので、立てると入れ直したときに 30 秒ほど繋がらない）。
    /// </summary>
    private bool _leaving;

    private int _triesWhileLeaving;

    /// <summary>
    /// 繋いでいる間に入れたダッシュボードのパネルが作れなかった。切り替え直すか、繋ぎ直すまで作りに行かない（毎周作りに行くと、周の速さで警告が並ぶ）。
    /// 繋いだ周に作れなかったときはこれを使わない — 繋ぎを返して、次の周に繋ぎ直すところからやり直す。
    /// </summary>
    private bool _dashboardRefused;

    /// <summary>前の周に聞いた使う所。変わったときだけログに出す。</summary>
    private VrFeatures? _seen;

    /// <summary>繋いでいる間に、両方切を何周続けて見たら手放すか。起動したときに両方切なら、これを待たずに最初から繋がない。</summary>
    internal const int BothOffTicksBeforeLettingGo = 2;

    /// <summary>繋いでいる間に、両方切を続けて見た周の数。</summary>
    private int _bothOffTicks;

    /// <summary>SteamVR に繋いでいるか（<c>OpenVR.Init</c> が通って、まだ <c>Shutdown</c> していない）。パネルも手首も無くても、繋いでいれば true。</summary>
    public bool Running { get; private set; }

    /// <summary>ダッシュボードのパネルがあるか。browser を作る・捨てるのは呼ぶ側（<see cref="VrLoop"/>）で、これの変わり目を見る。</summary>
    public bool DashboardUp => _dashboard.Up;

    /// <summary>手首の帯と VR ウィンドウ（<see cref="VrScene"/>）を立ててあるか。手首の板が作れなかったときも、立てた（止めるときに止める）なら true。</summary>
    public bool OverlayUp { get; private set; }

    /// <summary>周を回す速さ（Hz）。繋いだときにヘッドセットのリフレッシュレートを読んで決める（<see cref="FrameRateFor"/>）。板の絵（CEF）も点の動きも、この速さで差し替わる。</summary>
    public float FrameRate { get; private set; } = FallbackFrameRate;

    public const float FallbackFrameRate = 60f;

    public const float MinFrameRate = 30f;

    public const float MaxFrameRate = 240f;

    /// <summary>ヘッドセットが言うリフレッシュレートから、周の速さを決める。読めない・数でない・0 以下は既定、それ以外は 30〜240 Hz に収める（壊れた値で周が止まったり、UI のスレッドを埋めたりしない）。</summary>
    public static float FrameRateFor(float? reported) =>
        reported is { } hz && float.IsFinite(hz) && hz > 0f ? Math.Clamp(hz, MinFrameRate, MaxFrameRate) : FallbackFrameRate;

    public int Uploads => _dashboard.Uploads;

    public void Tick()
    {
        var wanted = _features();
        Note(wanted);

        if (!Running)
        {
            // 両方切ってあれば何も呼ばない。繋ぎに行かないのはもちろん、居るかを見に行くのもやめる。
            if (!wanted.None) TryStart(wanted);
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

        if (wanted.None)
        {
            // 1 周だけなら手放さない（この周は何もしない）。設定の適用は値を 1 つずつ書くので、片方を切ってもう片方を入れる適用の途中を読むと、
            // 一瞬だけ両方切に見える。そこで手放すと、繋ぎ直しが 1 回余計に入る。
            if (++_bothOffTicks < BothOffTicksBeforeLettingGo) return;

            // こちらから手放す。SteamVR は居続けるので、閉じるのを待つ印（_leaving）は立てない。
            logger.LogInformation("The VR overlay and the SteamVR dashboard are both off; disconnecting from SteamVR");
            Stop();
            return;
        }

        _bothOffTicks = 0;

        if (!Follow(wanted)) return;

        _dashboard.Tick();

        // 手首と空間の窓はダッシュボードのあと。どちらが転んでもダッシュボードの絵は上がっている。
        if (OverlayUp) scene?.Tick();
    }

    /// <summary>
    /// 繋いでいる間に切り替わった分を合わせる。ダッシュボードのパネルと、手首と VR ウィンドウは互いに触らない（片方を切っても、もう片方はそのまま動き続ける）。
    /// パネルしか使わないのにパネルが無いときは繋ぎを返して false（「居るのに何も出さないアプリ」で居座らない。次の周に繋ぎ直すところからやり直す）。
    /// </summary>
    private bool Follow(VrFeatures wanted)
    {
        if (!wanted.Dashboard)
        {
            _dashboardRefused = false;
            _dashboard.Close();
        }
        else if (!_dashboard.Up && !_dashboardRefused)
        {
            _dashboardRefused = !_dashboard.Open();
        }

        if (wanted.Dashboard && !_dashboard.Up && !wanted.Overlay)
        {
            Stop();
            return false;
        }

        if (wanted.Overlay != OverlayUp) SetOverlay(wanted.Overlay);

        return true;
    }

    /// <summary>手首の帯と VR ウィンドウを立てる / 畳む。畳んでも繋ぎは返さない（ダッシュボードのパネルが残る）。</summary>
    private void SetOverlay(bool on)
    {
        if (on) scene?.Start();
        else scene?.Stop();

        OverlayUp = on;
    }

    /// <summary>使う所が変わった（最初の周も）ときだけ 1 行残す。切ってあるせいで繋がらないのか、SteamVR が居ないのかを後から見分けるため。</summary>
    private void Note(VrFeatures wanted)
    {
        if (_seen == wanted) return;

        _seen = wanted;
        logger.LogInformation(
            "VR features: the VR overlay is {Overlay}, the SteamVR dashboard is {Dashboard}",
            wanted.Overlay ? "on" : "off", wanted.Dashboard ? "on" : "off");
    }

    /// <summary>2 回呼ばれても壊れない（終了の道と Quit の道の両方から来る）。</summary>
    public void Stop()
    {
        if (!Running) return;

        // 手首と空間の窓が先。繋ぎを返したあとに板を消しに行かない。
        if (OverlayUp) SetOverlay(false);

        _dashboard.Close();
        _dashboardRefused = false;
        _bothOffTicks = 0;

        Running = false;
        overlays.Stop();
    }

    private void TryStart(VrFeatures wanted)
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

        // パネルが作れなかったら繋ぎを返して、次の周でまた試す（繋いだばかりの SteamVR がまだ受けられないだけのことがある）。
        if (wanted.Dashboard && !_dashboard.Open())
        {
            overlays.Stop();
            return;
        }

        Running = true;

        var reported = overlays.DisplayFrequency();
        FrameRate = FrameRateFor(reported);
        logger.LogInformation(
            "The headset says {Reported} Hz; the VR loop runs at {Rate} Hz",
            reported is { } hz ? hz.ToString("0.#") : "nothing", FrameRate);

        // 手首や窓が作れなくてもダッシュボードは諦めない。作れなかった板は黙って休む。
        if (wanted.Overlay) SetOverlay(true);
    }
}
