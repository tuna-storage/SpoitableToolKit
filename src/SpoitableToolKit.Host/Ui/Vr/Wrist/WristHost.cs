using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Ui.Vr.Overlays;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Pointing;
using SpoitableToolKit.Host.Ui.Vr.Rendering;
using SpoitableToolKit.Host.Ui.Vr.Window;

namespace SpoitableToolKit.Host.Ui.Vr.Wrist;

/// <summary>
/// 手首の板を「いつ出すか・どこに置くか・掴んだらどう動かすか」を決めるところ。<see cref="VrHost"/> と同じく OpenVR も CEF も直に触らない。回すのは <see cref="VrHost"/>（ダッシュボードの後）。
/// 操作は自前のレーザー（<see cref="VrPointing"/>、反対の手）。SteamVR のレーザーの旗は立てない — 立てている間は SteamVR が入力を全部取り、手首が見えているだけで VRChat の操作が全部止まる。
/// 見ていない間は隠す（視界の邪魔になるうえ、XSOverlay の手首の板と重なりやすい）。隠している間は画面ごと眠らせる（描かない）。
/// 普段は固定: 掴む・大きさを変える・↺ は Layout Mode の間しか受けない（間違って掴まないように）。固定かどうかはここが持つ — 画面で取っ手を隠すだけだと、古い頼みが届けば掴めてしまう。
/// 掴むのは「指してグリップ」。グリップは VRChat にも素通しで届く（固定の間は、こちらは何もしない）。
/// Layout Mode は手首と空間の窓で共通（ここが持ち、窓は <see cref="InLayout"/> を読む）。その間は入力を全部預かる（VRChat に届かない。<see cref="VrScene"/> の captureAll）。
/// 周の中の順番（<see cref="VrScene"/>）: 板を置く（<see cref="Place"/>）→ レーザー（今の周の置き場で当てる）→ グリップを押していたら掴み始める（<see cref="Take"/>）。掴み始めた周の板はまだ動かさず、次の周から付いてくる（1 周ぶん遅れるが、跳ばない）。
/// </summary>
internal sealed class WristHost(
    IVrOverlays overlays,
    IWristSettings settings,
    VrHost.TextureSource textures,
    VrHost.PointerSink pointers,
    Action<bool> shownChanged,
    Action<WristLayout> layoutChanged,
    ILogger logger,
    bool onHead = false,
    Func<long>? clock = null,
    Func<bool>? clickable = null,
    Func<bool>? windowHeld = null) : IVrBoardOwner
{
    private readonly Func<bool> _clickable = clickable ?? (() => false);

    /// <summary>空間の窓を掴んでいるか（<see cref="VrWindowHost.Dragging"/>）。掴んでいる間は時間切れを数えない（窓を並べている最中）。</summary>
    private readonly Func<bool> _windowHeld = windowHeld ?? (() => false);

    /// <summary>今の時刻（ミリ秒）。見ない時間は周の数ではなく時計で測る — 利用者が秒で決める値なので。</summary>
    private readonly Func<long> _now = clock ?? (() => Environment.TickCount64);

    public const string Key = "dev.f2a.spoitable.wrist";

    private const string Name = "SpoitableToolKit (wrist)";

    /// <summary>
    /// 板の画素数。左が手首の板（746 × 176、ショートカット 6 つ分で固定）、右が通知の板（352 × 228、見えるのは 4 件）で、絵はその 2 枚が乗る 1 枚。間は 12。右端に 10 の余り（746 + 12 + 352 = 1110）。
    /// 2 枚は縦の真ん中をそろえる（通知の板は 4 件のまま、高さは合わせない）。何も描いていない所は透ける。
    /// 透けている所も指すと当たるので、絵は中身に合わせて小さくしてある。手首の板はショートカットの数で伸び縮みさせず、6 つ分の幅で固定している。
    /// </summary>
    public const int Width = 1120;

    public const int Height = 240;

    /// <summary>
    /// 大きさの設定（<see cref="DefaultWidth"/> など）が指している画素の幅。メートルと画素の比として使う（保存してある値の意味を変えないために残してある）。
    /// 手首の板を 746 画素で固定したときも変えていない — 変えるとタイルの実寸が縮む。代わりに、ショートカットが 6 つに満たないときは板が前より広く見える。
    /// </summary>
    public const int BoardWidth = 640;

    /// <summary>手首の真ん中が来る画素（左端から）。手首の板の真ん中（板の幅 746 の半分）。板の幅は固定なので、ショートカットを足しても手首からずれない（足りない分は左に詰めて、右が空く）。</summary>
    public const int Axis = 373;

    /// <summary>
    /// 手首のページが受け取る報せ。ログの生の行は受けない（使わないのに 1 セッションで 1 万行を超える）。
    /// 出来事（<c>logEvents</c>）とアプリのログ（<c>appLogs</c>）は通知の板に並べるので受ける。タイルの中身（<c>shortcutStatus</c>）も受ける。
    /// モジュールの push（<c>modulePush</c>）も受ける — タイルに描くページ（<see cref="SpoitableToolKit.Abstractions.Shortcuts.IWristShortcut.Page"/>）へ手首が中継する。タイルが無くても届く（心拍は 1 拍ごと。手首のページは宛先の無いものを捨てる）。
    /// トースト（<c>toast</c> / <c>toastClosed</c>）は受けない — 通知の板は履歴（<c>notifyHistory</c>）から並べる。
    /// </summary>
    public static readonly IReadOnlySet<string> Channels =
        new HashSet<string>(["wristStatus", "shortcutStatus", "notify", "notifyClosed", "notifyHistory", "configChanged", "logEvents", "appLogs", "modulePush"]);

    /// <summary>板の実寸（手首の板 <see cref="BoardWidth"/> 画素ぶんの横幅）の既定。実機で Layout Mode から合わせた 0.24 m。</summary>
    public const float DefaultWidth = 0.24f;

    public const float MinWidth = 0.08f;

    public const float MaxWidth = 0.40f;

    /// <summary>1 メートルを何刻みにするか（50 = 2 cm 刻み）。刻みはこれで割って作る（<see cref="ClampWidth"/>）。</summary>
    private const float StepsPerMeter = 50f;

    public const float WidthStep = 1f / StepsPerMeter;

    /// <summary>向けてから出すまでに待つ周の数（周はヘッドセットの速さ: 90 Hz で 44 ms、120 Hz で 33 ms）。腕を振った拍子に一瞬だけ出るのを防ぐ。隠す方は待たない — 見ていないのに視界を塞いでいる時間を作りたくない。</summary>
    public const int ShowAfterTicks = 4;

    /// <summary>出るとき・消えるときに透明度を動かす時間（ミリ秒）。出も消も同じ。</summary>
    public const int FadeMs = 150;

    /// <summary>1 周で進める時間の上限。周が止まったあと（引っかかった・眠った）に一気に飛ばないようにする。下は 0 を許す（<see cref="Environment.TickCount64"/> の刻みは 15 ms ほどで、90 Hz の 1 周より粗い）。</summary>
    private const long MaxFadeStepMs = 100;

    private readonly OverlayFeed _feed = new(overlays, textures);

    private ulong _handle;

    /// <summary>出ているか（指してよいか）。見えているか（<see cref="_visible"/>）とは別 — 消えていく途中は見えているが出ていない。</summary>
    private bool _shown;

    private bool _visible;

    private float _alpha;

    /// <summary>前に透明度を動かした時刻。null は「まだ測っていない」（最初の周は動かさない）。</summary>
    private long? _fadedAt;

    private int _facingTicks;

    private uint? _attachedTo;

    private VrMatrix? _attachedOffset;

    private float _attachedWidth;

    private VrHand _hand;

    private Drag? _drag;

    /// <summary>周の外（画面のスレッド）から頼まれたこと。周の中で 1 回だけ拾う。</summary>
    private int _resetRequested;

    /// <summary>Layout Mode の出入り。0 = 無し / 1 = 入る / 2 = 出る（後から来た方が勝つ）。</summary>
    private int _layoutRequested;

    /// <summary>大きさの頼み。刻みの数を足し込む（周の間に 2 回押されたら 2 刻み）。</summary>
    private int _resizeRequested;

    /// <summary>Layout Mode か。周のスレッドの外からも読む（空間の窓のページが読み終わったときに聞き直す: <see cref="InLayout"/>）ので volatile。</summary>
    private volatile bool _layout;

    /// <summary>Layout Mode のまま、どの板も指さず掴んでもいなくなった時刻。触っている間・固定の間は null。</summary>
    private long? _idleSince;

    /// <summary>板に当てている横幅。変わったときだけ OpenVR に渡す。</summary>
    private float _width;

    private bool _failed;

    private sealed record Drag(uint Device, VrMatrix Grip);

    /// <summary>この周に板を差し出したときの様子。指したままグリップを押したら、ここから掴み始める。</summary>
    private sealed record Offered(VrHand Other, uint? Device, VrMatrix? Pose, VrMatrix WristPose, VrMatrix Offset);

    private Offered? _offered;

    private bool _pointed;

    /// <summary>作れなかったら手首は休む（ダッシュボードは動き続ける）。</summary>
    public bool Running => _handle != 0;

    public bool Shown => _shown;

    public bool Visible => _visible;

    public float Alpha => _alpha;

    public bool Dragging => _drag is not null;

    public bool Pointed => _pointed;

    /// <summary>板の今。出ていなければ null。空間の窓を手首の板の上に付けるのに使う（窓は手首の後に置かれるので、同じ周の値が読める）。窓は手首の出し入れをそのまま借りる（手首を下ろすと一緒に隠れる）。</summary>
    public WristBoardState? Board { get; private set; }

    public float HeightInMeters => _width * Height / BoardWidth;

    public int Uploads => _feed.Uploads;

    /// <summary>Layout Mode か（固定でないか）。手首と空間の窓で共通（窓を掴めるのも、入力を全部預かるのもこの間だけ）。どのスレッドから読んでもよい。</summary>
    public bool InLayout => _layout;

    /// <summary>絵 1 枚（<see cref="Width"/> 画素）の実寸の横幅（メートル）。OpenVR に渡すのはこちら。大きさの設定は <see cref="Size"/>（手首の板 <see cref="BoardWidth"/> 画素ぶん）。</summary>
    public float WidthInMeters => _width * Width / BoardWidth;

    public float Size => _width;

    /// <summary>絵の真ん中を置く所。手首の真ん中（<paramref name="offset"/>）から、軸と絵の真ん中の差だけ横へずらす。向いているか・掴む・空間の窓の置き場は手首の真ん中で考えるので、ずらすのは OpenVR に付けるときと指すときだけ。</summary>
    private VrMatrix Centre(VrMatrix offset) =>
        offset.Then(VrMatrix.Translation(_width * (Width / 2f - Axis) / BoardWidth, 0f, 0f));

    /// <summary>置き場と大きさを既定へ戻す。周の外から呼んでよい。Layout Mode の外では捨てる。</summary>
    public void RequestReset() => Interlocked.Exchange(ref _resetRequested, 1);

    /// <summary>Layout Mode に入る / 出る。周の外から呼んでよい。入るのは板が出ている間だけ受ける（出ていないのに来たら古い頼み）。出るのはいつでも受ける。</summary>
    public void RequestLayout(bool on) => Interlocked.Exchange(ref _layoutRequested, on ? 1 : 2);

    /// <summary>大きさを <paramref name="steps"/> 刻み変える（負で小さく）。周の外から呼んでよい。Layout Mode の外では捨てる。</summary>
    public void RequestResize(int steps) => Interlocked.Add(ref _resizeRequested, steps);

    /// <summary>
    /// 横幅を範囲に収めて刻みに揃える。null・数でないものは既定（手で書き換えた <c>config.json</c> も来る）。
    /// 刻みに揃えるのは <c>k / 50</c> で割る形にしてある — <c>0.18f + 0.02f</c> のように足し込むと浮動小数の端数が残り、同じ幅なのに「変わった」と判断して付け直すことになる。
    /// </summary>
    public static float ClampWidth(float? meters)
    {
        if (meters is not { } value || !float.IsFinite(value)) return DefaultWidth;

        var snapped = MathF.Round(value * StepsPerMeter) / StepsPerMeter;

        return Math.Clamp(snapped, MinWidth, MaxWidth);
    }

    public void Start()
    {
        if (_handle != 0) return;

        _failed = false;
        _width = ClampWidth(settings.Width);
        _handle = overlays.CreateOverlay(Key, Name, WidthInMeters);

        if (_handle == 0)
        {
            logger.LogWarning("Could not create the wrist overlay; the dashboard carries on without it");
            return;
        }

        // SteamVR のレーザーでは触らせない（入力の口も旗も無し）。触るのは自前のレーザーだけ（VrPointing）。
        _hand = settings.Hand;
        logger.LogInformation("The wrist overlay is up ({Where})", onHead ? "in front of the head, for development" : _hand);
    }

    public void Stop()
    {
        if (_handle == 0) return;

        overlays.Destroy(_handle);

        _handle = 0;
        _attachedTo = null;
        _attachedOffset = null;
        _drag = null;
        _offered = null;
        _pointed = false;
        Board = null;
        _facingTicks = 0;
        _feed.Forget();
        HideNow(notifyOverlay: false);
        _fadedAt = null;

        // 次に付けたときは固定から始める。
        if (_layout) SetLayout(false);
    }

    public void Place(VrHands hands, bool dashboardOpen, List<VrLaserTarget> targets)
    {
        _offered = null;
        Board = null;

        if (_handle == 0 || _failed) return;

        Guard(() => Step(hands, dashboardOpen, targets));
    }

    /// <summary>周の後半: 反対の手が指したままグリップを押したら掴み始める。隠れている周も、Layout Mode の時間切れは数える。</summary>
    public void Take(VrHands hands, VrPointed pointed)
    {
        _pointed = false;

        if (_handle == 0 || _failed) return;

        Guard(() =>
        {
            if (_offered is { } offered)
            {
                var step = pointed.For(offered.Other);

                _pointed = step.Target == _handle;

                if (_pointed && step.GrabPressed) StartDrag(offered);
            }

            LockWhenIdle(pointed);
        });
    }

    /// <summary>手首が転んでも周ごとは止めない — 外へ投げると <see cref="VrLoop"/> がダッシュボードごと止める。手首だけ畳んで、1 回だけ残す。</summary>
    private void Guard(Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            _failed = true;
            logger.LogError(ex, "The wrist overlay stopped; the dashboard carries on");

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
        var hand = settings.Hand;

        // 手を替えたら掴みはやめる。掴んだ時の手首はもう別の手。動かしたところまでは前の手の置き場として覚える（手首を見失ったときと同じ）。
        if (hand != _hand)
        {
            if (_drag is not null && _attachedOffset is { } moved) Release(moved);

            _hand = hand;
            _drag = null;
        }

        TakeLayoutRequest();

        // ↺ と大きさは Layout Mode の間だけ（頼みは毎周捨てるので、固定の間に来たものは後から効かない）。
        if (Interlocked.Exchange(ref _resetRequested, 0) == 1 && _layout)
        {
            _drag = null;
            settings.SaveOffset(_hand, null);
            settings.SaveWidth(null);
        }

        if (Interlocked.Exchange(ref _resizeRequested, 0) is var steps and not 0 && _layout)
        {
            var next = ClampWidth(_width + steps * WidthStep);

            if (next != _width) settings.SaveWidth(next);
        }

        ApplyWidth();

        var device = onHead ? 0u : overlays.ControllerFor(_hand);
        var poses = overlays.Poses();

        var other = Other(_hand);
        var otherInput = hands.For(other);

        if (device is not { } wrist || Pose(poses, wrist) is not { } wristPose)
        {
            // 手首が見えていない（コントローラーが無い・追えていない）。付け先が無いので出さない。
            // 動かしている最中なら、動かしたところまでで覚える（反対の手を見失ったときと同じ。覚えずに捨てると、追跡が戻ったとき掴む前の所へ跳ぶ）。
            if (_drag is not null && _attachedOffset is { } moved) Release(moved);

            _drag = null;
            _facingTicks = 0;

            // フェードせずにすぐ消す（付け先が追えていないので、薄くしていく間の置き場が無い）。
            HideNow();
            return;
        }

        if (dashboardOpen)
        {
            // ダッシュボードが開いている間は隠す（重なって邪魔になる）。閉じたら向きで出し直す — 腕を振ったときと同じく、向いてから少し待って出る。手首に付けた窓は手首の出し入れを借りるので、一緒に隠れる。
            // 動かしている最中に開いたら、動かしたところで覚える（手首を見失ったときと同じ）。
            if (_drag is not null && _attachedOffset is { } held) Release(held);

            _drag = null;
            _facingTicks = 0;
            SetShown(false);

            Fade(wrist, wristPose, _attachedOffset ?? Offset());
            return;
        }

        var offset = Offset();

        if (_drag is { } drag)
        {
            offset = Follow(drag, poses, wristPose, offset, otherInput.Grab);
        }

        // 大きさが変わったときも付け直す。軸からのずれは大きさに比例するので、同じ offset でも絵の真ん中は動く。
        if (_attachedTo != wrist || _attachedOffset != offset || _attachedWidth != _width)
        {
            _attachedWidth = _width;
            overlays.Attach(_handle, wrist, Centre(offset));
            _attachedTo = wrist;
            _attachedOffset = offset;
        }

        SetShown(_drag is not null || onHead || LookedAt(poses, wristPose, offset));

        Fade(wrist, wristPose, offset);

        if (_shown) Offer(other, poses, wrist, wristPose, offset, targets);
    }

    /// <summary>周の終わり: 透明度を進め、見えている間は板の今を出して絵を上げる（消えていく途中も描き続ける）。</summary>
    private void Fade(uint wrist, VrMatrix wristPose, VrMatrix offset)
    {
        StepAlpha();

        if (!_visible) return;

        Board = new WristBoardState(_hand, wrist, wristPose, offset, HeightInMeters, _alpha, _shown);

        _feed.Hand(_handle);
    }

    /// <summary>透明度を <see cref="_shown"/> の方へ、前の周からの時間ぶん動かす（<see cref="FadeMs"/> で 0 → 1）。消え終わったら SteamVR から下ろし、browser を眠らせる（消えていく途中に眠らせると絵が止まる）。</summary>
    private void StepAlpha()
    {
        var now = _now();
        var elapsed = Math.Clamp(now - (_fadedAt ?? now), 0, MaxFadeStepMs);
        _fadedAt = now;

        var target = _shown ? 1f : 0f;

        if (_alpha != target)
        {
            var step = (float)elapsed / FadeMs;
            var next = _shown ? Math.Min(1f, _alpha + step) : Math.Max(0f, _alpha - step);

            if (next != _alpha)
            {
                _alpha = next;
                if (_visible) overlays.SetAlpha(_handle, _alpha);
            }
        }

        if (!_shown && _visible && _alpha <= 0f)
        {
            _visible = false;
            overlays.SetShown(_handle, false);
            shownChanged(false);
        }
    }

    private static VrHand Other(VrHand hand) => hand == VrHand.Left ? VrHand.Right : VrHand.Left;

    /// <summary>反対の手に差し出す。指せるのは出ている間だけ（見えていない物を押せてしまう）。掴んでいる間も差し出さない — 板は手に付いて動くので点は板の同じ所に居続け、トリガーを引くとその下のボタンが押される。ダッシュボードが開いている間は隠れているので、ここまで来ない。</summary>
    private void Offer(VrHand other, IReadOnlyList<VrMatrix?> poses, uint wrist, VrMatrix wristPose, VrMatrix offset, List<VrLaserTarget> targets)
    {
        if (_drag is not null) return;

        var otherDevice = overlays.ControllerFor(other);
        var otherPose = otherDevice is { } index ? Pose(poses, index) : null;

        targets.Add(new VrLaserTarget(
            _handle, wrist, wristPose, Centre(offset), WidthInMeters, HeightInMeters, Width, Height,
            pointers, _clickable, Scrolls: false, "wrist overlay", OnlyFor: other));

        _offered = new Offered(other, otherDevice, otherPose, wristPose, offset);
    }

    private VrMatrix Offset() =>
        onHead ? WristPlacement.InFrontOfHead : settings.SavedOffset(_hand) ?? WristPlacement.DefaultOffset(_hand);

    /// <summary>Layout Mode の出入り。入るのは出ている間だけ（掴むのと同じく、出ていないのに来たら古い頼み）。掴んでいる最中に「完了」が来たら、動かしたところで離して覚える。</summary>
    private void TakeLayoutRequest()
    {
        switch (Interlocked.Exchange(ref _layoutRequested, 0))
        {
            case 1 when _shown && !_layout:
                SetLayout(true);
                break;

            case 2 when _layout:
                // 掴んでいる間は必ず付け直している（_attachedOffset がある）。
                if (_drag is not null && _attachedOffset is { } offset) Release(offset);
                SetLayout(false);
                break;
        }
    }

    /// <summary>横幅を板に当てる。設定から毎周読むので、↺ も大きさの頼みもここを通って効く。</summary>
    private void ApplyWidth()
    {
        var width = ClampWidth(settings.Width);

        if (width == _width) return;

        _width = width;
        overlays.SetWidth(_handle, WidthInMeters);

        if (_layout) layoutChanged(new WristLayout(true, width));
    }

    /// <summary>
    /// Layout Mode のまま、どの板も指さず・掴んでもいない時間が続いたら固定に戻す（秒数は設定。0 で戻さない）。「完了」を押し忘れると、次に手首を見たとき取っ手が出ていて間違って掴める — 固定の約束が崩れる。しかも Layout Mode の間は入力を全部預かるので、押し忘れたままだと VRChat が動かせない。
    /// Layout Mode が手首と空間の窓で共通なので、腕を下ろして窓を並べている間に切れないよう、手首・窓のどれかを指している間と、どれかを掴んでいる間は数えない。手首を見ているだけで指していなければ数える。ダッシュボードが開いている間は、どの板も出ていないので数える。
    /// </summary>
    /// </summary>
    private void LockWhenIdle(VrPointed pointed)
    {
        var busy = !_layout
            || _drag is not null
            || _windowHeld()
            || pointed.For(VrHand.Left).Target != 0
            || pointed.For(VrHand.Right).Target != 0;

        if (busy)
        {
            _idleSince = null;
            return;
        }

        var now = _now();
        _idleSince ??= now;

        var seconds = settings.LayoutLockSeconds;

        if (seconds > 0 && now - _idleSince.Value >= seconds * 1000L) SetLayout(false);
    }

    private void SetLayout(bool on)
    {
        _layout = on;
        _idleSince = null;
        logger.LogInformation("Layout mode on the wrist overlay: {On}", on);
        layoutChanged(new WristLayout(on, _width));
    }

    /// <summary>指したままグリップを押した。掴むのは Layout Mode の間だけ — 固定の間のグリップは VRChat のもの（素通しで届いている）なので、こちらは何もしない（ログも出さない。VRChat で物を掴むたびに出ることになる）。指すのは出ている間だけなので、隠れている板はここに来ない。</summary>
    private void StartDrag(Offered offered)
    {
        var (other, device, pose, wristPose, offset) = offered;

        if (!_layout) return;

        // 断った理由をログに残す。「掴めなかった」が実機で起きたとき、何で断ったのかを後から追えるように。
        if (onHead)
        {
            logger.LogInformation("Did not start moving the wrist overlay: the board is in front of the head (development)");
            return;
        }

        if (device is not { } grip || pose is not { } gripPose)
        {
            logger.LogInformation("Did not start moving the wrist overlay: the {Hand} controller is not tracked", other);
            return;
        }

        _drag = new Drag(grip, gripPose.Inverse().Then(wristPose.Then(offset)));
        logger.LogInformation("Moving the wrist overlay with the {Hand} hand", other);
    }

    /// <summary>掴んでいる間。反対の手に付いてくるように、手首から見た置き場を毎周計算し直す。グリップを離したら離す（状態を毎周読むので、離した報せの取りこぼしは無い）。</summary>
    private VrMatrix Follow(Drag drag, IReadOnlyList<VrMatrix?> poses, VrMatrix wristPose, VrMatrix offset, bool holding)
    {
        if (!holding || Pose(poses, drag.Device) is not { } gripPose)
        {
            // 離した、または反対の手を見失った（割り当てが外れてグリップが読めなくなったときもここ）。
            // 動かしたところまでで覚える（掴む前の置き場へ戻すと、動かした分が黙って消える）。
            var last = _attachedOffset ?? offset;
            Release(last);
            return last;
        }

        return wristPose.Inverse().Then(gripPose.Then(drag.Grip));
    }

    private void Release(VrMatrix offset)
    {
        _drag = null;

        // 歪みを取ってから覚える。掴み直すたびに SteamVR の姿勢の誤差が積み重なる（VrMatrix.Orthonormalized）。
        settings.SaveOffset(_hand, offset.Orthonormalized());
        logger.LogInformation("Put the wrist overlay down");
    }

    /// <summary>手首の板を見ているか: 板の真ん中が視野の真ん中の近くにあり、面がこちらを向いている（<see cref="WristPlacement.ShouldShow"/>）。見るのは手首の真ん中（OpenVR に付ける絵の真ん中ではない。絵は右に通知の板の分だけ広い）。</summary>
    private bool LookedAt(IReadOnlyList<VrMatrix?> poses, VrMatrix wristPose, VrMatrix offset)
    {
        if (Pose(poses, 0) is not { } head) return false;

        var board = wristPose.Then(offset);
        var facing = WristPlacement.Facing(head, board);
        var gaze = WristPlacement.Gaze(head, board);

        var angles = WristAngles.FromDegrees(settings.ShowWithinDegrees, settings.HideBeyondDegrees);
        if (!WristPlacement.ShouldShow(_shown, facing, gaze, angles))
        {
            _facingTicks = 0;
            return false;
        }

        if (_shown) return true;

        // 向けてすぐには出さない。腕を振った拍子に一瞬だけ出るのを防ぐ。
        return ++_facingTicks >= ShowAfterTicks;
    }

    /// <summary>出す / 隠す（指してよいか）。出すときは透明度を今の値から上げ始める（消えていく途中に向け直したら、そこから戻る）。隠すときは薄くしていくだけで、SteamVR から下ろすのは消え終わってから（<see cref="StepAlpha"/>）。</summary>
    private void SetShown(bool shown)
    {
        if (shown == _shown) return;

        _shown = shown;

        if (!shown)
        {
            _facingTicks = 0;
            return;
        }

        if (_visible) return;

        // 透明度を先に渡してから出す（出してから渡すと、1 枚だけ濃いまま見える）。
        _visible = true;
        overlays.SetAlpha(_handle, _alpha);
        overlays.SetShown(_handle, true);

        shownChanged(true);
    }

    private void HideNow(bool notifyOverlay = true)
    {
        _shown = false;
        _facingTicks = 0;
        _alpha = 0f;

        if (!_visible) return;

        _visible = false;
        if (notifyOverlay) overlays.SetShown(_handle, false);
        shownChanged(false);
    }

    private static VrMatrix? Pose(IReadOnlyList<VrMatrix?> poses, uint device) =>
        device < poses.Count ? poses[(int)device] : null;
}
