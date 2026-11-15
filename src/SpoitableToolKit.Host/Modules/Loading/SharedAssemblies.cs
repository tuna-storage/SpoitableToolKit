using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Modules.Loading.Models;

namespace SpoitableToolKit.Host.Modules.Loading;

/// <summary>
/// 各モジュールの <c>modules/&lt;Id&gt;/shared/*.dll</c> を既定コンテキストへ載せ、全モジュールが同じ型を見るようにする。
/// <para>
/// モジュール間で受け渡す契約（<see cref="Abstractions.Modules.IExternalModuleRegistry"/> に登録する型）は、
/// どの <see cref="AssemblyLoadContext"/> から見ても<b>同一のアセンブリ</b>でなければならない。
/// 各モジュールが自分のフォルダに同じ dll を持っていると、ALC ごとに別の型になって一致しない。
/// </para>
/// <para>
/// 既定コンテキストへ載せた上で <see cref="AssemblyLoadContext.Resolving"/> にも挿す。
/// 前者はホスト自身が参照できるようにするため、後者は既定の解決が空振りしたときの受け皿。
/// </para>
/// </summary>
internal sealed class SharedAssemblies(ILoggerFactory loggerFactory)
{
    private readonly ILogger<SharedAssemblies> _logger = loggerFactory.CreateLogger<SharedAssemblies>();
    private readonly Lock _gate = new();

    private readonly Dictionary<string, Assembly> _loaded = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> _candidates = new(StringComparer.OrdinalIgnoreCase);

    private bool _hookInstalled;

    /// <summary>
    /// <b>このプロセスで一度でも共有に控えた名前</b>。既定のコンテキストへ載せたものは外せないので、
    /// 後から「本体が持っているか」を聞くと、共有の dll まで本体の物に見える。それを見分けるために覚えておく
    /// （本物のアプリでは読み込みは 1 回なので、聞く時点では空。試験は 1 つのプロセスで何度も読み込む）。
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> EverShared =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool WasEverShared(string name) => EverShared.ContainsKey(name);

    public IReadOnlyList<string> LoadedNames
    {
        get { lock (_gate) return [.. _loaded.Keys.Order()]; }
    }

    /// <summary>
    /// 載せる共有の dll を控える（<see cref="ModuleDependencies.Resolve"/> が選んだもの）。<b>読み込むのは要求されたとき</b>。
    /// 同じ名前を 2 回控えたら先の方（選ぶのは Resolve の仕事なので、ここでは決め直さない）。
    /// </summary>
    public void Register(IEnumerable<SharedFile> files)
    {
        lock (_gate)
        {
            foreach (var file in files)
            {
                if (_loaded.ContainsKey(file.Name) || !_candidates.TryAdd(file.Name, file.Path))
                {
                    _logger.LogWarning("shared {Name} is already registered; not using {Path}", file.Name, file.Path);
                    continue;
                }

                EverShared.TryAdd(file.Name, 0);
                _logger.LogInformation("Sharing {Name} {Version} from {Owner} ({Path})", file.Name, file.Version, file.Owner, file.Path);
            }
        }
    }

    /// <summary>既定の解決が空振りしたときに共有の dll から出す。ホスト起動中に 1 回だけ挿す。</summary>
    public void InstallResolvingHook()
    {
        lock (_gate)
        {
            if (_hookInstalled) return;
            _hookInstalled = true;
        }

        AssemblyLoadContext.Default.Resolving += (_, name) => Resolve(name);
    }

    /// <summary>モジュールの ALC から引く。ここで返ったものは<b>常に</b>モジュール同梱のコピーより優先する。</summary>
    public Assembly? Resolve(AssemblyName name)
    {
        if (name.Name is not { Length: > 0 } simpleName) return null;

        string path;
        lock (_gate)
        {
            if (_loaded.TryGetValue(simpleName, out var already)) return already;
            if (!_candidates.TryGetValue(simpleName, out path!)) return null;
        }

        try
        {
            return Add(simpleName, path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load shared dependency {Name}", simpleName);
            return null;
        }
    }

    /// <summary>
    /// 既定コンテキストへ載せる。<paramref name="simpleName"/> は<b>中身の名前</b>（控えたときの名前。ファイル名とは違いうる）。
    /// <b>ファイルは開きっぱなしにしない</b>（<see cref="AssemblyLoadContext.LoadFromAssemblyPath"/> だと
    /// dll がロックされ、アプリを起動したままビルドできなくなる）。
    /// </summary>
    private Assembly Add(string simpleName, string path)
    {
        lock (_gate)
        {
            if (_loaded.TryGetValue(simpleName, out var already)) return already;
        }

        // ホスト本体が既に同名を持っているなら、そちらが正。二重に載せない。
        if (AssemblyLoadContext.Default.Assemblies
                .FirstOrDefault(a => string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
            is { } hostOwned)
        {
            _logger.LogWarning("shared {Name} is also in the host itself; using the host's copy (ignoring the one in shared/)", simpleName);
            lock (_gate) _loaded[simpleName] = hostOwned;
            return hostOwned;
        }

        using var dll = File.OpenRead(path);
        var pdb = Path.ChangeExtension(path, ".pdb");

        Assembly assembly;
        if (File.Exists(pdb))
        {
            using var symbols = File.OpenRead(pdb);
            assembly = AssemblyLoadContext.Default.LoadFromStream(dll, symbols);
        }
        else
        {
            assembly = AssemblyLoadContext.Default.LoadFromStream(dll);
        }

        lock (_gate) _loaded[assembly.GetName().Name ?? simpleName] = assembly;

        _logger.LogInformation("Loaded shared {Name} ({Path})", simpleName, path);
        return assembly;
    }
}
