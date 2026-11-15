using SpoitableToolKit.Host.Modules.Packaging;

namespace SpoitableToolKit.Host.Modules.Management.Models;

/// <summary>次の起動で入るもの（<c>modules/.pending/&lt;Id&gt;</c>）。</summary>
internal sealed record PendingModule(ModuleManifest Manifest, ModuleSource? Source, string Directory, string Kind);
