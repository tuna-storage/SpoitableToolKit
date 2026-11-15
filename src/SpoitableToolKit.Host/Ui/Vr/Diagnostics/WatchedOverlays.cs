using SpoitableToolKit.Host.Ui.Vr.Overlays;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;

namespace SpoitableToolKit.Host.Ui.Vr.Diagnostics;

/// <summary>
/// OpenVR のそれぞれの呼び出しに名札を付けるだけの包み。周が止まったとき、<see cref="VrStallWatch"/> がどの呼び出しの中だったか言えるようにする。
/// 名札は OpenVR の関数名（SteamVR 側のログと突き合わせられる）。実物（<see cref="OpenVrOverlays"/>）に名札を書かないのは、あちらを薄いままにするため。
/// </summary>
internal sealed class WatchedOverlays(IVrOverlays inner, VrStallWatch watch) : IVrOverlays
{
    public bool IsAvailable() => watch.Inside("looking for the SteamVR processes", inner.IsAvailable);

    public bool Start(out string? error)
    {
        string? failed = null;
        var started = watch.Inside("OpenVR.Init", () => inner.Start(out failed));

        error = failed;
        return started;
    }

    public void Stop() => watch.Inside("OpenVR.Shutdown", inner.Stop);

    public ulong CreateDashboard(string key, string name, float widthInMeters) =>
        watch.Inside("CreateDashboardOverlay", () => inner.CreateDashboard(key, name, widthInMeters));

    public void UploadTexture(ulong handle, nint texture) =>
        watch.Inside("SetOverlayTexture", () => inner.UploadTexture(handle, texture));

    public bool QuitRequested() => watch.Inside("PollNextEvent", inner.QuitRequested);

    public void EnableInput(ulong handle, int width, int height) =>
        watch.Inside("SetOverlayInputMethod", () => inner.EnableInput(handle, width, height));

    public IReadOnlyList<VrPointer> PollPointer(ulong handle) =>
        watch.Inside("PollNextOverlayEvent", () => inner.PollPointer(handle));

    public void Destroy(ulong handle) => watch.Inside("DestroyOverlay", () => inner.Destroy(handle));

    public ulong CreateOverlay(string key, string name, float widthInMeters) =>
        watch.Inside("CreateOverlay", () => inner.CreateOverlay(key, name, widthInMeters));

    public void Attach(ulong handle, uint device, VrMatrix offset) =>
        watch.Inside("SetOverlayTransformTrackedDeviceRelative", () => inner.Attach(handle, device, offset));

    public void PlaceAbsolute(ulong handle, VrMatrix pose) =>
        watch.Inside("SetOverlayTransformAbsolute", () => inner.PlaceAbsolute(handle, pose));

    public void SetShown(ulong handle, bool shown) =>
        watch.Inside(shown ? "ShowOverlay" : "HideOverlay", () => inner.SetShown(handle, shown));

    public void SetAlpha(ulong handle, float alpha) =>
        watch.Inside("SetOverlayAlpha", () => inner.SetAlpha(handle, alpha));

    public void SetWidth(ulong handle, float widthInMeters) =>
        watch.Inside("SetOverlayWidthInMeters", () => inner.SetWidth(handle, widthInMeters));

    public uint? ControllerFor(VrHand hand) =>
        watch.Inside("GetTrackedDeviceIndexForControllerRole", () => inner.ControllerFor(hand));

    public IReadOnlyList<VrMatrix?> Poses() => watch.Inside("GetDeviceToAbsoluteTrackingPose", inner.Poses);

    public bool DashboardOpen() => watch.Inside("IsDashboardVisible", inner.DashboardOpen);

    public float? DisplayFrequency() => watch.Inside("GetFloatTrackedDeviceProperty", inner.DisplayFrequency);

    public VrHands ReadHands(bool captureAll) => watch.Inside("UpdateActionState", () => inner.ReadHands(captureAll));

    public void Pulse(VrHand hand, float seconds, float amplitude) =>
        watch.Inside("TriggerHapticVibrationAction", () => inner.Pulse(hand, seconds, amplitude));

    public ulong CreateSprite(string key, string name, VrSprite sprite, float widthInMeters) =>
        watch.Inside("SetOverlayRaw", () => inner.CreateSprite(key, name, sprite, widthInMeters));

    public (float U, float V)? Intersect(ulong handle, VrMatrix aim) =>
        watch.Inside("ComputeOverlayIntersection", () => inner.Intersect(handle, aim));
}
