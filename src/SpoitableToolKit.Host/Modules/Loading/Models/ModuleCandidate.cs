namespace SpoitableToolKit.Host.Modules.Loading.Models;

/// <summary>
/// <c>modules/&lt;Id&gt;/</c> 1 つ。<paramref name="Id"/> はディレクトリ名（= <c>ISpoitableModule.Id</c> のはず）。
/// <b><paramref name="EntryAssembly"/> が null ならライブラリ</b>（モジュールの dll を持たず、<c>shared/</c> の dll で API だけを渡すもの）。
/// </summary>
internal sealed record ModuleCandidate(string Id, string Directory, string? EntryAssembly, string Root)
{
    public bool IsLibrary => EntryAssembly is null;

    /// <summary>このモジュール（ライブラリ）が持ってきた共有の dll の置き場。</summary>
    public string SharedDirectory => Path.Combine(Directory, ModuleDiscovery.SharedDirectoryName);
}
