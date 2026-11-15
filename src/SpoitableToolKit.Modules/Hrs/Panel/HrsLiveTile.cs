using SpoitableToolKit.Abstractions.Shortcuts;
using SpoitableToolKit.Abstractions.Ui;

namespace SpoitableToolKit.Modules.Hrs.Panel;

/// <summary>
/// 手首のタイル <c>hrs/live</c>: <b>タイルの中に今の心拍と直近の線を描く</b>（<c>web/tile.html</c>）。押すと心拍のパネルを空間の窓で開く（開いていれば閉じる）。
/// パネルを開くだけのタイル（字で「72 bpm」）は本体が用意するので、これはその横に並べる絵のある方。
/// </summary>
internal sealed class HrsLiveTile(IModuleUi ui) : IClickableWristShortcut
{
    public string Id => "live";

    public string Name => "hrs.shortcut.live";

    public string? IconPath => HrsModule.HeartPath;

    public string? Page => "tile.html";

    /// <summary>空間の窓が心拍のパネルを出している間は、タイルに枠を付ける（開くのは下の <see cref="OnTriggerAsync"/>）。</summary>
    public PanelView? MarkedPanel => new();

    public Task OnTriggerAsync(CancellationToken cancellationToken)
    {
        ui.ToggleVrPanel();
        return Task.CompletedTask;
    }
}
