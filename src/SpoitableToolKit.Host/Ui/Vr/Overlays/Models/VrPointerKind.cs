namespace SpoitableToolKit.Host.Ui.Vr.Overlays.Models;

internal enum VrPointerKind
{
    Move,
    Down,
    Up,

    Scroll,

    /// <summary>引いたまま動かして巻く（自前のレーザー）。量は画素そのままで、滑らかにしない（ホイールと同じに送ると Chromium がアニメーションで追いかけて遅れて見える）。</summary>
    Pan,

    /// <summary>板から外れた。送らないと CEF の <c>:hover</c> が残る（自前のレーザーだけが送る）。</summary>
    Leave,
}
