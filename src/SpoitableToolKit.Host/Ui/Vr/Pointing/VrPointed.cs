using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;

namespace SpoitableToolKit.Host.Ui.Vr.Pointing;

internal readonly record struct VrPointed(VrLaserStep Left, VrLaserStep Right)
{
    public VrLaserStep For(VrHand hand) => hand == VrHand.Left ? Left : Right;
}
