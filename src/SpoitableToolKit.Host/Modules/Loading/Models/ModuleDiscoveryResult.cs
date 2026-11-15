namespace SpoitableToolKit.Host.Modules.Loading.Models;

internal sealed record ModuleDiscoveryResult(IReadOnlyList<ModuleCandidate> Candidates, IReadOnlyList<SkippedModule> Skipped);
