using System.Reflection;
using System.Runtime.Loader;

namespace SpoitableToolKit.Abstractions.Modules;

/// <summary>
/// モジュールを読み込んだ <c>AssemblyLoadContext</c> が知っている、そのモジュールのフォルダ。<b>本体のローダーが実装する。</b>
/// モジュールはこれを直に使わず、SDK の build が作る <c>ModuleInfo.ModuleDirectory</c> から読む（中で <see cref="DirectoryOf"/> を呼ぶ）。
/// </summary>
public interface IModuleLoadContext
{
    /// <summary>モジュールのフォルダ（<c>modules/&lt;Id&gt;/</c>）の絶対パス。</summary>
    string ModuleDirectory { get; }

    /// <summary>
    /// <paramref name="assembly"/> を読み込んだモジュールのフォルダ（<c>modules/&lt;Id&gt;/</c>）の絶対パス。
    /// <list type="bullet">
    ///   <item>中身は build の出力そのまま（zip で配ったもの）。モジュールが持ってきたファイルは <c>Path.Combine(ModuleInfo.ModuleDirectory, "some/file")</c> で開ける</item>
    ///   <item><b>読むだけ。</b> 更新するとフォルダごと入れ替わり、同梱のモジュールのフォルダは書けない場所にあることがある。
    ///         書くもの・あとから更新するデータは <see cref="IModuleHost.DataDirectory"/> に置く</item>
    ///   <item>本体の外（モジュールの単体試験など）で読み込まれた dll にはフォルダが無いので、<see cref="InvalidOperationException"/></item>
    /// </list>
    /// </summary>
    /// <param name="assembly">モジュールの dll（<c>typeof(ModuleInfo).Assembly</c>）。</param>
    static string DirectoryOf(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (AssemblyLoadContext.GetLoadContext(assembly) is IModuleLoadContext context) return context.ModuleDirectory;

        throw new InvalidOperationException(
            $"{assembly.GetName().Name} was not loaded by SpoitableToolKit as a module, so it has no module folder. " +
            "ModuleInfo.ModuleDirectory is known only while the app runs the module (the app loads it from modules/<Id>/). " +
            "Code that runs outside the app, such as a unit test of the module, has to be given the folder in another way.");
    }
}
