namespace SpoitableToolKit.Host.Modules.Management.Models;

/// <summary>
/// 「モジュールを追加」の URL の段の答え（<see cref="ModuleInstallJobs.Check"/>。件はまだ作らない）。
/// <list type="bullet">
///   <item><paramref name="Repository"/> は読めた repo（<c>owner/name</c>）</item>
///   <item><paramref name="Problem"/> は断った訳の種類: <see cref="UrlProblem"/>（GitHub の repo の URL でない）か <see cref="BusyProblem"/>（いまは始められない）。
///         <paramref name="Error"/> はその文（英語。画面は見出しの下にそのまま出す）</item>
///   <item><paramref name="ShowAgreement"/> は、件を始める前に同意の段を出すか（本体の設定 <see cref="Settings.HostConfig.ShowModuleAgreement"/>）</item>
/// </list>
/// </summary>
internal sealed record ModuleUrlCheck(bool Ok, string? Repository, string? Problem, string? Error, bool ShowAgreement)
{
    public const string UrlProblem = "url";
    public const string BusyProblem = "busy";
}
