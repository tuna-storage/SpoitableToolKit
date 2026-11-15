using SpoitableToolKit.Host.Ui.Vr.Pointing;

namespace SpoitableToolKit.Host.Ui.Vr.Overlays.Models;

/// <summary>レーザーの 1 手。OpenVR の座標のまま持つ（画面の座標に直すのは <see cref="VrPointerMap"/>）。</summary>
internal sealed record VrPointer
{
    public required VrPointerKind Kind { get; init; }

    /// <summary>
    /// <c>SetOverlayMouseScale</c> に板の画素数を渡してあるので、単位は画素。
    /// ただし縦の向きは OpenVR の流儀のままなので、そのままでは使えない。
    /// </summary>
    public float X { get; init; }

    public float Y { get; init; }

    /// <summary>押されたボタン。<see cref="VrPointerKind.Move"/> と Scroll では見ない。</summary>
    public VrPointerButton Button { get; init; }

    public float ScrollX { get; init; }

    public float ScrollY { get; init; }
}
