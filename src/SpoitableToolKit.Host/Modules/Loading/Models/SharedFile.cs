namespace SpoitableToolKit.Host.Modules.Loading.Models;

/// <summary>
/// <c>modules/&lt;Id&gt;/shared/</c> に置かれた dll 1 つ。<paramref name="Owner"/> は持ってきたモジュール（ライブラリ）の Id。
/// </summary>
internal sealed record SharedFile(string Owner, string Path, string Name, Version Version, string Sha256,
    IReadOnlyList<AssemblyReference> References);
