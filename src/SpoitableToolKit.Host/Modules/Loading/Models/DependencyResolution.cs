namespace SpoitableToolKit.Host.Modules.Loading.Models;

internal sealed record DependencyResolution(
    IReadOnlyDictionary<string, SharedFile> Shared,
    IReadOnlyDictionary<string, DependencyProblem> Failures,
    IReadOnlyList<string> Warnings);
