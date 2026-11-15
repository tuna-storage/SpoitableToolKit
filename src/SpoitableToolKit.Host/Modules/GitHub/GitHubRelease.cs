namespace SpoitableToolKit.Host.Modules.GitHub;

internal sealed record GitHubRelease(string Tag, IReadOnlyList<ReleaseAsset> Assets);
