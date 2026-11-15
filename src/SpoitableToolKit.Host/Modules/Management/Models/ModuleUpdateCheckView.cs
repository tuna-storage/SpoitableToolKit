namespace SpoitableToolKit.Host.Modules.Management.Models;

/// <summary>
/// 「更新を確認」の答え。<paramref name="Ok"/> が false なら、答えの返らなかった入れ元がある（画面は「更新はありません」と言わない）。
/// <paramref name="Catalog"/> は確かめたあとの中身。
/// </summary>
internal sealed record ModuleUpdateCheckView(bool Ok, ModuleCatalogView Catalog);
