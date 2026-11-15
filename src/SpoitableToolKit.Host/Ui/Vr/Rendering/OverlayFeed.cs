using SpoitableToolKit.Host.Ui.Vr.Overlays;

namespace SpoitableToolKit.Host.Ui.Vr.Rendering;

internal sealed class OverlayFeed(IVrOverlays overlays, VrHost.TextureSource textures)
{
    private nint _shown;

    private long _sequence = -1;

    public int Uploads { get; private set; }

    /// <summary>
    /// ポインタでは判断できない。写し取る形なので指す先はずっと同じで、変わったかは何枚目かでしか分からない（ポインタだけで見ると 1 枚目で止まったままになる）。
    /// </summary>
    public void Hand(ulong handle) => textures(frame =>
    {
        // 中身の無い板は渡さない。テクスチャがまだ無い間と、有っても 1 枚も写していない間があり、
        // どちらも 0 枚目。作っただけのテクスチャに何が入っているかは決まっていないので、渡すとゴミが板に出る。
        if (frame.Sequence == 0) return;

        if (frame.Texture == _shown && frame.Sequence == _sequence) return;

        // 借りている間に渡す。ここを貸し借りの外へ出すと、渡すまでの隙間で捨てられうる。
        overlays.UploadTexture(handle, frame.Texture);
        _shown = frame.Texture;
        _sequence = frame.Sequence;
        Uploads++;
    });

    /// <summary>板を作り直したとき。次に来た絵は同じ番号でも渡し直す（新しい板は何も持っていない）。</summary>
    public void Forget()
    {
        _shown = 0;
        _sequence = -1;
    }
}
