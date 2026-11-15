using Microsoft.Extensions.Logging.Abstractions;
using SpoitableToolKit.Host.Ui.Vr;
using SpoitableToolKit.Host.Ui.Vr.Overlays;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Wrist;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Vr;

/// <summary>VR の面を「いつ繋ぐか・いつ諦めるか・いつ絵を上げるか」。OpenVR も CEF も境目の向こうなので、ここで見るのは判断だけ。出た絵が正しいかは目でしか確かめられない。</summary>
public class VrHostTests
{
    private static VrHost Host(
        FakeOverlays overlays, Func<VrFrame>? frames = null, List<string>? sent = null, WristHost? wrist = null) =>
        new(
            overlays,
            use => use((frames ?? (() => VrFrame.None))()),
            (kind, x, y, button, wheelX, wheelY) =>
                sent?.Add($"{kind} {x},{y} {button} wheel={wheelX},{wheelY}"),
            NullLogger.Instance,
            wrist is null ? null : new VrScene(overlays, NullLogger.Instance, [wrist]));

    private static WristHost Wrist(FakeOverlays overlays) =>
        new(overlays, new NoWristSettings(), use => use(VrFrame.None), (_, _, _, _, _, _) => { }, _ => { }, _ => { },
            NullLogger.Instance);

    /// <summary>
    /// <b>居ないのに繋ぎに行かない。</b> 繋ぎに行くと SteamVR が起動してしまう —
    /// ヘッドセットを使う気が無い人の機械で VR が立ち上がる。
    /// </summary>
    [Fact]
    public void NothingHappensWhileThereIsNoHeadset()
    {
        var overlays = new FakeOverlays { Available = false };

        Host(overlays).Tick();

        Assert.False(overlays.Started);
        Assert.Empty(overlays.Created);
    }

    [Fact]
    public void TheOverlayGoesUpOnceTheHeadsetIsThere()
    {
        var overlays = new FakeOverlays();
        var host = Host(overlays);

        host.Tick();

        Assert.True(host.Running);
        Assert.Equal(["dev.f2a.spoitable.dashboard"], overlays.Created);
    }

    /// <summary>繋ぎ直しに行かない。1 度立てたら、閉じられるまでそのまま。</summary>
    [Fact]
    public void ItIsNotBuiltTwice()
    {
        var overlays = new FakeOverlays();
        var host = Host(overlays);

        host.Tick();
        host.Tick();
        host.Tick();

        Assert.Single(overlays.Created);
    }

    /// <summary>
    /// 繋げなかった回は<b>諦めるだけ</b>。次の周でまた試すので、面も作らない。
    /// </summary>
    [Fact]
    public void AFailedConnectionLeavesNothingBehind()
    {
        var overlays = new FakeOverlays { StartFails = true };
        var host = Host(overlays);

        host.Tick();

        Assert.False(host.Running);
        Assert.Empty(overlays.Created);
    }

    /// <summary>
    /// 板が作れなかったら<b>繋いだものも戻す</b>。繋ぎっぱなしにすると、
    /// SteamVR からは「居るのに何も出さないアプリ」に見える。
    /// </summary>
    [Fact]
    public void TheConnectionIsGivenBackWhenTheOverlayCannotBeMade()
    {
        var overlays = new FakeOverlays { CreateFails = true };
        var host = Host(overlays);

        host.Tick();

        Assert.False(host.Running);
        Assert.True(overlays.Stopped);
    }

    [Fact]
    public void TheTextureReachesTheOverlay()
    {
        var overlays = new FakeOverlays();
        var host = Host(overlays, () => new VrFrame(4242, 1));

        host.Tick();   // 繋ぐ
        host.Tick();   // 差し替える

        Assert.Equal(1, host.Uploads);
        Assert.Equal(4242, overlays.LastTexture);
    }

    /// <summary>同じ絵なら差し替えない。描き替えられていないのに指し直すのは無駄なうえ、差し替えるたびに画面が乱れる。</summary>
    [Fact]
    public void TheSameFrameIsNotHandedOverTwice()
    {
        var overlays = new FakeOverlays();
        var host = Host(overlays, () => new VrFrame(4242, 7));

        host.Tick();
        host.Tick();
        host.Tick();
        host.Tick();

        Assert.Equal(1, overlays.Uploaded);
    }

    /// <summary>指す先が同じでも、描き替えられたら指し直す。CEF の絵はこちらの 1 枚へ写すのでテクスチャのポインタは二度と変わらず、ポインタだけで見ると 1 枚目で止まったままになる。</summary>
    [Fact]
    public void ARepaintOfTheSameTextureIsHandedOverAgain()
    {
        var overlays = new FakeOverlays();
        var frame = new VrFrame(4242, 1);
        var host = Host(overlays, () => frame);

        host.Tick();
        host.Tick();

        frame = new VrFrame(4242, 2);
        host.Tick();

        Assert.Equal(2, overlays.Uploaded);
        Assert.Equal(4242, overlays.LastTexture);
    }

    [Fact]
    public void ANewTextureIsHandedOver()
    {
        var overlays = new FakeOverlays();
        nint texture = 1;
        var host = Host(overlays, () => new VrFrame(texture, 1));

        host.Tick();
        host.Tick();

        texture = 2;
        host.Tick();

        Assert.Equal(2, overlays.Uploaded);
        Assert.Equal(2, overlays.LastTexture);
    }

    /// <summary>借りている間に渡す。返してから渡すと、その隙間に向こうがテクスチャを捨てうる（捨てられた先を <c>SetOverlayTexture</c> に渡すと 0xC0000005 で落ちる）。</summary>
    [Fact]
    public void TheTextureIsHandedOverWhileItIsLent()
    {
        List<string> order = [];
        var overlays = new FakeOverlays { OnUpload = () => order.Add("upload") };
        var host = new VrHost(
            overlays,
            use =>
            {
                order.Add("lent");
                use(new VrFrame(4242, 1));
                order.Add("returned");
            },
            (_, _, _, _, _, _) => { },
            NullLogger.Instance);

        host.Tick();   // 繋ぐ（絵は見に行かない）
        host.Tick();

        Assert.Equal(["lent", "upload", "returned"], order);
    }

    [Fact]
    public void NothingIsHandedOverBeforeTheFirstPaint()
    {
        var overlays = new FakeOverlays();
        var host = Host(overlays);

        host.Tick();
        host.Tick();

        Assert.Equal(0, host.Uploads);
    }

    /// <summary>作っただけの板は渡さない。テクスチャは有るが 1 枚も写していない間があり、中身が決まっていないので渡すとゴミが板に出る。</summary>
    [Fact]
    public void ATextureThatWasNeverPaintedIntoIsNotHandedOver()
    {
        var overlays = new FakeOverlays();
        var host = Host(overlays, () => new VrFrame(4242, 0));

        host.Tick();
        host.Tick();

        Assert.Equal(0, host.Uploads);
    }

    [Fact]
    public void ATextureBeforeTheOverlayExistsIsDropped()
    {
        var overlays = new FakeOverlays { Available = false };
        var host = Host(overlays, () => new VrFrame(4242, 1));

        host.Tick();

        Assert.Equal(0, host.Uploads);
    }

    /// <summary>SteamVR が閉じると言ったら畳む。付き合わないと「アプリを待っています」で止まる。</summary>
    [Fact]
    public void TheOverlayIsPutAwayWhenSteamVrLeaves()
    {
        var overlays = new FakeOverlays();
        var host = Host(overlays, () => new VrFrame(4242, 1));

        host.Tick();

        overlays.Quitting = true;
        host.Tick();

        Assert.False(host.Running);
        Assert.True(overlays.Stopped);
        Assert.Equal(1, overlays.Destroyed);
    }

    /// <summary>閉じる周では絵を差し替えない（畳んだ先へ渡すことになる）。</summary>
    [Fact]
    public void NoFrameIsSentOnTheWayOut()
    {
        var overlays = new FakeOverlays { Quitting = false };
        var host = Host(overlays, () => new VrFrame(4242, 1));

        host.Tick();
        overlays.Quitting = true;
        host.Tick();

        Assert.Equal(0, host.Uploads);
    }

    /// <summary>閉じると言った SteamVR が居なくなるまで、繋ぎに行かない。閉じる途中に <c>OpenVR.Init</c> を呼ぶと、終わりかけの SteamVR を起動し直す。</summary>
    [Fact]
    public void SteamVrIsNotWokenUpWhileItIsLeaving()
    {
        var overlays = new FakeOverlays();
        var host = Host(overlays);

        host.Tick();
        overlays.Quitting = true;
        host.Tick();
        overlays.Quitting = false;

        // まだ居る（閉じる途中）。
        host.Tick();
        host.Tick();
        host.Tick();

        Assert.False(host.Running);
        Assert.Equal(1, overlays.Starts);
    }

    [Fact]
    public void ItConnectsAgainOnceSteamVrHasGoneAndComeBack()
    {
        var overlays = new FakeOverlays();
        var host = Host(overlays);

        host.Tick();
        overlays.Quitting = true;
        host.Tick();
        overlays.Quitting = false;

        overlays.Available = false;
        host.Tick();

        overlays.Available = true;
        host.Tick();

        Assert.True(host.Running);
        Assert.Equal(2, overlays.Starts);
    }

    /// <summary>閉じると言ったきり居座り続けても、いつかは繋ぎ直す（待ち続けると二度と繋がらない）。</summary>
    [Fact]
    public void ItConnectsAgainEventuallyIfSteamVrNeverLeaves()
    {
        var overlays = new FakeOverlays();
        var host = Host(overlays);

        host.Tick();
        overlays.Quitting = true;
        host.Tick();
        overlays.Quitting = false;

        for (var i = 0; i < 15; i++) host.Tick();

        Assert.True(host.Running);
        Assert.Equal(2, overlays.Starts);
    }

    /// <summary>
    /// <b>待つのは閉じると言われたあとだけ。</b> 繋げなかった（まだ準備中の）SteamVR へは、
    /// 次の周でそのまま繋ぎに行く。
    /// </summary>
    [Fact]
    public void AFailedConnectionIsRetriedOnTheNextTick()
    {
        var overlays = new FakeOverlays { StartFails = true };
        var host = Host(overlays);

        host.Tick();
        overlays.StartFails = false;
        host.Tick();

        Assert.True(host.Running);
    }

    /// <summary>終了の道と Quit の道の両方から来るので、2 回呼ばれても壊れない。</summary>
    [Fact]
    public void PuttingItAwayTwiceIsHarmless()
    {
        var overlays = new FakeOverlays();
        var host = Host(overlays);

        host.Tick();
        host.Stop();
        host.Stop();

        Assert.Equal(1, overlays.Destroyed);
    }

    [Fact]
    public void StoppingBeforeStartingTouchesNothing()
    {
        var overlays = new FakeOverlays();

        Host(overlays).Stop();

        Assert.False(overlays.Stopped);
        Assert.Equal(0, overlays.Destroyed);
    }

    /// <summary>板が出来てから触れるようにする。先に呼ぶと宛先が無い。</summary>
    [Fact]
    public void TheOverlayBecomesTouchableOnceItExists()
    {
        var overlays = new FakeOverlays();

        Host(overlays).Tick();

        Assert.True(overlays.InputEnabled);
    }

    /// <summary>縦は反る（OpenVR の板は左下が原点で、ブラウザは左上）。</summary>
    [Fact]
    public void ThePointerLandsWhereItWasAimed()
    {
        var overlays = new FakeOverlays();
        List<string> sent = [];
        var host = Host(overlays, sent: sent);

        host.Tick();
        overlays.Pointers.Add(new VrPointer { Kind = VrPointerKind.Down, X = 100, Y = 700 });
        host.Tick();

        // 縦 720 の板の y=700（上の方）→ ブラウザの 20（上の方）。
        Assert.Equal(["Down 100,20 Left wheel=0,0"], sent);
    }

    /// <summary>板の外を指したぶんは端に寄せる。負の座標をブラウザへ渡さない。</summary>
    [Fact]
    public void AimingOffTheBoardIsPulledBackToTheEdge()
    {
        var overlays = new FakeOverlays();
        List<string> sent = [];
        var host = Host(overlays, sent: sent);

        host.Tick();
        overlays.Pointers.Add(new VrPointer { Kind = VrPointerKind.Move, X = -50, Y = 9000 });
        host.Tick();

        Assert.Equal(["Move 0,0 Left wheel=0,0"], sent);
    }

    /// <summary>
    /// スクロールの報せに座標は入っていないので、<b>最後に指していたところ</b>で巻く。
    /// </summary>
    [Fact]
    public void ScrollingHappensWhereThePointerLastWas()
    {
        var overlays = new FakeOverlays();
        List<string> sent = [];
        var host = Host(overlays, sent: sent);

        host.Tick();
        overlays.Pointers.Add(new VrPointer { Kind = VrPointerKind.Move, X = 300, Y = 620 });
        overlays.Pointers.Add(new VrPointer { Kind = VrPointerKind.Scroll, ScrollY = 1 });
        host.Tick();

        // 上へ 1 目盛り → 上へ 120。反さない。
        Assert.Equal(["Move 300,100 Left wheel=0,0", "Scroll 300,100 Left wheel=0,120"], sent);
    }

    /// <summary>右で押したら右として届く（自前のメニューがこれで出る）。</summary>
    [Fact]
    public void TheRightButtonArrivesAsTheRightButton()
    {
        var overlays = new FakeOverlays();
        List<string> sent = [];
        var host = Host(overlays, sent: sent);

        host.Tick();
        overlays.Pointers.Add(new VrPointer { Kind = VrPointerKind.Down, X = 5, Y = 715, Button = VrPointerButton.Right });
        host.Tick();

        Assert.Equal(["Down 5,5 Right wheel=0,0"], sent);
    }

    /// <summary>
    /// <b>配るのが先で、絵は後。</b> 逆にすると、押した結果が 1 枚ぶん遅れて見える。
    /// </summary>
    [Fact]
    public void ThePointerIsDeliveredBeforeTheNextFrame()
    {
        var overlays = new FakeOverlays();
        List<string> order = [];
        var host = new VrHost(
            overlays,
            use =>
            {
                order.Add("frame");
                use(VrFrame.None);
            },
            (kind, x, y, button, wheelX, wheelY) => order.Add("pointer"),
            NullLogger.Instance);

        host.Tick();
        overlays.Pointers.Add(new VrPointer { Kind = VrPointerKind.Move });
        host.Tick();

        // 1 周目は繋ぐだけで戻るので、絵を見に行かない。
        Assert.Equal(["pointer", "frame"], order);
    }

    [Fact]
    public void NoPointerIsDeliveredAfterQuitting()
    {
        var overlays = new FakeOverlays();
        List<string> sent = [];
        var host = Host(overlays, sent: sent);

        host.Tick();
        overlays.Quitting = true;
        overlays.Pointers.Add(new VrPointer { Kind = VrPointerKind.Down });
        host.Tick();

        Assert.Empty(sent);
    }

    /// <summary>繋いだら、周の速さをヘッドセットのリフレッシュレートに合わせる。板の絵（CEF）も点の動きもこの速さになる。</summary>
    [Fact]
    public void TheLoopFollowsTheHeadsetRefreshRate()
    {
        var overlays = new FakeOverlays { Frequency = 120f };
        var host = Host(overlays);
        Assert.Equal(VrHost.FallbackFrameRate, host.FrameRate);

        host.Tick();

        Assert.True(host.Running);
        Assert.Equal(120f, host.FrameRate);
    }

    /// <summary>
    /// 読めない・壊れた値は既定（60）、<b>速すぎ・遅すぎは 30〜240 に収める</b>
    /// （0 で割って周が止まったり、UI のスレッドを周で埋めたりしない）。
    /// </summary>
    [Theory]
    [InlineData(90f, 90f)]
    [InlineData(120f, 120f)]
    [InlineData(null, 60f)]
    [InlineData(0f, 60f)]
    [InlineData(-5f, 60f)]
    [InlineData(float.NaN, 60f)]
    [InlineData(float.PositiveInfinity, 60f)]
    [InlineData(10f, 30f)]
    [InlineData(1000f, 240f)]
    public void TheRateIsKeptInRange(float? reported, float expected) =>
        Assert.Equal(expected, VrHost.FrameRateFor(reported));

    /// <summary>手首の板はダッシュボードと<b>同じ繋ぎで</b>作る（繋ぐのは 1 回）。</summary>
    [Fact]
    public void TheWristGoesUpWithTheDashboard()
    {
        var overlays = new FakeOverlays();
        var wrist = Wrist(overlays);
        var host = Host(overlays, wrist: wrist);

        host.Tick();

        Assert.True(wrist.Running);
        Assert.Equal(["dev.f2a.spoitable.dashboard", WristHost.Key], overlays.Created);
        Assert.Equal(1, overlays.Starts);
    }

    /// <summary>手首が作れなくてもダッシュボードは諦めない（手首は付け足し）。</summary>
    [Fact]
    public void TheDashboardStaysUpWhenTheWristCannotBeMade()
    {
        var overlays = new FakeOverlays { WristFails = true };
        var wrist = Wrist(overlays);
        var host = Host(overlays, wrist: wrist);

        host.Tick();

        Assert.True(host.Running);
        Assert.False(wrist.Running);
        Assert.False(overlays.Stopped);
    }

    /// <summary>
    /// 閉じるときは<b>手首の板を消してから繋ぎを返す</b>。返したあとに消しに行くと、
    /// 宛先の無い呼び出しになる。
    /// </summary>
    [Fact]
    public void TheWristIsPutAwayBeforeTheConnectionIsGivenBack()
    {
        var overlays = new FakeOverlays();
        var wrist = Wrist(overlays);
        var host = Host(overlays, wrist: wrist);

        host.Tick();
        overlays.Quitting = true;
        host.Tick();

        Assert.False(wrist.Running);
        Assert.Equal(2, overlays.Destroyed);

        // 自前のポインタの点は手ごとに 1 つ（VrPointing）。
        Assert.Equal([VrSprite.Dot, VrSprite.Dot], overlays.Sprites);
        Assert.Equal(2, overlays.SpritesDestroyed);
        Assert.True(overlays.DestroyedBeforeStop);
    }

    /// <summary>
    /// 繋がっている間は<b>毎周、手首も回す</b>（向けたかどうかを毎周見るので）。
    /// 繋いだ周はまだ回さない — 板を作っただけで、まだ付け先を探していない。
    /// </summary>
    [Fact]
    public void TheWristIsTickedEveryRound()
    {
        var overlays = new FakeOverlays();
        var host = Host(overlays, wrist: Wrist(overlays));

        host.Tick();
        host.Tick();
        host.Tick();

        Assert.Equal(2, overlays.PoseReads);
    }

    private sealed class NoWristSettings : IWristSettings
    {
        public VrHand Hand => VrHand.Left;

        public VrMatrix? SavedOffset(VrHand hand) => null;

        public void SaveOffset(VrHand hand, VrMatrix? offset)
        {
        }

        public float? Width => null;

        public int LayoutLockSeconds => 10;

        public int ShowWithinDegrees => WristPlacement.DefaultShowWithinDegrees;

        public int HideBeyondDegrees => WristPlacement.DefaultHideBeyondDegrees;

        public void SaveWidth(float? meters)
        {
        }
    }

    private sealed class FakeOverlays : IVrOverlays
    {
        public bool Available { get; set; } = true;

        public bool StartFails { get; set; }

        public bool CreateFails { get; init; }

        public bool Quitting { get; set; }

        public bool Started { get; private set; }

        /// <summary>繋ぎに行った回数（失敗した回も数える）。</summary>
        public int Starts { get; private set; }

        public bool Stopped { get; private set; }

        public int Destroyed { get; private set; }

        public List<string> Created { get; } = [];

        public nint LastTexture { get; private set; }

        public int Uploaded { get; private set; }

        /// <summary>渡された瞬間に呼ぶ。順番を見る試験のため。</summary>
        public Action? OnUpload { get; init; }

        public bool IsAvailable() => Available;

        public bool Start(out string? error)
        {
            Starts++;
            error = StartFails ? "nope" : null;
            Started = !StartFails;
            return !StartFails;
        }

        public void Stop()
        {
            Stopped = true;
            DestroyedBeforeStop = Destroyed == Created.Count && SpritesDestroyed == Sprites.Count;
        }

        /// <summary>繋ぎを返した時点で、作った板が全部消えていたか（<b>自前のレーザーの線と点も</b>）。</summary>
        public bool DestroyedBeforeStop { get; private set; }

        public bool WristFails { get; init; }

        public ulong CreateDashboard(string key, string name, float widthInMeters)
        {
            if (CreateFails) return 0;

            Created.Add(key);
            return 42;
        }

        public void UploadTexture(ulong handle, nint texture)
        {
            OnUpload?.Invoke();
            LastTexture = texture;
            Uploaded++;
        }

        public bool InputEnabled { get; private set; }

        public List<VrPointer> Pointers { get; } = [];

        public void EnableInput(ulong handle, int width, int height) => InputEnabled = true;

        public IReadOnlyList<VrPointer> PollPointer(ulong handle)
        {
            var waiting = Pointers.ToArray();
            Pointers.Clear();
            return waiting;
        }

        public bool QuitRequested() => Quitting;

        public void Destroy(ulong handle)
        {
            if (handle >= FirstSprite) SpritesDestroyed++;
            else Destroyed++;
        }

        /// <summary>自前のレーザーの線と点。<b>板とは別に数える</b>（handle は <see cref="FirstSprite"/> から）。</summary>
        public List<VrSprite> Sprites { get; } = [];

        public int SpritesDestroyed { get; private set; }

        private const ulong FirstSprite = 100;

        public ulong CreateSprite(string key, string name, VrSprite sprite, float widthInMeters)
        {
            Sprites.Add(sprite);
            return FirstSprite + (ulong)Sprites.Count;
        }

        public (float U, float V)? Intersect(ulong handle, VrMatrix aim) => null;

        public bool DashboardOpen() => false;

        public float? Frequency { get; set; } = 90f;

        public float? DisplayFrequency() => Frequency;

        public VrHands ReadHands(bool captureAll) => default;

        public void Pulse(VrHand hand, float seconds, float amplitude)
        {
        }

        public ulong CreateOverlay(string key, string name, float widthInMeters)
        {
            if (WristFails) return 0;

            Created.Add(key);
            return 43;
        }

        public void Attach(ulong handle, uint device, VrMatrix offset)
        {
        }

        public void PlaceAbsolute(ulong handle, VrMatrix pose)
        {
        }

        public void SetShown(ulong handle, bool shown)
        {
        }

        public void SetAlpha(ulong handle, float alpha)
        {
        }

        public void SetWidth(ulong handle, float widthInMeters)
        {
        }

        // コントローラーは繋がっていない（null ドライバと同じ）。手首は出ない。
        public uint? ControllerFor(VrHand hand) => null;

        /// <summary>姿勢を読みに来た回数。手首が回ったことの目印。</summary>
        public int PoseReads { get; private set; }

        public IReadOnlyList<VrMatrix?> Poses()
        {
            PoseReads++;
            return [];
        }

    }
}
