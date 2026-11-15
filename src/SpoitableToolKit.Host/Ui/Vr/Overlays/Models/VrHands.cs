namespace SpoitableToolKit.Host.Ui.Vr.Overlays.Models;

/// <summary>両手の今。<b>1 周に 1 回だけ読む</b>（<see cref="IVrOverlays.ReadHands"/>）。</summary>
internal readonly record struct VrHands(VrHandInput Left, VrHandInput Right)
{
    public VrHandInput For(VrHand hand) => hand == VrHand.Left ? Left : Right;
}
