using SpoitableToolKit.Abstractions.Shortcuts;
using SpoitableToolKit.Abstractions.Ui;

namespace SpoitableToolKit.Modules.VideoUrl.Shortcuts;

/// <summary>
/// 手首のタイル <c>videourl/resolve-latest</c>: <b>いちばん新しい失敗を解いてコピーする</b>。
/// 確認に答えそびれたとき・「聞かない」にしているときに、窓を開かずに手首から解ける。
/// タイルには解いている間だけ「解決中…」を出す（押してから結果のトーストまで数秒かかる）。
/// </summary>
internal sealed class ResolveLatestShortcut(VideoUrlModule module, ModuleStatus resolving) : IClickableWristShortcut
{
    public string Id => "resolve-latest";

    public string Name => "videourl.shortcut.resolveLatest";

    public string? IconPath => VideoUrlModule.CopiedKind.IconPath;

    public ModuleStatus? Status => resolving;

    public Task OnTriggerAsync(CancellationToken cancellationToken) => module.ResolveLatestAsync(cancellationToken);
}
