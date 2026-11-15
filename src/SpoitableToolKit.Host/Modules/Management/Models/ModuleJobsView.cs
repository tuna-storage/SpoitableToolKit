namespace SpoitableToolKit.Host.Modules.Management.Models;

/// <summary>
/// 画面へ渡す、入れている途中のもの全部（<see cref="ModuleInstallJobs.Snapshot"/>）。
/// <paramref name="Revision"/> は変わるたびに増える（画面は、古い方を捨てる）。<paramref name="DotnetMajor"/> は「.NET SDK 10 以上が…」の 10。
/// </summary>
internal sealed record ModuleJobsView(long Revision, int DotnetMajor, ModuleJobView[] Jobs);
