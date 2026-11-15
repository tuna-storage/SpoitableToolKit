namespace SpoitableToolKit.Host.Modules.Management.Catalog;

/// <summary>
/// 1 行分。画面へは <see cref="Models.ModuleEntryView"/> にして渡す（<paramref name="Directory"/> は渡さない）。
/// <paramref name="Source"/>・<paramref name="SourceTag"/> は入れ元（GitHub の <c>owner/name</c> とタグ。次の起動で入る物があれば、その入れ元）、
/// <paramref name="Update"/> は入れ元にある新しいタグ、
/// <paramref name="Pending"/> は次の起動で入る版（どれも無ければ null）。<paramref name="State"/> が <c>pending</c> の行は、まだ入っていないもの。
/// </summary>
internal sealed record ModuleCatalogEntry(
    string Id, string Name, string Kind, string? Version, bool Bundled, string State, string? Reason, string[] Shared,
    bool Enabled, bool EnabledAtStart, bool Removing, bool CanRemove, bool CanToggle, bool Overrides, string Directory,
    string? Source = null, string? SourceTag = null, string? Update = null, string? Pending = null, bool Built = false,
    string? PendingCommit = null);
