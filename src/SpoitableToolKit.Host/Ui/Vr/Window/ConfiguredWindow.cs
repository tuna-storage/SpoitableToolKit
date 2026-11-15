using SpoitableToolKit.Host.Config;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using SpoitableToolKit.Host.Ui.Vr.Wrist;

namespace SpoitableToolKit.Host.Ui.Vr.Window;

internal sealed class ConfiguredWindow(HostConfig config, ModuleConfigStore configs) : IVrWindowSettings
{
    public VrWindowSpot Spot => config.VrWindowSpot;

    public float? Width => config.VrWindowWidth;

    public void SaveWidth(float? meters)
    {
        config.VrWindowWidth = meters;
        configs.Save(HostConfig.Id);
    }

    public float? WristWidth => config.VrWindowWristWidth;

    public void SaveWristWidth(float? meters)
    {
        config.VrWindowWristWidth = meters;
        configs.Save(HostConfig.Id);
    }

    public VrMatrix? WristOffset(VrHand hand) =>
        config.VrWindowWristOffsets.TryGetValue(Key(hand), out var values) ? VrMatrix.FromArray(values) : null;

    /// <summary>辞書は差し替える（中身を書き換えない）。保存は別のスレッドからも走るので、書き出している最中の辞書を触ると落ちうる。</summary>
    public void SaveWristOffset(VrHand hand, VrMatrix? offset)
    {
        var next = new Dictionary<string, float[]>(config.VrWindowWristOffsets);

        if (offset is { } value) next[Key(hand)] = value.ToArray();
        else next.Remove(Key(hand));

        config.VrWindowWristOffsets = next;
        configs.Save(HostConfig.Id);
    }

    private static string Key(VrHand hand) => hand == VrHand.Left ? "left" : "right";
}
