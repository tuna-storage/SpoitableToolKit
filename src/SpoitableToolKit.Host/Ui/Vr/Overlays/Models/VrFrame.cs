namespace SpoitableToolKit.Host.Ui.Vr.Overlays.Models;

/// <summary>
/// テクスチャは変わらない（CEF の絵はこちらの 1 枚へ写し取る）。中身が変わったかどうかは <see cref="Sequence"/> で見る —
/// ポインタの比較だけだと 1 枚目で止まったままになる。
/// </summary>
/// <param name="Texture">OpenVR へ渡す <c>ID3D11Texture2D*</c>。まだ無ければ 0。</param>
/// <param name="Sequence">写し取った回数。0 は作っただけで、まだ中身が無い。</param>
internal readonly record struct VrFrame(nint Texture, long Sequence)
{
    public static VrFrame None => default;
}
