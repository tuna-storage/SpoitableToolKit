namespace SpoitableToolKit.Host.Modules.Management.Models;

/// <summary>
/// 画面へ渡す管理画面の中身（<see cref="ModuleCatalog.Snapshot"/>。<c>modulesChanged</c> で配るものと同じ形）。
/// <paramref name="UpdatesCheckedAt"/> は最後に更新を確かめた時刻（ISO 8601。確かめたことが無ければ null）。
/// </summary>
internal sealed record ModuleCatalogView(bool RestartNeeded, string? UpdatesCheckedAt, ModuleEntryView[] Entries);
