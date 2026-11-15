using SpoitableToolKit.Host.Modules.Packaging;

namespace SpoitableToolKit.Host.Modules.Management.Models;

/// <summary>仮置きしたもの（入れるかを利用者に聞いている間）。<paramref name="Kind"/> は <c>module</c> か <c>library</c>。</summary>
internal sealed record StagedModule(string Token, ModuleManifest Manifest, ModuleSource Source, string Directory, string Kind);
