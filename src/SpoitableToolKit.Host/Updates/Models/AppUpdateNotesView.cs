namespace SpoitableToolKit.Host.Updates.Models;

internal sealed record AppUpdateNotesView(string? Version, IReadOnlyList<ReleaseNoteBlock> Blocks);
