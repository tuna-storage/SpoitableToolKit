using SpoitableToolKit.Host.Config;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;

namespace SpoitableToolKit.Host.Ui.Vr.Wrist;

internal sealed class ConfiguredWrist(HostConfig config, ModuleConfigStore configs) : IWristSettings
{
    public VrHand Hand => config.WristHand == WristHand.Right ? VrHand.Right : VrHand.Left;

    public VrMatrix? SavedOffset(VrHand hand) =>
        config.WristOffsets.TryGetValue(Key(hand), out var values) ? VrMatrix.FromArray(values) : null;

    /// <summary>辞書は差し替える（中身を書き換えない）。保存は別のスレッドからも走るので、書き出している最中の辞書を触ると落ちうる。</summary>
    public void SaveOffset(VrHand hand, VrMatrix? offset)
    {
        var next = new Dictionary<string, float[]>(config.WristOffsets);

        if (offset is { } value) next[Key(hand)] = value.ToArray();
        else next.Remove(Key(hand));

        config.WristOffsets = next;
        configs.Save(HostConfig.Id);
    }

    public float? Width => config.WristWidth;

    public int LayoutLockSeconds => config.WristLayoutLockSeconds;

    public int ShowWithinDegrees => config.WristShowWithinDegrees;

    public int HideBeyondDegrees => config.WristHideBeyondDegrees;

    public void SaveWidth(float? meters)
    {
        config.WristWidth = meters;
        configs.Save(HostConfig.Id);
    }

    private static string Key(VrHand hand) => hand == VrHand.Left ? "left" : "right";
}
