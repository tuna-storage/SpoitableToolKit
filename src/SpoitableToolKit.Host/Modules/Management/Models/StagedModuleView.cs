namespace SpoitableToolKit.Host.Modules.Management.Models;

/// <summary>
/// 画面へ渡す、仮置きしたもの（「これをインストールしますか」と聞く中身。<see cref="ModuleCatalog.Describe"/>）。
/// <paramref name="Built"/> はこの PC でソースから build したもの（そのとき <paramref name="Tag"/> は commit）。
/// <paramref name="Replaces"/> は同じ Id がもう入っているか（入れると、その上に入る）、<paramref name="ReplacesVersion"/> はその版、
/// <paramref name="ReplacesBundled"/> はそれが同梱のものか（同梱の版より優先される）。
/// </summary>
internal sealed record StagedModuleView(
    string Token, string Id, string Name, string Version, string Kind, string Repository, string Tag, string Asset, string Sha256,
    bool Built, bool Replaces, string? ReplacesVersion, bool ReplacesBundled);
