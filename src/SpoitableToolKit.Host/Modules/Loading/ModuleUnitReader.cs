using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Modules.Loading.Models;

namespace SpoitableToolKit.Host.Modules.Loading;

/// <summary>
/// 読み込む<b>前に</b>、1 つ分の中身（入口の dll の参照・<c>shared/</c> の dll）と、本体・モジュールのフォルダが何を出せるかを読む。
/// <b>読み込みはしない</b>（読むのは dll の metadata だけ）。結果は <see cref="ModuleDependencies.Resolve"/> に渡る。
/// </summary>
/// <param name="logger">モジュールの読み込みの logger（<see cref="ModuleManager"/> のもの）。</param>
/// <param name="readEntryMetadata">入口の dll の中身を読む口（開けないと null）。</param>
internal sealed class ModuleUnitReader(
    ILogger logger,
    Func<string, (string Name, Version Version, IReadOnlyList<AssemblyReference> References)?> readEntryMetadata)
{
    /// <summary>
    /// <see cref="Read"/> を 1 つずつ守る。<b>1 つのフォルダが読めないせいで、ほかのモジュールまで止めない</b>。
    /// 読めなければ、共有の dll を持たないものとして扱う（入口の dll が読めなければ、そのあとの読み込みで落ちて理由が出る）。
    /// </summary>
    public ModuleUnit TryRead(ModuleCandidate candidate)
    {
        try
        {
            return Read(candidate);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "{Id}: could not read its files; sharing nothing from it", candidate.Id);
            return new ModuleUnit(candidate.Id, [], []);
        }
    }

    /// <summary>
    /// 1 つ分の中身を読む（入口の dll の参照・<c>shared/</c> の dll）。<b>読み込まない</b>。
    /// .NET の dll でないもの・本体が同じ名前を持っているものは、共有に入れずに警告する。
    /// </summary>
    private ModuleUnit Read(ModuleCandidate candidate)
    {
        IReadOnlyList<AssemblyReference> entryReferences =
            candidate.EntryAssembly is { } entry && readEntryMetadata(entry) is { } metadata ? metadata.References : [];

        List<SharedFile> shared = [];
        if (Directory.Exists(candidate.SharedDirectory))
        {
            foreach (var path in Directory.GetFiles(candidate.SharedDirectory, "*.dll").Order(StringComparer.OrdinalIgnoreCase))
            {
                if (AssemblyMetadata.Read(path) is not { } file)
                {
                    logger.LogWarning("{Id}: {File} in shared/ is not a .NET assembly; only .NET dlls can be shared", candidate.Id, path);
                    continue;
                }

                if (HostResolves(new AssemblyReference(file.Name, new Version(0, 0))))
                {
                    logger.LogWarning("{Id}: the host already has {Name}; not sharing {File}", candidate.Id, file.Name, path);
                    continue;
                }

                string sha;
                try
                {
                    sha = AssemblyMetadata.Sha256(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "{Id}: could not read {File}; not sharing it", candidate.Id, path);
                    continue;
                }

                shared.Add(new SharedFile(candidate.Id, path, file.Name, file.Version, sha, file.References));
            }
        }

        return new ModuleUnit(candidate.Id, entryReferences, shared);
    }

    /// <summary>
    /// 本体（既定のコンテキスト）が、その名前をその版以上で出せるか。
    /// <b>共有の解決フックを挿す前に呼ぶ</b>（挿した後だと、共有の dll まで本体が持っているように見える）。
    /// </summary>
    public static bool HostResolves(AssemblyReference reference)
    {
        // 前に共有へ載せたものは本体の物ではない（外せずに残っているだけ）。
        if (SharedAssemblies.WasEverShared(reference.Name)) return false;

        try
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(reference.Name) { Version = reference.Version }) is not null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// 本体（既定のコンテキスト）がその名前で持っている版。持っていなければ null。
    /// 読み込めない理由の文のためだけに聞く（「見つからない」と「アプリが古い」を分ける）。
    /// </summary>
    public static Version? HostVersion(string name)
    {
        if (SharedAssemblies.WasEverShared(name)) return null;

        try
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(name)).GetName().Version;
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>モジュールのフォルダ（deps.json）から出せるか。<see cref="ModuleLoadContext"/> と同じ解き方。</summary>
    public static bool OwnFolderHas(ModuleCandidate candidate, AssemblyReference reference,
        Dictionary<string, AssemblyDependencyResolver?> resolvers)
    {
        if (candidate.EntryAssembly is not { } entry) return false;

        if (!resolvers.TryGetValue(candidate.Id, out var resolver))
        {
            try
            {
                resolver = new AssemblyDependencyResolver(entry);
            }
            catch (InvalidOperationException)
            {
                resolver = null;
            }

            resolvers[candidate.Id] = resolver;
        }

        return resolver?.ResolveAssemblyToPath(new AssemblyName(reference.Name)) is { } path && File.Exists(path);
    }
}
