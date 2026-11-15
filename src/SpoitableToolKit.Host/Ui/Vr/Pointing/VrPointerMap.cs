namespace SpoitableToolKit.Host.Ui.Vr.Pointing;

/// <summary>
/// レーザーの座標を板の中の画素へ直す。横はそのまま（<c>SetOverlayMouseScale</c> に板の画素数を渡してあるので、OpenVR は画素で寄こす）。
/// 縦は反す（OpenVR の板は左下が原点で、ブラウザは左上）。
/// 板からはみ出した座標は挟み込む（ブラウザが負の座標を受けないように）。
/// </summary>
internal static class VrPointerMap
{
    public static (int X, int Y) ToBrowser(float x, float y, int width, int height) =>
        (Clamp(x, width), Clamp(height - y, height));

    /// <summary>
    /// OpenVR は何目盛り回したかで寄こすので、1 目盛りぶんの画素を掛ける。
    /// 符号は反さない: CEF が受ける縦の量は Windows のホイールと同じ流儀で、正が奥へ回した ＝ 上へ送る。OpenVR の目盛りも正が上。
    /// 横はまだ実測していない。逆なら横だけ符号を分けること。
    /// </summary>
    public static (int X, int Y) ToWheel(float scrollX, float scrollY) =>
        ((int)(scrollX * WheelStep), (int)(scrollY * WheelStep));

    /// <summary>1 目盛りで動く量。Windows の既定（120 = 3 行）に合わせる。</summary>
    private const int WheelStep = 120;

    private static int Clamp(float value, int size) => (int)Math.Clamp(value, 0, size - 1);
}
