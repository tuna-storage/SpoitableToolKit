namespace SpoitableToolKit.Host.Modules.Management.Models;

/// <summary>
/// 画面へ渡す 1 件（カード 1 枚）。
/// <list type="bullet">
///   <item><paramref name="Title"/> は repo（URL が読めなければ、絞った字）。<paramref name="Commit"/> は build するもの（リリースから入れる件は null）</item>
///   <item><paramref name="State"/> は <see cref="ModuleJobStates"/> の名前</item>
///   <item><paramref name="Ask"/>（質問の通し番号）と <paramref name="AskKind"/>（<c>build</c> / <c>sdk</c> / <c>install</c>）は、<b>答えを待っている間だけ</b>入る
///         （答えたあと・聞いていないときは 0 と null）。画面は、これをそのまま <see cref="ModuleInstallJobs.Answer"/> に返す</item>
///   <item><paramref name="Step"/> / <paramref name="Steps"/> は手順（.NET SDK が要るときだけ 1/2 → 2/2。要らなければ 0）、
///         <paramref name="Phase"/> は build のどこか（<c>sdk</c> / <c>source</c> / <c>build</c> / <c>check</c>）、<paramref name="Since"/> はそれを始めた時刻（unix の ms）</item>
///   <item><paramref name="Tag"/>・<paramref name="Asset"/>・<paramref name="Bytes"/>・<paramref name="Total"/> は落としているもの（<paramref name="Total"/> が 0 なら全部の量は分からない）、
///         <paramref name="SdkVersion"/> は落とす .NET SDK、<paramref name="WaitingFor"/> は build の順番を待っている相手</item>
///   <item><paramref name="FailedAt"/>（<c>install</c> / <c>build</c> / <c>sdk</c>）と <paramref name="Error"/> は失敗して残っている件、
///         <paramref name="Note"/> は聞き直している訳（<c>confirmFailed</c>）、<paramref name="Staged"/> は入れるかを聞いているもの</item>
/// </list>
/// </summary>
internal sealed record ModuleJobView(
    string Id, string Title, string? Repository, string? Commit, string State, int Ask, string? AskKind,
    int Step, int Steps, string? Phase, long? Since, string? Tag, string? Asset, long Bytes, long Total,
    string? SdkVersion, string? WaitingFor, string? FailedAt, string? Error, string? Note, StagedModuleView? Staged);
