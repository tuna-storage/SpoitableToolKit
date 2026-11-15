using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Vr.Overlays;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Pointing;
using SpoitableToolKit.Host.Ui.Vr.Rendering;
using SpoitableToolKit.Host.Ui.Vr.Wrist;

namespace SpoitableToolKit.Host.Ui.Vr.Window;

/// <summary>
/// 空間の窓（VR の中で使う 1 枚）を「いつ出すか・どこに置くか・掴んだらどう動かすか」を決めるところ。ダッシュボードとは別の 1 枚（別の browser）。
/// 開くのは手首のショートカット（<see cref="Toggle"/>）: 開いている画面のショートカットをもう一度押すと閉じ、別の画面のショートカットならその場で切り替える（動かさない）。閉じるのは窓の ✕ でも（<see cref="Close"/>）。
/// 置き方は 2 つ（設定 <see cref="VrWindowSpot"/>、開いたときに決まる）:
/// 手首の上（既定）— 手首の板の上にディスプレイとして付いて、腕と一緒に動く。手首を下ろして手首の板が隠れると一緒に隠れる（閉じたわけではない。見ていない間は描かない）。指すのは手首と反対の手だけ。置き場は手首の板から見た位置で手ごとに覚える。
/// 目の前 — 開くたびに目の前に、顔の方を向いて出る。そのあとは空間に残る（手には付いてこない）。置き場は覚えない。
/// 操作は自前のレーザー（<see cref="VrPointing"/>）で、トリガーを引いたまま動かすと巻く。掴めるのは Layout Mode の間だけ（手首と共通）。普段のグリップは VRChat にだけ届く。
/// Layout Mode の間は中身を押せない（薄くして、点だけ動く）— 窓のどこでも掴める。掴んだままトリガーを引いて、手を前へ押すと縮み、手前へ引くと大きくなる（その間、窓は動かさない — 手に付いたまま縮めると、遠ざかりながら縮んで小さくなりすぎる）。大きさは置き方ごとに覚える。
/// 周の外から呼ばれる口（<see cref="Toggle"/> / <see cref="Close"/> / <see cref="Viewing"/>）も、周のスレッドで呼ぶ（<see cref="VrLoop"/> が頼みを預かって、周の頭で流す）。
/// </summary>
/// <param name="showing">窓に出す画面が変わった（開いた・切り替えた）。null は閉じた（browser を眠らせる）。browser を最初に開いたときに作る（使わない人の RAM を増やさない）のは呼ぶ側の仕事。呼ばれた browser は起きている。</param>
/// <param name="awake">開いたまま見えなくなった（false）/ また見えた（true）。手首に付けた窓が手首と一緒に隠れたとき。browser を眠らせる・起こす。閉じたのとは別（画面はそのまま、<see cref="View"/> も残る）。</param>
/// <param name="wristBoard">手首の板の今。出ていなければ null（手首に付けた窓も隠れる）。</param>
/// <param name="layout">Layout Mode か（<see cref="WristHost.InLayout"/>。持ち主は手首）。差し出すたび・掴み始めるたびに聞く。</param>
/// <param name="viewChanged"><see cref="View"/> が変わった（開いた・切り替えた・閉じた・窓のサイドバーで切り替えた）ときだけ呼ぶ。手首のページへ送って、ショートカットの枠をすぐ塗り直させる。<paramref name="showing"/> とは別 — あちらは browser を起こす・眠らせるためのもので、サイドバーでの切り替えでは呼ばない。</param>
internal sealed class VrWindowHost(
    IVrOverlays overlays,
    IVrWindowSettings settings,
    VrHost.TextureSource textures,
    VrHost.PointerSink pointers,
    Action<string?> showing,
    Action<bool> awake,
    Func<WristBoardState?> wristBoard,
    Func<bool> layout,
    ILogger logger,
    Func<bool>? clickable = null,
    Action<string?>? viewChanged = null,
    Func<bool>? grabbable = null) : IVrBoardOwner
{
    public const string Key = "dev.f2a.spoitable.window";

    private const string Name = "SpoitableToolKit (window)";

    /// <summary>板の画素数。ダッシュボードと同じ（同じページを出すので、並びが同じに見える）。</summary>
    public const int Width = VrSurface.Width;

    public const int Height = VrSurface.Height;

    public const float DefaultWidth = 0.5f;

    /// <summary>手首に付ける窓の横幅の既定（メートル）。手首の板（0.18 m）の上に出すので、空間に置くときより小さい（0.5 m だと顔の前を塞ぐ）。</summary>
    public const float DefaultWristWidth = 0.3f;

    public const float MinWidth = 0.15f;

    public const float MaxWidth = 2.5f;

    private readonly Func<bool> _clickable = clickable ?? (() => false);

    /// <summary>いま掴める物の上か（<see cref="VrSurface.OverGrab"/>）。掴める物の上で引くと、巻かずにページへそのまま送る。</summary>
    private readonly Func<bool> _grabbable = grabbable ?? (() => false);

    /// <summary>
    /// Layout Mode の間に差し出す口。点を動かす（Move）と外れた（Leave）だけ通す。押す・離す・長押しの右クリックは捨てる（中身は押せない）。Leave を捨てると <c>:hover</c> が残る。
    /// 押したまま Layout Mode に入っても、browser の中でボタンが押しっぱなしで残ることは無い — 普段の窓は巻く板なので、離すまで Down を送っていない（<see cref="VrLaser"/>）。
    /// </summary>
    private readonly VrHost.PointerSink _arranging = (kind, x, y, button, wheelX, wheelY) =>
    {
        if (kind is VrPointerKind.Move or VrPointerKind.Leave) pointers(kind, x, y, button, wheelX, wheelY);
    };

    private readonly OverlayFeed _feed = new(overlays, textures);

    private ulong _handle;

    private bool _shown;

    /// <summary>SteamVR に出している。空間に置く窓では開いている間ずっと、手首に付けた窓では手首の板が出ている間だけ。</summary>
    private bool _visible;

    /// <summary>板に渡した透明度。手首に付けた窓は手首の板と同じ値（手首と一緒にフェードする）、空間に置く窓は 1。OpenVR の既定は 1 なので、板を作り直したら 1 に戻す。</summary>
    private float _alpha = 1f;

    private bool _awake;

    /// <summary>手首に付けているか。開いたときに決める（開いている間に設定が変わっても窓は跳ばない）。</summary>
    private bool _onWrist;

    /// <summary>いま出している画面。閉じている間は null。周のスレッドの外からも読む（窓のページが読み終わったときに聞き直す: <see cref="View"/>）ので volatile。</summary>
    private volatile string? _view;

    /// <summary>窓の今の姿勢（立った姿勢の原点から見た位置と向き）。手首に付けた窓でも毎周ここへ入れる（掴む・大きさの前の向きに使う）。</summary>
    private VrMatrix _pose = VrMatrix.Identity;

    /// <summary>手首に付けた窓の、手首の板から見た置き場。null は既定（板の真上。窓の大きさが変わっても下端が板の上に来る）。</summary>
    private VrMatrix? _rel;

    /// <summary>手首に付けた窓が付いている手。替わったらその手の置き場を読み直す（掴んでいたら置く）。</summary>
    private VrHand? _hand;

    /// <summary>最後に付けた機器と置き場。変わったときだけ OpenVR に渡す。</summary>
    private (uint Device, VrMatrix Offset)? _attached;

    /// <summary>板に当てている横幅。変わったときだけ OpenVR に渡す。</summary>
    private float _width;

    private bool _failed;

    private string? _openRequested;

    private bool _closeRequested;

    /// <summary>この周に読んだ姿勢。掴み始めは周の後半なので、前半で読んだものを使う。</summary>
    private IReadOnlyList<VrMatrix?> _poses = [];

    private bool _offered;

    private Drag? _drag;

    private sealed record Drag(VrHand Hand, uint Device, VrMatrix Grip);

    private sealed record Resize((float X, float Y, float Z) From, float Width, (float X, float Y, float Z) Forward, VrMatrix Rel);

    private Resize? _resize;

    /// <summary>作れなかったら窓は休む（ダッシュボードと手首は動き続ける）。</summary>
    public bool Running => _handle != 0;

    public bool Shown => _shown;

    public bool Visible => _visible;

    public bool OnWrist => _shown && _onWrist;

    /// <summary>どのスレッドから読んでもよい — 窓のページは読み込み終わったときにこれを聞き直す（読み込みの途中に送った切り替えは捨てられているため）。</summary>
    public string? View => _view;

    public bool Dragging => _drag is not null;

    public VrMatrix Pose => _pose;

    public float WidthInMeters => _width;

    public float HeightInMeters => _width * Height / Width;

    public int Uploads => _feed.Uploads;

    /// <summary>横幅を範囲に収める。null・数でないものは既定（手で書き換えた <c>config.json</c> も来る）。</summary>
    public static float ClampWidth(float? meters, float fallback = DefaultWidth) =>
        meters is { } value && float.IsFinite(value) ? Math.Clamp(value, MinWidth, MaxWidth) : fallback;

    /// <summary>手首のショートカット。その画面を出しているなら閉じ、閉じているなら開き、別の画面なら切り替える。周のスレッドで呼ぶ（片付けるのは次の周の頭）。板が無い間（繋がっていない）は捨てる — 預かると、繋いだ瞬間に開く。</summary>
    public void Toggle(string view)
    {
        if (_handle == 0) return;

        var next = _closeRequested ? null : _openRequested ?? View;

        if (next == view)
        {
            Close();
            return;
        }

        _openRequested = view;
        _closeRequested = false;
    }

    public void Close()
    {
        if (_handle == 0) return;

        _openRequested = null;
        _closeRequested = true;
    }

    /// <summary>窓のページが自分で画面を切り替えた（サイドバー）。ショートカットをもう一度押したときに「閉じる」か「切り替える」かを正しく決めるために覚えておく。周のスレッドで呼ぶ。閉じている間に来たもの（古い報せ）は捨てる — 覚えると、閉じているのに <see cref="View"/> が画面を返す。</summary>
    public void Viewing(string view)
    {
        if (_shown) SetView(view);
    }

    /// <summary><see cref="_view"/> を書く口はここだけ。変わったときだけ手首へ知らせる。</summary>
    private void SetView(string? view)
    {
        if (_view == view) return;

        _view = view;
        viewChanged?.Invoke(view);
    }

    public void Start()
    {
        if (_handle != 0) return;

        _failed = false;
        _width = ClampWidth(settings.WristWidth, DefaultWristWidth);
        _handle = overlays.CreateOverlay(Key, Name, _width);

        if (_handle == 0)
        {
            logger.LogWarning("Could not create the VR window overlay; the dashboard and the wrist carry on without it");
            return;
        }

        // SteamVR のレーザーでは触らせない（入力の口も旗も無し。立てると VRChat の操作が止まる）。触るのは自前のレーザーだけ。
        logger.LogInformation("The VR window overlay is ready (opens from the wrist shortcuts)");
    }

    public void Stop()
    {
        if (_handle == 0) return;

        overlays.Destroy(_handle);

        _handle = 0;
        _drag = null;
        _resize = null;
        _offered = false;
        _openRequested = null;
        _closeRequested = false;
        _visible = false;
        _alpha = 1f;
        _attached = null;
        _hand = null;
        _feed.Forget();

        if (_shown)
        {
            _shown = false;
            SetView(null);
            _awake = false;
            showing(null);
        }
    }

    public void Place(VrHands hands, bool dashboardOpen, List<VrLaserTarget> targets)
    {
        _offered = false;

        if (_handle == 0 || _failed) return;

        Guard(() => Step(hands, dashboardOpen, targets));
    }

    public void Take(VrHands hands, VrPointed pointed)
    {
        if (_handle == 0 || _failed || !_offered) return;

        Guard(() =>
        {
            foreach (var hand in (ReadOnlySpan<VrHand>)[VrHand.Left, VrHand.Right])
            {
                var step = pointed.For(hand);

                if (step.Target != _handle || !step.GrabPressed) continue;

                StartDrag(hand);
                return;
            }
        });
    }

    /// <summary>窓が転んでも周ごとは止めない — 外へ投げると <see cref="VrLoop"/> がダッシュボードごと止める。窓だけ畳んで、1 回だけ残す。</summary>
    private void Guard(Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            _failed = true;
            logger.LogError(ex, "The VR window stopped; the dashboard and the wrist carry on");

            try
            {
                Stop();
            }
            catch
            {
                // 片付けまで転んだ。板が残っても、出してはいない。
            }
        }
    }

    private void Step(VrHands hands, bool dashboardOpen, List<VrLaserTarget> targets)
    {
        _poses = overlays.Poses();

        if (_closeRequested)
        {
            _closeRequested = false;
            Hide();
        }

        if (_openRequested is { } view)
        {
            _openRequested = null;
            Open(view);
        }

        // Layout Mode が終わったら、掴んでいても置く。掴み始めだけを Layout Mode に絞っても、続きを放っておくと、完了を押したあともグリップを離すまで窓が手に付いてくる（そのグリップは VRChat にも届いている）。
        if (_drag is not null && !layout()) PutDown();

        if (_shown)
        {
            if (dashboardOpen) HideUnderDashboard();
            else if (_onWrist) PlaceOnWrist(hands, targets);
            else PlaceInSpace(hands, targets);
        }

        SyncAwake();
    }

    /// <summary>ダッシュボードが開いている間は出さない（重なって邪魔になる）。閉じてはいない — 閉じたら同じ所・同じ画面で戻り、見えていない間は browser も眠る（<see cref="SyncAwake"/>）。掴んでいたら、動かしたところで置く。手首に付けた窓は手首の板と一緒に隠れるが、窓の側でも見る（手首の出し入れに頼りきらない）。</summary>
    private void HideUnderDashboard()
    {
        if (_drag is not null) PutDown();

        SetVisible(false);
    }

    private void PlaceInSpace(VrHands hands, List<VrLaserTarget> targets)
    {
        if (_drag is { } drag) Follow(drag, hands.For(drag.Hand));

        // ダッシュボードを閉じたあと出し直す（開いたときにも、ここで出す）。手首の上でフェードの途中に閉じた窓を、目の前で開き直すこともあるので、薄いまま残さない。
        SetAlpha(1f);
        SetVisible(true);
        _feed.Hand(_handle);

        Offer(targets, null, VrMatrix.Identity, _pose, null);
    }

    /// <summary>手首に付けた窓: 手首の板が見えている間だけ出す。手首の板の上に付けて（手首が動けば一緒に動く）、掴んでいれば動かし、手首と反対の手に差し出す。手首の手が替わったら、その手の置き場を読み直す（掴んでいたら置く）。透明度は手首の板と同じ（一緒にフェードする）。手首が消えていく途中は指させない。</summary>
    private void PlaceOnWrist(VrHands hands, List<VrLaserTarget> targets)
    {
        if (wristBoard() is not { } board)
        {
            // 手首を下ろして消え終わった・手首を見失った。掴んでいたら、動かしたところで置く（手首と同じ）。
            if (_drag is not null) PutDown();

            SetVisible(false);
            return;
        }

        // 手首が消え始めた。掴んでいたら、ここで置く（消え終わるのを待たない）。
        if (!board.Live && _drag is not null) PutDown();

        if (_hand != board.Hand)
        {
            if (_drag is not null) PutDown();

            _hand = board.Hand;
            _rel = settings.WristOffset(board.Hand);
        }

        if (_drag is { } drag) FollowOnWrist(drag, hands.For(drag.Hand), board);

        var offset = board.Offset.Then(Rel(board));

        if (_attached is not { } last || last.Device != board.Device || last.Offset != offset)
        {
            overlays.Attach(_handle, board.Device, offset);
            _attached = (board.Device, offset);
        }

        _pose = board.DevicePose.Then(offset);

        // 透明度を先に渡してから出す（出してから渡すと、1 枚だけ濃いまま見える）。
        SetAlpha(board.Alpha);
        SetVisible(true);
        _feed.Hand(_handle);

        if (board.Live) Offer(targets, board.Device, board.DevicePose, offset, Other(board.Hand));
    }

    /// <summary>
    /// 差し出す。掴んでいる間は差し出さない（窓は手に付いて動くので、点が窓の同じ所に居続けてトリガーで下のボタンが押される。しかも掴んだままのトリガーは「大きさを変える」の合図）。
    /// Layout Mode の間は押せない窓として差し出す: 押したことは browser へ送らない（<see cref="_arranging"/>）・押せる物の上でも震えない・巻く板にしない（巻く板だとレーザーが長押しを測って震える。メニューは出ないのに、出るものと思う）。
    /// </summary>
    private void Offer(List<VrLaserTarget> targets, uint? device, VrMatrix devicePose, VrMatrix offset, VrHand? onlyFor)
    {
        if (_drag is not null) return;

        var arranging = layout();

        targets.Add(new VrLaserTarget(
            _handle, device, devicePose, offset, _width, HeightInMeters, Width, Height,
            arranging ? _arranging : pointers, arranging ? NotClickable : _clickable, Scrolls: !arranging, "VR window", onlyFor,
            Grabbable: _grabbable));

        _offered = true;
    }

    private static readonly Func<bool> NotClickable = () => false;

    /// <summary>手首の板から見た今の置き場。動かしたことが無ければ板の真上（今の大きさで、下端が板の上に来る）。</summary>
    private VrMatrix Rel(WristBoardState board) => _rel ?? VrWindowPlacement.OnWrist(board.Height, HeightInMeters);

    private static VrHand Other(VrHand hand) => hand == VrHand.Left ? VrHand.Right : VrHand.Left;

    /// <summary>開く。出ていれば画面だけ切り替える（動かさない）。閉じていれば、設定の置き方で出す。空間に置く窓は頭が追えていなければ開かない（置き場が決まらない）。手首に付ける窓は、手首の板が出ていなくても開いておき、出たら見える。</summary>
    private void Open(string view)
    {
        if (_shown)
        {
            SetView(view);
            showing(view);
            _awake = true;
            logger.LogInformation("Switched the VR window to {View}", view);
            return;
        }

        var onWrist = settings.Spot == VrWindowSpot.AboveWrist;

        if (!onWrist)
        {
            if (PoseOf(_poses, 0) is not { } head)
            {
                logger.LogInformation("Did not open the VR window on {View}: the headset is not tracked", view);
                return;
            }

            ApplyWidth(ClampWidth(settings.Width));

            _pose = VrWindowPlacement.InFront(head);
            overlays.PlaceAbsolute(_handle, _pose);
            _attached = null;
        }
        else
        {
            // 大きさは置き方ごとに覚えている。置き場はこのあと手首の板の上に付けるとき（この周のうち）に決まる。
            ApplyWidth(ClampWidth(settings.WristWidth, DefaultWristWidth));

            _hand = null;
            _attached = null;
        }

        _onWrist = onWrist;
        _shown = true;
        SetView(view);
        showing(view);
        _awake = true;

        // 出すのはこの周のうちに置くとき（空間なら PlaceInSpace、手首なら手首の板が出ていれば PlaceOnWrist）。

        logger.LogInformation(
            "Opened the VR window on {View} ({Where}, {Width:0.00} m wide)",
            view, onWrist ? "on the wrist" : "in front of the head", _width);
    }

    private void Hide()
    {
        if (!_shown) return;

        if (_drag is not null) PutDown();

        _shown = false;
        SetVisible(false);

        SetView(null);
        _awake = false;
        showing(null);

        logger.LogInformation("Closed the VR window");
    }

    private void SetVisible(bool visible)
    {
        if (visible == _visible) return;

        _visible = visible;
        overlays.SetShown(_handle, visible);
    }

    private void SetAlpha(float alpha)
    {
        if (alpha == _alpha) return;

        _alpha = alpha;
        overlays.SetAlpha(_handle, alpha);
    }

    /// <summary>browser の寝起きを合わせる。開いていて見えている間だけ起こす（手首と一緒に隠れた窓は描かない）。閉じたときは <paramref name="showing"/>(null) が眠らせるので、ここは開いている間だけ見る。</summary>
    private void SyncAwake()
    {
        if (!_shown || _awake == _visible) return;

        _awake = _visible;
        awake(_visible);
    }

    /// <summary>指したままグリップを押した。掴むのは Layout Mode の間だけ（手首と同じ）— 固定の間のグリップは VRChat のもの（素通しで届いている）なので、こちらは何もしない（ログも出さない。VRChat で物を掴むたびに出ることになる）。</summary>
    private void StartDrag(VrHand hand)
    {
        if (!layout()) return;

        if (overlays.ControllerFor(hand) is not { } device || PoseOf(_poses, device) is not { } grip)
        {
            logger.LogInformation("Did not start moving the VR window: the {Hand} controller is not tracked", hand);
            return;
        }

        _drag = new Drag(hand, device, grip.Inverse().Then(_pose));
        logger.LogInformation("Moving the VR window with the {Hand} hand", hand);
    }

    /// <summary>空間に置いた窓を掴んでいる間。トリガーを引いていなければ手に付いてくる。引いている間はその場に止めて、引き始めた所から前へ押した分だけ縮める（手前へ引けば大きくする。真ん中を保ったまま）。トリガーを離したら、その時の手から見た置き場を取り直す（取り直さないと、大きさを変えている間に動かした手の分だけ窓が跳ぶ）。グリップを離したら置く。</summary>
    private void Follow(Drag drag, VrHandInput input)
    {
        if (!input.Grab || PoseOf(_poses, drag.Device) is not { } grip)
        {
            // 離した、または手を見失った（割り当てが外れてグリップが読めなくなったときもここ）。動かしたところで置く。
            PutDown();
            return;
        }

        if (input.Click)
        {
            ResizeWith(grip, VrMatrix.Identity);
            return;
        }

        if (_resize is not null)
        {
            _resize = null;
            _drag = drag = drag with { Grip = grip.Inverse().Then(_pose) };
        }

        _pose = grip.Then(drag.Grip);
        overlays.PlaceAbsolute(_handle, _pose);
    }

    /// <summary>手首に付けた窓を掴んでいる間。空間に置いた窓と同じく手に付いてくるが、覚えるのは手首の板から見た置き場（窓は手首に付いたまま。手首を動かしても、掴んだ手との位置関係で決まる）。大きさを変えている間は置き場を止めて、下端を保ったまま大きくする（ディスプレイが板の上へ伸びる。真ん中を保つと板に被る）。</summary>
    private void FollowOnWrist(Drag drag, VrHandInput input, WristBoardState board)
    {
        if (!input.Grab || PoseOf(_poses, drag.Device) is not { } grip)
        {
            PutDown();
            return;
        }

        if (input.Click)
        {
            var started = _resize?.Rel ?? Rel(board);
            var startHeight = (_resize?.Width ?? _width) * Height / Width;

            ResizeWith(grip, started);

            _rel = _resize!.Rel.Then(VrMatrix.Translation(0f, (HeightInMeters - startHeight) / 2f, 0f));
            return;
        }

        if (_resize is not null)
        {
            _resize = null;
            _drag = drag = drag with { Grip = grip.Inverse().Then(_pose) };
        }

        var world = grip.Then(drag.Grip);
        _rel = board.Offset.Inverse().Then(board.DevicePose.Inverse()).Then(world);
    }

    /// <summary>掴んだままトリガーを引いている間の大きさ。引き始めた所から前（窓の見える面の裏向き）へ押した長さで決める。</summary>
    private void ResizeWith(VrMatrix grip, VrMatrix rel)
    {
        _resize ??= new Resize(grip.Position, _width, VrWindowPlacement.Forward(_pose), rel);

        var (x, y, z) = grip.Position;
        var (fx, fy, fz) = _resize.Forward;
        var (sx, sy, sz) = _resize.From;
        var pushed = (x - sx) * fx + (y - sy) * fy + (z - sz) * fz;

        ApplyWidth(ClampWidth(VrWindowPlacement.Resized(_resize.Width, pushed)));
    }

    /// <summary>置く。大きさは置き方ごとに覚える（次に開いたときも同じ大きさ）。手首に付けた窓は置き場も手ごとに覚える。空間に置いた窓の置き場は覚えない。歪みを取ってから持つ（掴み直すたびに SteamVR の姿勢の誤差が積もる: <see cref="VrMatrix.Orthonormalized"/>）。</summary>
    private void PutDown()
    {
        _drag = null;
        _resize = null;

        if (_onWrist)
        {
            if (_rel is { } rel && _hand is { } hand)
            {
                _rel = rel.Orthonormalized();
                settings.SaveWristOffset(hand, _rel);
            }

            // 変えていなければ書かない（掴んで動かしただけで config.json の幅を書き直さない）。
            if (ClampWidth(settings.WristWidth, DefaultWristWidth) != _width) settings.SaveWristWidth(_width);
        }
        else
        {
            _pose = _pose.Orthonormalized();
            overlays.PlaceAbsolute(_handle, _pose);

            if (ClampWidth(settings.Width) != _width) settings.SaveWidth(_width);
        }

        logger.LogInformation("Put the VR window down ({Width:0.00} m wide)", _width);
    }

    private void ApplyWidth(float width)
    {
        if (width == _width) return;

        overlays.SetWidth(_handle, width);
        _width = width;
    }

    private static VrMatrix? PoseOf(IReadOnlyList<VrMatrix?> poses, uint device) =>
        device < poses.Count ? poses[(int)device] : null;
}
