using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Ui.Vr.Overlays;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;

namespace SpoitableToolKit.Host.Ui.Vr.Pointing;

/// <summary>
/// 自前のポインタ 1 本（片手の分）。手首と空間の窓とで使う（両手の分をまとめるのは <see cref="VrPointing"/>）。
/// SteamVR のレーザー（<c>MakeOverlaysInteractiveIfVisible</c>）をやめて自前にしたのは、旗が立っている間は SteamVR が入力を全部取り、手首が見えているだけで VRChat の操作が全部止まったから。こちらは読むだけで取り上げないので、押したトリガーは VRChat にも届く。
/// 板に向いたときだけ、当たった所に点を出す（線は出さない）。入るときは板 + <see cref="EnterMargin"/>、出るときは板 + <see cref="LeaveMargin"/> で測る（縁で出たり消えたりしない）。乗ったとき・押したときに軽く震わせる。
/// 板が何枚あっても、指すのはいちばん近い 1 枚（手首の板の奥に窓がある、など）。別の板へ移ったら、前の板には外れたことを送ってから移る。
/// 押しっぱなしで板から外れたら、外れた所で離したことにする（CEF にボタンが押されたまま残らない）。外れたら <see cref="VrPointerKind.Leave"/> も送る（送らないと <c>:hover</c> が残る）。
/// 巻く板で引いて、動かしながら離したら、その速さのまま流れ続けて、だんだん緩んで止まる（慣性）。送るのはこれまでの巻きと同じホイール（<see cref="VrPointerKind.Pan"/>）なので、ページは何もしなくても効く。
/// 流れるのは自前のレーザーで引いたときだけ — マウスのホイール・デスクトップの窓・SteamVR のレーザーのスティックには効かない。手首は巻かない板なので流れない（通知欄はページが自分で流す）。
/// </summary>
internal sealed class VrLaser(IVrOverlays overlays, ILogger logger, string key, Func<long>? now = null)
{
    /// <summary>
    /// 今の時刻（ミリ秒）。長押しと流れを周の数ではなく時間で測る（周の速さはヘッドセットで変わる）。
    /// 既定は <see cref="System.Diagnostics.Stopwatch"/>。<see cref="Environment.TickCount64"/> は使わない — 刻みが 10〜16 ms なので、90 Hz（1 周 11 ms）では周ごとの間が 0 と 16 を行き来し、流れが 1 周おきに止まって進む。
    /// </summary>
    private readonly Func<long> _now = now ?? (() => System.Diagnostics.Stopwatch.GetTimestamp() * 1000 / System.Diagnostics.Stopwatch.Frequency);

    public const float DotWidth = 0.012f;

    /// <summary>点を板からどれだけ浮かせるか。板と同じ面に置くと、どちらが手前か決まらずにちらつく。</summary>
    public const float DotLift = 0.002f;

    public const float EnterMargin = 0.01f;

    public const float LeaveMargin = 0.03f;

    /// <summary>これより遠い板は指さない。空間の窓は掴んで遠くへ置けるので、手の届く所より広く取る。</summary>
    public const float Reach = 4.0f;

    public const float HoverPulseSeconds = 0.004f;

    public const float HoverPulseAmplitude = 0.2f;

    public const float PressPulseSeconds = 0.012f;

    public const float PressPulseAmplitude = 0.4f;

    /// <summary>巻く板で、トリガーを引いたまま板の上でこれだけ動いたら押すのをやめて巻きに入る（メートル）。画素ではなく板の上の長さで測る — 窓の大きさを変えても、手の振れに対する効き方が変わらない。トリガーを引いた拍子にコントローラーが振れても、押したことが消えない程度に取る。</summary>
    public const float DragThreshold = 0.012f;

    /// <summary>動かした画素をホイールの量へ直す掛け算。CEF のホイールは 120 で約 100 画素なので、指に付いてくるように 1.2。</summary>
    public const float DragScrollGain = 1.2f;

    /// <summary>巻く板で、動かさずにこれだけ引き続けたら右クリックを送る（ミリ秒）。ページはいつもの右クリックのメニュー（貼り付けなど）を出す。「動かさずに」は <see cref="DragThreshold"/> の内。巻きに入ったら長押しにはしない。</summary>
    public const long LongPressMilliseconds = 500;

    public const float LongPressPulseSeconds = 0.02f;

    public const float LongPressPulseAmplitude = 0.5f;

    /// <summary>SteamVR の当たりと並べてログに出す回数（入ったときだけ、繋ぐたびに数え直す）。</summary>
    public const int CrossChecks = 3;

    // 離したあとも流れる（慣性）。数は窓のもの（画素は窓の 1280 × 720）。手首の上限 3 px/ms は高さ 200 px ほどの通知欄に合わせた値なので使わない。

    public const long GlideWindowMs = 100;

    /// <summary>この時間より短い動きからは速さを取らない（ミリ秒）。同じ瞬間の動きを割ると速さが果てしなくなる（周が詰まった・試験で時計を進めない）ので、ヘッドセットの 1 周（90 Hz で 11 ms）より短く取る。</summary>
    public const long GlideMinSpanMs = 8;

    public const float GlideMinSpeed = 0.05f;

    /// <summary>速さの上限（画素 / ミリ秒）。流れる距離は最大で「これ × <see cref="GlideDecayMs"/>」= 約 2000 画素（窓の高さ 720 の 3 枚ほど）。</summary>
    public const float GlideMaxSpeed = 6f;

    /// <summary>緩め方。速さが 1/e になるまでの時間（ミリ秒）。流れる距離は「離した速さ × これ」。</summary>
    public const float GlideDecayMs = 325f;

    /// <summary>1 周で進める時間の上限（ミリ秒）。周が詰まったあとで一気に跳ばない。</summary>
    public const long GlideMaxStepMs = 50;

    private ulong _dot;

    private bool _visible;

    private VrLaserTarget? _target;

    private VrHit? _hit;

    private bool _pressed;

    /// <summary>巻く板でトリガーを引いたまま、まだ Down を送っていない。押したのか巻くのかは動いたかで決まるので、離すまで待つ（動かずに離したら、その場で Down と Up を送る）。</summary>
    private VrHit? _held;

    private long _heldSince;

    /// <summary>長押しで右クリックを送った。トリガーを離すまで、ほかに何も送らない — メニューは点の下に開くので、離したときにクリックを送るとメニューの 1 行目が押され、動かすと巻いてメニューが閉じる。</summary>
    private bool _longPressed;

    private VrHit? _dragged;

    /// <summary>巻いている間の、周ごとの位置（時刻・画素）。動かなかった周も積む（止めてから離したら速さが落ちる）。</summary>
    private readonly List<(long At, int X, int Y)> _trail = [];

    private GlideState? _glide;

    /// <summary>この引きは流れを止めるための 1 押しだった。離してもクリックにしない・長押しにもしない（巻くのはよい）。流れている中身の上はボタンや行だらけなので、止めた拍子に下の物が押されると困る。</summary>
    private bool _caught;

    private bool _click;

    private bool _grab;

    private bool _clickable;

    private int _checks;

    public bool Pointing => _hit is not null;

    public ulong Target => _target?.Handle ?? 0;

    public bool Visible => _visible;

    public ulong GlidingOn => _glide?.Handle ?? 0;

    public void StopGliding() => _glide = null;

    public void Start()
    {
        if (_dot != 0) return;

        _dot = overlays.CreateSprite(key + ".dot", "SpoitableToolKit (pointer dot)", VrSprite.Dot, DotWidth);
        _checks = 0;

        if (_dot == 0) logger.LogWarning("Could not create the pointer dot; pointing still works, but nothing is drawn");
    }

    public void Stop()
    {
        _glide = null;
        Leave();

        if (_dot != 0) overlays.Destroy(_dot);

        _dot = 0;
        _visible = false;
        _click = false;
        _grab = false;
    }

    /// <summary>1 周。<paramref name="targets"/> はいま指してよい板（隠れている・掴んでいる・ダッシュボードが開いている板は入らない）。押し始め・離しは前の周との違いで決める。板の外で押したトリガーを持ったまま板に入っても押したことにはしない（VRChat で何かを持ったまま腕を動かしただけ）。</summary>
    public VrLaserStep Tick(VrHand hand, VrHandInput input, IReadOnlyList<VrLaserTarget> targets)
    {
        var clickPressed = input.Click && !_click;
        var clickReleased = !input.Click && _click;
        var grabPressed = input.Grab && !_grab;

        _click = input.Click;
        _grab = input.Grab;

        var aim = input.Aim;
        var found = aim is { } pointedWith ? Nearest(pointedWith, targets) : null;

        // 板の上で押したら、流れているのを止める（押した物へ届く前に）。流れている板を押したなら、その 1 押しは止めるだけ。板の外のトリガー（VRChat で物を掴むなど）では止めない。
        var caught = clickPressed && found is { } pressedOn && _glide?.Handle == pressedOn.Target.Handle;

        if (clickPressed && found is not null) _glide = null;

        Glide(targets);

        if (found is not { } nearest)
        {
            // 巻いている途中で外れたら、その速さで流す（指が画面の外へ出ても中身は流れる）。
            Throw();
            Leave();
            return default;
        }

        var (target, at) = nearest;

        // 別の板へ移ったら、前の板からは外れたことにする（押したままなら離す、:hover を外す）。巻いていたなら前の板を流す。
        if (_target is { } last && last.Handle != target.Handle)
        {
            Throw();
            Leave();
        }

        var entering = _hit is null;
        var moved = _hit is not { } previous || previous.X != at.X || previous.Y != at.Y;

        if (entering) CrossCheck(target, aim!.Value, at);

        _target = target;
        _hit = at;

        if (moved) target.Sink(VrPointerKind.Move, at.X, at.Y, VrPointerButton.Left, 0, 0);

        if (clickPressed)
        {
            // 掴む手の上なら巻かない。押す・動かす・離すをそのまま送り、ページが自分で掴んで動かす（並べ替えのつまみなど）。
            // 決めるのは引いた瞬間だけ — 掴んで動かすとカーソルは変わる（grabbing・外れて矢印）が、途中で巻きに切り替えない。
            // ただし流れを止めた 1 押しは掴まない（動いていた中身の、狙っていない物を掴む）。巻く 1 押しとして扱い、離してもクリックにしない。
            if (target.Scrolls && (target.Grabbable?.Invoke() != true || caught))
            {
                // まだ押さない。動いたら巻く、動かずに離したら押す、動かずに引き続けたら右クリック。
                _held = at;
                _heldSince = _now();
                _dragged = null;
                _longPressed = false;
                _caught = caught;
                _trail.Clear();
            }
            else
            {
                target.Sink(VrPointerKind.Down, at.X, at.Y, VrPointerButton.Left, 0, 0);
                _pressed = true;
            }

            overlays.Pulse(hand, PressPulseSeconds, PressPulseAmplitude);
        }
        else if (input.Click && _held is { } from)
        {
            Drag(target, from, at);

            if (_dragged is null && !_longPressed && !_caught && _now() - _heldSince >= LongPressMilliseconds)
            {
                // 引き始めた所で右クリック（点が動いていても、巻きに入る前の振れの内なので、狙った所はそこ）。
                target.Sink(VrPointerKind.Down, from.X, from.Y, VrPointerButton.Right, 0, 0);
                target.Sink(VrPointerKind.Up, from.X, from.Y, VrPointerButton.Right, 0, 0);
                overlays.Pulse(hand, LongPressPulseSeconds, LongPressPulseAmplitude);
                _longPressed = true;
            }
        }
        else if (clickReleased)
        {
            Release(target, at);
        }

        // 乗ったときに震わせる。CEF のカーソルは動かしたあとで変わるので、1〜2 周遅れて来る。隣り合ったボタンを渡ると手の形のままなので、震えるのは最初の 1 つだけ。
        var clickable = target.Clickable();

        if (clickable && !_clickable) overlays.Pulse(hand, HoverPulseSeconds, HoverPulseAmplitude);

        _clickable = clickable;

        Draw(target, at);

        return new VrLaserStep(target.Handle, grabPressed, clickPressed);
    }

    /// <summary>いちばん近い当たり。いま指している板は出る側の余白で、ほかの板は入る側の余白で測る（指している板の縁で、隣の板と行ったり来たりしない）。</summary>
    private (VrLaserTarget Target, VrHit Hit)? Nearest(VrMatrix aim, IReadOnlyList<VrLaserTarget> targets)
    {
        (VrLaserTarget Target, VrHit Hit)? best = null;

        foreach (var target in targets)
        {
            var margin = _target?.Handle == target.Handle && _hit is not null ? LeaveMargin : EnterMargin;

            if (VrBoardHit.Cast(aim, target.Board, margin, Reach) is not { } hit) continue;

            if (best is not { } current || hit.Distance < current.Hit.Distance) best = (target, hit);
        }

        return best;
    }

    /// <summary>引いたまま動かした。板の上で <see cref="DragThreshold"/> 動いたら巻きに入り、以後は押したことにしない。巻く量は引き始めた所から数える（中身が指に付いてくる）。⚠ 向きは実機で確かめていない: 下へ動かす（画素の Y が増える）と中身も下へ動く = 上へ巻く = ホイールの正。</summary>
    private void Drag(VrLaserTarget target, VrHit from, VrHit at)
    {
        if (_longPressed) return;

        if (_dragged is null)
        {
            var dx = at.LocalX - from.LocalX;
            var dy = at.LocalY - from.LocalY;

            if (dx * dx + dy * dy < DragThreshold * DragThreshold) return;

            _dragged = from;
        }

        Remember(at);

        if (_dragged is not { } last || (last.X == at.X && last.Y == at.Y)) return;

        var wheelX = (int)MathF.Round((at.X - last.X) * DragScrollGain);
        var wheelY = (int)MathF.Round((at.Y - last.Y) * DragScrollGain);

        target.Sink(VrPointerKind.Pan, at.X, at.Y, VrPointerButton.Left, wheelX, wheelY);
        _dragged = at;
    }

    /// <summary>トリガーを離した。巻く板で動かさずに離したら、引き始めた所で押して離す（クリック）。巻いていたならクリックは送らない（巻いたあとにクリックが出ると、巻き終わりの所のボタンが押される）— 離した周の動きを巻いて、動かしながら離したなら流す。流れを止めた 1 押し（<see cref="_caught"/>）はクリックにしない。</summary>
    private void Release(VrLaserTarget target, VrHit at)
    {
        // 巻いていたなら、離した周の動きも巻く（離した周を数えないと、速さが 1 周古くなり、巻き始めた次の周で離すと流れない）。巻きに入る前なら動かしても巻かない（離した拍子の振れでクリックが巻きに変わらない）。
        if (_dragged is not null && _held is { } start) Drag(target, start, at);

        if (_pressed)
        {
            target.Sink(VrPointerKind.Up, at.X, at.Y, VrPointerButton.Left, 0, 0);
            _pressed = false;
        }
        else if (_held is { } from && _dragged is null && !_longPressed && !_caught)
        {
            target.Sink(VrPointerKind.Down, from.X, from.Y, VrPointerButton.Left, 0, 0);
            target.Sink(VrPointerKind.Up, from.X, from.Y, VrPointerButton.Left, 0, 0);
        }

        Throw();

        _held = null;
        _dragged = null;
        _longPressed = false;
        _caught = false;
    }

    /// <summary>巻いている間の 1 周ぶんの位置を積む。速さを取る時間より古いものは捨てる。</summary>
    private void Remember(VrHit at)
    {
        var now = _now();

        _trail.Add((now, at.X, at.Y));

        var stale = 0;
        while (stale < _trail.Count && now - _trail[stale].At > GlideWindowMs) stale++;

        if (stale > 0) _trail.RemoveRange(0, stale);
    }

    /// <summary>巻いている途中で手を離した（トリガーを離した・板から外れた）。離す直前の速さで流し始める。止めてから離した・短すぎて速さが取れない・巻いていなかったら流さない。</summary>
    private void Throw()
    {
        if (_dragged is not { } last || _target is not { } target) return;

        var now = _now();
        var (vx, vy) = ReleaseSpeed(now);

        _trail.Clear();

        if (MathF.Sqrt(vx * vx + vy * vy) < GlideMinSpeed) return;

        _glide = new GlideState(target.Handle, last.X, last.Y, vx, vy, now);
    }

    private (float X, float Y) ReleaseSpeed(long now)
    {
        var first = _trail.FindIndex(sample => now - sample.At <= GlideWindowMs);
        if (first < 0 || first == _trail.Count - 1) return (0, 0);

        var from = _trail[first];
        var to = _trail[^1];
        var span = to.At - from.At;

        if (span < GlideMinSpanMs) return (0, 0);

        var vx = (to.X - from.X) / (float)span;
        var vy = (to.Y - from.Y) / (float)span;
        var speed = MathF.Sqrt(vx * vx + vy * vy);

        if (speed <= GlideMaxSpeed) return (vx, vy);

        var scale = GlideMaxSpeed / speed;
        return (vx * scale, vy * scale);
    }

    /// <summary>
    /// 流れている板を 1 周ぶん進める。送るのは巻くときと同じホイールで、離した所へ送る（同じ巻く物に届く）。
    /// 止まるのは: 遅くなった・板が指せなくなった（隠れた・掴んだ・ダッシュボードが開いた・反対の手が持った）・巻かない板になった（Layout Mode）・板の上で押した（<see cref="Tick"/>）・繋ぎが切れた（<see cref="Stop"/>）。
    /// ⚠ 中身の端はこちらからは見えない。端に着いたら中身はそこで止まり（Chromium がそれ以上巻かない）、残りのホイールは緩みきるまで（長くて 1〜2 秒）空振りする。
    /// </summary>
    private void Glide(IReadOnlyList<VrLaserTarget> targets)
    {
        if (_glide is not { } glide) return;

        var board = targets.FirstOrDefault(target => target.Handle == glide.Handle);

        if (board is null || !board.Scrolls)
        {
            _glide = null;
            return;
        }

        var now = _now();
        var dt = Math.Min(now - glide.At, GlideMaxStepMs);

        if (dt <= 0) return;

        glide.At = now;

        // 端数を溜めて、まとまったら送る（ホイールは整数なので、遅い終わりの方が丸められて消える）。
        glide.CarryX += glide.SpeedX * dt * DragScrollGain;
        glide.CarryY += glide.SpeedY * dt * DragScrollGain;

        var wheelX = (int)MathF.Truncate(glide.CarryX);
        var wheelY = (int)MathF.Truncate(glide.CarryY);

        if (wheelX != 0 || wheelY != 0)
        {
            board.Sink(VrPointerKind.Pan, glide.X, glide.Y, VrPointerButton.Left, wheelX, wheelY);
            glide.CarryX -= wheelX;
            glide.CarryY -= wheelY;
        }

        var keep = MathF.Exp(-dt / GlideDecayMs);
        glide.SpeedX *= keep;
        glide.SpeedY *= keep;

        if (MathF.Sqrt(glide.SpeedX * glide.SpeedX + glide.SpeedY * glide.SpeedY) < GlideMinSpeed) _glide = null;
    }

    private sealed class GlideState(ulong handle, int x, int y, float speedX, float speedY, long at)
    {
        public ulong Handle { get; } = handle;

        public int X { get; } = x;

        public int Y { get; } = y;

        public float SpeedX { get; set; } = speedX;

        public float SpeedY { get; set; } = speedY;

        public long At { get; set; } = at;

        public float CarryX { get; set; }

        public float CarryY { get; set; }
    }

    /// <summary>指すのをやめる。押したままなら離す、<c>:hover</c> を外す、点を引っ込める。巻く板で引いたまま外れたら、押したことにはしない（まだ Down を送っていない）。流れは止めない（離してから手を下ろしても中身は流れる。止めるのは <see cref="Stop"/>）。</summary>
    private void Leave()
    {
        if (_hit is { } last && _target is { } target)
        {
            if (_pressed) target.Sink(VrPointerKind.Up, last.X, last.Y, VrPointerButton.Left, 0, 0);

            target.Sink(VrPointerKind.Leave, last.X, last.Y, VrPointerButton.Left, 0, 0);
        }

        _target = null;
        _hit = null;
        _pressed = false;
        _held = null;
        _dragged = null;
        _longPressed = false;
        _caught = false;
        _trail.Clear();
        _clickable = false;

        SetVisible(false);
    }

    /// <summary>点を置く。板が付いている機器に付ける — 狙いと板とで姿勢の取り方（予測の有無）が違うと、絶対の位置に置いた点は板の上で震える。空間に置いた板なら、点も空間に置く（板と同じ流儀）。</summary>
    private void Draw(VrLaserTarget target, VrHit at)
    {
        if (_dot == 0) return;

        var where = target.Offset.Then(VrMatrix.Translation(at.LocalX, at.LocalY, DotLift));

        if (target.Device is { } device) overlays.Attach(_dot, device, where);
        else overlays.PlaceAbsolute(_dot, where);

        SetVisible(true);
    }

    private void SetVisible(bool visible)
    {
        if (visible == _visible || _dot == 0) return;

        _visible = visible;
        overlays.SetShown(_dot, visible);
    }

    /// <summary>
    /// 自前の当たりと SteamVR の当たりを並べて残す。画素への直し方（縦の向き）は試験では確かめきれないので、実機で 1 回見れば分かるようにする。入ったときだけ、繋ぐたびに <see cref="CrossChecks"/> 回まで。
    /// ⚠ SteamVR の UV は縦横 1:1 の枠で来る（板にマウスの寸法を渡していないので、SteamVR は板を正方形とみなす）。縦は下が 0。読み方は <c>X ≈ u·640</c>、<c>Y ≈ 160 − (v − 0.5)·640</c>（640×320 の板のとき）。
    /// </summary>
    private void CrossCheck(VrLaserTarget target, VrMatrix aim, VrHit at)
    {
        if (_checks >= CrossChecks) return;

        _checks++;

        var theirs = overlays.Intersect(target.Handle, aim);

        logger.LogInformation(
            "Pointing at the {Name} at ({X}, {Y}) of {Width}x{Height}; SteamVR puts it at UV {Theirs}",
            target.Name, at.X, at.Y, target.PixelWidth, target.PixelHeight,
            theirs is { } uv ? $"({uv.U:0.000}, {uv.V:0.000})" : "nowhere");
    }
}
