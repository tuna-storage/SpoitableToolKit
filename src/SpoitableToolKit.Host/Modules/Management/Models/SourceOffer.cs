using SpoitableToolKit.Host.Modules.Packaging;

namespace SpoitableToolKit.Host.Modules.Management.Models;

/// <summary>
/// 「この commit を build しますか」と利用者に聞くもの。<b>同意と commit の結び付けは本体が持つ</b>:
/// 聞いた 1 件（<see cref="ModuleInstallJobs"/>）がこれを持ち、build はこれをそのまま受ける（画面から repo や commit を受け取らない）。
/// </summary>
internal sealed record SourceOffer(GitHubRepository Repository, string Commit);
