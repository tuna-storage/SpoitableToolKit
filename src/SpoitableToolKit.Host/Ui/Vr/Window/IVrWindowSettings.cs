using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;

namespace SpoitableToolKit.Host.Ui.Vr.Window;

internal interface IVrWindowSettings
{
    /// <summary>手首に付けるか、空間に置くか。<b>開いたときに読む</b>（開いている間に変わっても、次に開いたときから効く）。</summary>
    VrWindowSpot Spot { get; }

    /// <summary>空間に置く窓の横幅（メートル）。<b>変えたことが無ければ null</b>。範囲は見ない（丸めるのは窓の側）。</summary>
    float? Width { get; }

    void SaveWidth(float? meters);

    /// <summary>手首に付ける窓の横幅（メートル）。<b>空間に置くときとは別に覚える</b>（手首の上は顔に近いので、ちょうどよい大きさが違う）。</summary>
    float? WristWidth { get; }

    void SaveWristWidth(float? meters);

    /// <summary>手首に付ける窓の置き場（<b>手首の板から見た</b>位置と向き）。<b>手ごとに覚える</b>。動かしたことが無ければ null（板の真上）。</summary>
    VrMatrix? WristOffset(VrHand hand);

    void SaveWristOffset(VrHand hand, VrMatrix? offset);
}
