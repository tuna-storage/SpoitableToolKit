using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;

namespace SpoitableToolKit.Host.Ui.Vr.Pointing;

/// <summary>
/// 指される側の板。Pose は立った姿勢の原点から見た板の中心の位置と向き（剛体）。
/// 縦の長さは横幅と画素数から割り出さずに渡す（texel aspect を変えた板で合わなくなるため）。
/// </summary>
internal readonly record struct VrBoard(VrMatrix Pose, float Width, float Height, int PixelWidth, int PixelHeight);
