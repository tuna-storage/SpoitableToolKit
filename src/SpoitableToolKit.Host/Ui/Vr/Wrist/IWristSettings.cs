using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;

namespace SpoitableToolKit.Host.Ui.Vr.Wrist;

internal interface IWristSettings
{
    VrHand Hand { get; }

    /// <summary>掴んで動かした置き場。動かしたことが無ければ null（既定の置き場を使う）。</summary>
    VrMatrix? SavedOffset(VrHand hand);

    /// <summary>置き場を覚える。null で忘れる（既定へ戻す）。</summary>
    void SaveOffset(VrHand hand, VrMatrix? offset);

    /// <summary>板の横幅（メートル）。変えたことが無ければ null。両手で 1 つ。範囲は見ない（丸めるのは手首の側）。</summary>
    float? Width { get; }

    /// <summary>横幅を覚える。null で忘れる（既定へ戻す）。</summary>
    void SaveWidth(float? meters);

    /// <summary>Layout Mode のまま、どの板も指さず掴んでもいない時間がこの秒数続いたら固定に戻す。0 以下は戻さない。</summary>
    int LayoutLockSeconds { get; }

    /// <summary>
    /// 板の真ん中が視野の真ん中（頭の向き）からこれより近くなったら出す（度。<see cref="WristPlacement.Gaze"/>）。周ごとに読む（設定を変えたらその場で効く）。
    /// 範囲は見ない（丸めるのは <see cref="WristAngles.FromDegrees"/>）。
    /// </summary>
    int ShowWithinDegrees { get; }

    /// <summary>出ている板の真ん中が視野の真ん中からこれより離れたら隠す（度）。<see cref="ShowWithinDegrees"/> より小さければ、出す角度として扱う。</summary>
    int HideBeyondDegrees { get; }
}
