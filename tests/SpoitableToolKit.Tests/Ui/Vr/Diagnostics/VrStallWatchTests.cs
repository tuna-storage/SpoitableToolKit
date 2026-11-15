using SpoitableToolKit.Host.Ui.Vr.Diagnostics;
using SpoitableToolKit.Host.Ui.Vr.Overlays;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using Xunit;

namespace SpoitableToolKit.Tests.Ui.Vr.Diagnostics;

/// <summary>VR の周が回らなくなったら、どこで止まっていたかを残す見張り。時計は手で進める（時間を待たずに確かめる）。</summary>
public class VrStallWatchTests
{
    private long _now = 1_000;

    private VrStallWatch Watch() => new(() => _now, limitMs: 3_000);

    [Fact]
    public void ALoopThatKeepsComingRoundIsNotReported()
    {
        var watch = Watch();

        _now += 2_000;
        watch.Beat();
        _now += 2_000;

        Assert.Null(watch.Check());
    }

    /// <summary>止まったら、どこの中だったかを添えて報せる。</summary>
    [Fact]
    public void AStallSaysWhereTheLoopWas()
    {
        var watch = Watch();

        watch.Enter("SetOverlayTexture");
        _now += 3_500;

        var stall = watch.Check();

        Assert.NotNull(stall);
        Assert.True(stall.Value.Stuck);
        Assert.Equal("SetOverlayTexture", stall.Value.Where);
        Assert.Equal(TimeSpan.FromMilliseconds(3_500), stall.Value.For);
    }

    /// <summary>報せるのは止まった最初の 1 回だけ。毎回出すと、その行がアプリログの欄へ流れて再描画を起こす。</summary>
    [Fact]
    public void AStallIsReportedOnce()
    {
        var watch = Watch();

        _now += 3_500;
        watch.Check();
        _now += 1_000;

        Assert.Null(watch.Check());
    }

    /// <summary>止まる手前では報せない。<b>境目は上限ちょうど</b>。</summary>
    [Fact]
    public void JustUnderTheLimitIsNotAStall()
    {
        var watch = Watch();

        _now += 2_999;

        Assert.Null(watch.Check());
    }

    [Fact]
    public void TheLimitItselfIsAStall()
    {
        var watch = Watch();

        _now += 3_000;

        Assert.NotNull(watch.Check());
    }

    [Fact]
    public void ComingRoundAgainIsReportedWithHowLongItTook()
    {
        var watch = Watch();

        _now += 3_500;
        watch.Check();

        _now += 6_500;
        watch.Beat();

        var back = watch.Check();

        Assert.NotNull(back);
        Assert.False(back.Value.Stuck);
        Assert.Equal(TimeSpan.FromMilliseconds(10_000), back.Value.For);
    }

    [Fact]
    public void ASecondStallIsReportedToo()
    {
        var watch = Watch();

        _now += 3_500;
        watch.Check();
        watch.Beat();
        watch.Check();

        _now += 3_500;

        Assert.True(watch.Check()?.Stuck);
    }

    /// <summary>呼び出しを抜けたら名札を戻す。戻さないと、とっくに抜けた呼び出しで止まっていたことにされる。</summary>
    [Fact]
    public void TheNameIsTakenOffWhenTheCallReturns()
    {
        var watch = Watch();
        string? during = null;

        watch.Inside("OpenVR.Init", () => during = watch.Where);

        Assert.Equal("OpenVR.Init", during);
        Assert.Equal(VrStallWatch.InsideTick, watch.Where);
    }

    [Fact]
    public void TheNameIsTakenOffEvenWhenTheCallThrows()
    {
        var watch = Watch();

        Assert.Throws<InvalidOperationException>(() =>
            watch.Inside("OpenVR.Init", () => throw new InvalidOperationException()));

        Assert.Equal(VrStallWatch.InsideTick, watch.Where);
    }

    /// <summary>1 周終えたら「周と周の間」に戻す。このまま止まっていたら、VR の外で UI のスレッドが塞がっていると読める。</summary>
    [Fact]
    public void ABeatMeansTheLoopIsBetweenTicks()
    {
        var watch = Watch();

        watch.Enter("PollNextEvent");
        watch.Beat();

        Assert.Equal(VrStallWatch.BetweenTicks, watch.Where);
    }

    /// <summary>OpenVR の呼び出しの中にいる間は、その関数名が名札になる（SteamVR 側のログと突き合わせて探せる）。</summary>
    [Fact]
    public void TheOpenVrCallIsNamedWhileItRuns()
    {
        var watch = Watch();
        var inner = new SpyOverlays(watch);
        var overlays = new WatchedOverlays(inner, watch);

        overlays.IsAvailable();
        overlays.Start(out _);
        overlays.CreateDashboard("k", "n", 1);
        overlays.EnableInput(1, 2, 3);
        overlays.QuitRequested();
        overlays.PollPointer(1);
        overlays.UploadTexture(1, 2);
        overlays.CreateOverlay("k", "n", 1);
        overlays.Attach(1, 2, VrMatrix.Identity);
        overlays.PlaceAbsolute(1, VrMatrix.Identity);
        overlays.SetShown(1, true);
        overlays.SetShown(1, false);
        overlays.SetWidth(1, 0.2f);
        overlays.ControllerFor(VrHand.Left);
        overlays.Poses();
        overlays.DashboardOpen();
        overlays.DisplayFrequency();
        overlays.ReadHands(false);
        overlays.Pulse(VrHand.Left, 0.01f, 0.5f);
        overlays.CreateSprite("k", "n", VrSprite.Dot, 0.01f);
        overlays.Intersect(1, VrMatrix.Identity);
        overlays.Destroy(1);
        overlays.Stop();

        // 呼び出しの数と名札の数が揃っていることも見る（包みの試験が一部しか見ていないのを防ぐ）。
        Assert.Equal(
            [
                "looking for the SteamVR processes", "OpenVR.Init", "CreateDashboardOverlay",
                "SetOverlayInputMethod", "PollNextEvent", "PollNextOverlayEvent", "SetOverlayTexture",
                "CreateOverlay", "SetOverlayTransformTrackedDeviceRelative", "SetOverlayTransformAbsolute", "ShowOverlay", "HideOverlay",
                "SetOverlayWidthInMeters", "GetTrackedDeviceIndexForControllerRole", "GetDeviceToAbsoluteTrackingPose",
                "IsDashboardVisible", "GetFloatTrackedDeviceProperty", "UpdateActionState", "TriggerHapticVibrationAction",
                "SetOverlayRaw", "ComputeOverlayIntersection", "DestroyOverlay", "OpenVR.Shutdown",
            ],
            inner.SeenWhere);
    }

    /// <summary>包みが中身をそのまま渡すこと（手首の置き場は向きと位置そのもので、化けると板が変な所へ飛ぶ）。</summary>
    [Fact]
    public void TheWrapperHandsOverWhatItIsGiven()
    {
        var watch = Watch();
        var inner = new SpyOverlays(watch);
        var overlays = new WatchedOverlays(inner, watch);
        var offset = VrMatrix.Translation(1, 2, 3);

        overlays.Attach(7, 3, offset);
        overlays.PlaceAbsolute(8, offset);
        overlays.SetShown(7, true);
        overlays.SetWidth(7, 0.24f);

        Assert.Equal((7UL, 3U, offset), inner.LastAttach);
        Assert.Equal((8UL, offset), inner.LastPlaced);
        Assert.Equal((7UL, true), inner.LastShown);
        Assert.Equal((7UL, 0.24f), inner.LastWidth);
        Assert.Equal(5U, overlays.ControllerFor(VrHand.Right));
        Assert.Equal(VrMatrix.Translation(4, 5, 6), overlays.Poses()[0]);
        Assert.Equal(11UL, overlays.CreateOverlay("k", "n", 1));
    }

    /// <summary>包んでも中身はそのまま返る（失敗の理由も）。</summary>
    [Fact]
    public void TheWrapperChangesNothingItPassesThrough()
    {
        var watch = Watch();
        var overlays = new WatchedOverlays(new SpyOverlays(watch) { StartError = "Init_HmdNotFound" }, watch);

        var started = overlays.Start(out var error);

        Assert.False(started);
        Assert.Equal("Init_HmdNotFound", error);
    }

    /// <summary>呼ばれた瞬間の名札を控えるだけの OpenVR。</summary>
    private sealed class SpyOverlays(VrStallWatch watch) : IVrOverlays
    {
        public List<string> SeenWhere { get; } = [];

        public string? StartError { get; init; }

        public bool IsAvailable() => See(true);

        public bool Start(out string? error)
        {
            error = StartError;
            return See(StartError is null);
        }

        public void Stop() => See(0);

        public ulong CreateDashboard(string key, string name, float widthInMeters) => See(1UL);

        public void UploadTexture(ulong handle, nint texture) => See(0);

        public bool QuitRequested() => See(false);

        public void EnableInput(ulong handle, int width, int height) => See(0);

        public IReadOnlyList<VrPointer> PollPointer(ulong handle) => See(Array.Empty<VrPointer>());

        public void Destroy(ulong handle) => See(0);

        public (ulong, uint, VrMatrix) LastAttach { get; private set; }

        public (ulong, bool) LastShown { get; private set; }

        public ulong CreateOverlay(string key, string name, float widthInMeters) => See(11UL);

        public void Attach(ulong handle, uint device, VrMatrix offset) => LastAttach = See((handle, device, offset));

        public (ulong, VrMatrix) LastPlaced { get; private set; }

        public void PlaceAbsolute(ulong handle, VrMatrix pose) => LastPlaced = See((handle, pose));

        public void SetShown(ulong handle, bool shown) => LastShown = See((handle, shown));

        public (ulong, float) LastAlpha { get; private set; }

        public void SetAlpha(ulong handle, float alpha) => LastAlpha = See((handle, alpha));

        public (ulong, float) LastWidth { get; private set; }

        public void SetWidth(ulong handle, float widthInMeters) => LastWidth = See((handle, widthInMeters));

        public bool DashboardOpen() => See(false);

        public float? DisplayFrequency() => See<float?>(90f);

        public VrHands ReadHands(bool captureAll) => See(default(VrHands));

        public void Pulse(VrHand hand, float seconds, float amplitude) => See(0);

        public ulong CreateSprite(string key, string name, VrSprite sprite, float widthInMeters) => See(12UL);

        public (float U, float V)? Intersect(ulong handle, VrMatrix aim) => See<(float U, float V)?>(null);

        public uint? ControllerFor(VrHand hand) => See<uint?>(hand == VrHand.Right ? 5U : 4U);

        public IReadOnlyList<VrMatrix?> Poses() => See<IReadOnlyList<VrMatrix?>>([VrMatrix.Translation(4, 5, 6)]);

        private T See<T>(T result)
        {
            SeenWhere.Add(watch.Where);
            return result;
        }
    }
}
