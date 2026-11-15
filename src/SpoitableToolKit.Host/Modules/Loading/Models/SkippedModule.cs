namespace SpoitableToolKit.Host.Modules.Loading.Models;

/// <summary>読み込まなかったもの（管理画面で理由を出すため）。</summary>
internal sealed record SkippedModule(
    string Id, string Directory, string Root, SkipReason Reason, DependencyProblem? Problem = null, string? Detail = null);
