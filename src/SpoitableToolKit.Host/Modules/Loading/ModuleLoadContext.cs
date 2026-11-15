using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Modules;

namespace SpoitableToolKit.Host.Modules.Loading;

/// <summary>
/// モジュール 1 つ分の <see cref="AssemblyLoadContext"/>。
/// <para>
/// 解決の順番が肝で、<b>共有すべきものを先に決める</b>:
/// </para>
/// <list type="number">
///   <item>shared/ から読んだもの（モジュールが同じ dll を同梱していても、こちらを使う）</item>
///   <item>モジュールのフォルダに無いもの → null を返して既定コンテキストへ委ねる
///         （<c>SpoitableToolKit.Abstractions</c> はここを通る）</item>
///   <item>モジュールのフォルダに<b>ある</b>もの → 既定コンテキストが同じ名前を解決できるなら
///         そちらを優先し、駄目なら同梱のコピーを読む</item>
/// </list>
/// <para>
/// 3 番目が無いと、<c>Private="false"</c> を忘れて契約 dll を同梱したモジュールが
/// 型を二重に読み込み、<see cref="Abstractions.Modules.IExternalModuleRegistry.GetRequired{T}"/> が
/// 「登録されているのに一致しない」という分かりにくい失敗をする。
/// 代償として、ホストと同じアセンブリの<b>新しい版</b>を同梱しても古い方が使われる。
/// v1 はこれを許容する（黙って型が二重になるより、版が揃っている方がまだ追える）。
/// </para>
/// <para>
/// モジュールのフォルダ（<see cref="ModuleDirectory"/>）を知っている。モジュールは SDK の build が作る <c>ModuleInfo.ModuleDirectory</c> から、
/// 自分の dll を読み込んだここを引いて読む（dll はメモリから読むので、<c>Assembly.Location</c> は空）。
/// </para>
/// </summary>
internal sealed class ModuleLoadContext : AssemblyLoadContext, IModuleLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly SharedAssemblies _shared;
    private readonly ILogger _logger;

    /// <param name="name">ログと例外に出る名前（<c>module:&lt;Id&gt;</c>）。</param>
    /// <param name="entryAssemblyPath">入口の dll。依存はこれの <c>*.deps.json</c> から探す。</param>
    /// <param name="moduleDirectory">モジュールのフォルダ（<c>modules/&lt;Id&gt;/</c>）。</param>
    /// <param name="shared">shared/ から読んだ dll。</param>
    /// <param name="logger">ローダーの logger。</param>
    public ModuleLoadContext(string name, string entryAssemblyPath, string moduleDirectory, SharedAssemblies shared, ILogger logger)
        : base(name, isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        ModuleDirectory = moduleDirectory;
        _shared = shared;
        _logger = logger;
    }

    public string ModuleDirectory { get; }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (_shared.Resolve(assemblyName) is { } shared) return shared;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);

        // deps.json に載っていても実体が無いことがある（ProjectReference Private="false" など）。
        // その場合も既定コンテキストへ委ねる。
        if (path is null || !File.Exists(path)) return null;

        if (TryLoadFromDefault(assemblyName) is { } hostOwned)
        {
            _logger.LogDebug("{Module}: using the host's {Assembly} instead of the bundled copy", Name, assemblyName.Name);
            return hostOwned;
        }

        return LoadFromFile(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }

    /// <summary>
    /// ファイルをロックせずに読む。ロックすると、アプリを起動したままビルドできなくなる。
    /// </summary>
    public Assembly LoadFromFile(string path)
    {
        using var dll = File.OpenRead(path);
        var pdb = Path.ChangeExtension(path, ".pdb");

        if (!File.Exists(pdb)) return LoadFromStream(dll);

        using var symbols = File.OpenRead(pdb);
        return LoadFromStream(dll, symbols);
    }

    private static Assembly? TryLoadFromDefault(AssemblyName assemblyName)
    {
        try
        {
            return Default.LoadFromAssemblyName(assemblyName);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            // ホスト側には無い。モジュール同梱のコピーを使う。
            return null;
        }
    }
}
