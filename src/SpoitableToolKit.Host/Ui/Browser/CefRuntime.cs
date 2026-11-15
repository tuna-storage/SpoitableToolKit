using CefSharp;
using CefSharp.WinForms;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.App;
using SpoitableToolKit.Host.Theming;

namespace SpoitableToolKit.Host.Ui.Browser;

/// <summary>
/// <c>Cef.Shutdown()</c> のあとの <c>Cef.Initialize()</c> のやり直しは CEF がサポートしていないので、初期化はプロセス寿命で 1 回きり。
/// 表示 / 非表示は <see cref="ChromiumWebBrowser"/> の生成・破棄で回し、ここでは最初に UI が必要になった瞬間まで初期化を遅らせる。
/// </summary>
internal static class CefRuntime
{
    /// <summary>シェルの配信元。<c>file://</c> だと fetch や module が塞がるので独自スキームで出す。</summary>
    public const string UiOrigin = $"{SpoitableSchemeHandlerFactory.SchemeName}://{SpoitableSchemeHandlerFactory.ShellHost}";

    private static readonly Lock Gate = new();

    public static bool IsInitialized { get; private set; }

    /// <param name="resolveModuleWebRoot">モジュール Id → その <c>web/</c> のパス（無ければ null）。<c>spoitable://&lt;Id&gt;/</c> へのリクエストが来たときに実行時に引く。</param>
    public static void EnsureInitialized(
        Func<string, string?> resolveModuleWebRoot, UiToken token,
        HostThemes themes, Func<string> themeName, ILogger logger)
    {
        lock (Gate)
        {
            if (IsInitialized) return;

            // bind したオブジェクトが Task を返せるようにする。既定は同期メソッドのみ。
            // 最初の ChromiumWebBrowser を作る前に立てる必要がある。
            CefSharpSettings.ConcurrentTaskExecution = true;

            var settings = new CefSettings
            {
                RootCachePath = AppPaths.CefCacheDirectory,
                CachePath = AppPaths.CefCacheDirectory,
                LogFile = Path.Combine(AppPaths.DataDirectory, "cef.log"),
                LogSeverity = LogSeverity.Warning,

                // VR のオーバーレイは画面外に描いてテクスチャを取り出す（offscreen rendering）。
                // 最初の browser を作る前、初期化の時点で決まるので、ここで立てないと使えない。
                // 窓（WinForms）の browser と同じプロセスに同居する。CEF の doc は使わないなら立てるなと言っているが、SteamVR はいつ起動されるか分からないので必ず使う可能性がある。
                WindowlessRenderingEnabled = true,
            };

            // e2e 用の CDP。既定では開かない（開いていると、ローカルの何からでもこの窓を操作できてしまう）。SPOITABLE_DEBUG_PORT を指定したときだけ開く。
            if (int.TryParse(Environment.GetEnvironmentVariable("SPOITABLE_DEBUG_PORT"), out var debugPort)
                && debugPort is > 0 and < 65536)
            {
                settings.RemoteDebuggingPort = debugPort;
            }

            // DomainName を指定しない = spoitable:// 配下の全ホストをこのファクトリが受ける。
            // シェルもモジュールも同じスキームで、ホスト名だけが違う（= origin が分かれる）。
            settings.RegisterScheme(new CefCustomScheme
            {
                SchemeName = SpoitableSchemeHandlerFactory.SchemeName,
                IsStandard = true,
                IsSecure = true,
                IsCorsEnabled = true,
                SchemeHandlerFactory =
                    new SpoitableSchemeHandlerFactory(resolveModuleWebRoot, token, themes, themeName, logger),
            });

            if (!Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null))
                throw new InvalidOperationException("Cef.Initialize failed. See cef.log.");

            IsInitialized = true;
        }
    }
}
