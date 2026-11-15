using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions;

namespace SpoitableToolKit.Host.Modules.Loading;

/// <summary>読み込んだモジュールの dll から、入口の型（<see cref="ISpoitableModule"/>）を探して作る。見つからない・作れないときは<b>なぜか</b>をログに書く。</summary>
internal static class ModuleActivator
{
    /// <summary>
    /// 入り口の型を探す。見つからないときは<b>なぜ</b>見つからないかまで出す
    /// （契約 dll の二重読み込みが一番よくある原因で、症状からは分からないため）。
    /// </summary>
    /// <param name="assembly">読み込んだ入口の dll。</param>
    /// <param name="id">モジュールの Id（ログと理由の文に出す）。</param>
    /// <param name="logger">モジュールの読み込みの logger（<see cref="ModuleManager"/> のもの）。</param>
    public static Type? FindModuleType(Assembly assembly, string id, ILogger logger)
    {
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // 依存 dll が足りないと、読めた型だけが返る。読めた分で続行する。
            logger.LogWarning("{Id}: some types could not be loaded (a dependency dll may be missing): {Reason}",
                id, ex.LoaderExceptions.FirstOrDefault()?.Message);
            types = ex.Types;
        }

        var classes = types.OfType<Type>().Where(t => t is { IsClass: true, IsAbstract: false }).ToArray();

        if (Array.Find(classes, typeof(ISpoitableModule).IsAssignableFrom) is { } match) return match;

        var lookalike = classes
            .Select(type => (type, contract: Array.Find(type.GetInterfaces(), i => i.FullName == typeof(ISpoitableModule).FullName)))
            .FirstOrDefault(pair => pair.contract is not null);

        if (lookalike.contract is { } duplicated)
        {
            logger.LogError(
                "{Id}: {Type} implements ISpoitableModule, but it is a different type from the one the host loaded. " +
                "{Contract} is loaded twice (module side: {TheirContext} / host side: {OurContext}). " +
                "Add Private=\"false\" to the module's ProjectReference so {Dll} is not copied into the module output",
                id, lookalike.type.FullName, duplicated.FullName,
                AssemblyLoadContext.GetLoadContext(duplicated.Assembly)?.Name ?? "?",
                AssemblyLoadContext.GetLoadContext(typeof(ISpoitableModule).Assembly)?.Name ?? "Default",
                duplicated.Assembly.GetName().Name + ".dll");
        }
        else
        {
            logger.LogError("{Id}: {Assembly} contains no ISpoitableModule implementation", id, assembly.GetName().Name);
        }

        return null;
    }

    /// <summary>入口の型を作る（public で引数の無い constructor）。作れなければ null。</summary>
    /// <param name="type">見つけた入口の型（<see cref="FindModuleType"/>）。</param>
    /// <param name="id">モジュールの Id（ログに出す）。</param>
    /// <param name="logger">モジュールの読み込みの logger（<see cref="ModuleManager"/> のもの）。</param>
    public static ISpoitableModule? Create(Type type, string id, ILogger logger)
    {
        try
        {
            return Activator.CreateInstance(type) as ISpoitableModule;
        }
        catch (MissingMethodException)
        {
            logger.LogError("{Id}: cannot create {Type}; a public parameterless constructor is required", id, type.FullName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Id}: the constructor of {Type} threw", id, type.FullName);
        }

        return null;
    }
}
