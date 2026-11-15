namespace SpoitableToolKit.Host.Modules.Loading.Models;

/// <summary>
/// 調べる単位（モジュール 1 つ、またはライブラリ 1 つ）。ライブラリは入口の dll を持たないので <paramref name="EntryReferences"/> が空。
/// </summary>
internal sealed record ModuleUnit(string Id, IReadOnlyList<AssemblyReference> EntryReferences, IReadOnlyList<SharedFile> Shared);
