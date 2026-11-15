using SpoitableToolKit.Host.Modules.Management.Catalog;

namespace SpoitableToolKit.Host.Modules.Management.Models;

/// <summary>
/// 画面へ渡す 1 行（<see cref="ModuleCatalogEntry"/> から、画面が使うものだけ。置き場のフォルダは渡さない）。
/// <list type="bullet">
///   <item><paramref name="Kind"/> は <c>module</c> / <c>library</c>、<paramref name="State"/> は <c>running</c> / <c>off</c> / <c>failed</c> / <c>refused</c> / <c>broken</c> / <c>pending</c>（まだ入っていない）</item>
///   <item><paramref name="Source"/>・<paramref name="SourceTag"/> は入れ元（GitHub の <c>owner/name</c> とタグ。<paramref name="Built"/> なら commit）。
///         次の起動で入る物があれば、その入れ元</item>
///   <item><paramref name="Update"/> は入れ元にある新しいタグ（か commit）、<paramref name="Pending"/> は次の起動で入る版、
///         <paramref name="PendingCommit"/> はそれがソースから Build した物のときの commit</item>
/// </list>
/// </summary>
internal sealed record ModuleEntryView(
    string Id, string Name, string Kind, string? Version, bool Bundled, string State, string? Reason, string[] Shared,
    bool Enabled, bool EnabledAtStart, bool Removing, bool CanRemove, bool CanToggle, bool Overrides,
    string? Source, string? SourceTag, string? Update, string? Pending, bool Built, string? PendingCommit)
{
    public static ModuleEntryView From(ModuleCatalogEntry entry) => new(
        entry.Id, entry.Name, entry.Kind, entry.Version, entry.Bundled, entry.State, entry.Reason, entry.Shared,
        entry.Enabled, entry.EnabledAtStart, entry.Removing, entry.CanRemove, entry.CanToggle, entry.Overrides,
        entry.Source, entry.SourceTag, entry.Update, entry.Pending, entry.Built, entry.PendingCommit);
}
