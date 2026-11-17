using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace SpoitableToolKit.Host.Ui.Tray;

/// <summary>
/// Windows の「アプリ モード」が暗いときに、このプロセスの Windows 標準のメニュー（トレイのメニュー）も暗く出す。
/// uxtheme の名前の無い export を序数で引く: 135（1903 からは <c>SetPreferredAppMode</c>。前の値を返して入れ替える。1809 では <c>AllowDarkModeForApp</c>）、
/// 136（<c>FlushMenuThemes</c>）。
/// 引けなければ何もしない（メニューは明るいまま）。1809 より前の Windows では、同じ序数が別の関数を指しうるので引かない。
/// 効くのはメニューのように Windows が描くものだけ。窓の題名の帯や枠は、窓ごとに選ばない限り変わらない。起動のときに 1 回だけ呼ぶ。
/// </summary>
internal static class DarkMenus
{
    /// <summary>この序数が今の意味になった Windows の版（1809）。</summary>
    internal const int FirstBuild = 17763;

    private const int SetPreferredAppModeOrdinal = 135;
    private const int FlushMenuThemesOrdinal = 136;

    /// <summary><c>PreferredAppMode.AllowDark</c>（アプリ モードに従う）。1809 の <c>AllowDarkModeForApp</c> にとっては TRUE。</summary>
    private const int AllowDark = 1;

    /// <summary>その版の Windows で序数を引いてよいか。</summary>
    public static bool CanUseOn(int build) => build >= FirstBuild;

    /// <returns>呼べたか。</returns>
    public static bool Allow(ILogger logger)
    {
        var build = Environment.OSVersion.Version.Build;
        if (!CanUseOn(build))
        {
            logger.LogDebug("Windows {Build} is older than {First}; the tray menu stays light", build, FirstBuild);
            return false;
        }

        var uxtheme = LoadLibraryW("uxtheme.dll");
        var setMode = uxtheme == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(uxtheme, SetPreferredAppModeOrdinal);
        var flush = uxtheme == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(uxtheme, FlushMenuThemesOrdinal);
        if (setMode == IntPtr.Zero || flush == IntPtr.Zero)
        {
            logger.LogDebug("uxtheme.dll has no export {SetMode} or {Flush}; the tray menu stays light", SetPreferredAppModeOrdinal, FlushMenuThemesOrdinal);
            return false;
        }

        Marshal.GetDelegateForFunctionPointer<SetPreferredAppModeFunction>(setMode)(AllowDark);
        Marshal.GetDelegateForFunctionPointer<FlushMenuThemesFunction>(flush)();
        return true;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetPreferredAppModeFunction(int mode);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void FlushMenuThemesFunction();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadLibraryW(string name);

    /// <summary>序数で引く（名前の代わりに、下の 16 ビットに序数を入れたものを渡す）。</summary>
    [DllImport("kernel32.dll")] private static extern IntPtr GetProcAddress(IntPtr module, nint ordinal);
}
