using CefSharp;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Theming;

namespace SpoitableToolKit.Host.Ui.Browser;

/// <summary>
/// <c>spoitable://</c> の配信元。ホスト名でどのフォルダから出すかを決める: <c>ui</c> はシェル本体（exe の隣の <c>web/</c>）、<c>&lt;モジュール Id&gt;</c> はそのモジュールの <c>web/</c>、<c>…/_host/…</c> はどの origin からでもシェルが用意したもの（<c>bridge.js</c> など）。
/// モジュール 1 つ = 1 origin にするのが肝。同じ document に混ぜると CSS が衝突し、1 つの JS エラーで全部巻き添えになる。origin が分かれていれば、シェルは <c>postMessage</c> の発信元から呼び出し元のモジュールを特定できる。
/// ドメインごとに <c>CefCustomScheme</c> を登録する形にしないのは、<c>Cef.Initialize</c> の時点でモジュール一覧が確定している必要が出るため。実行時に引く方が読み込み順に縛られない。
/// </summary>
internal sealed class SpoitableSchemeHandlerFactory(
    Func<string, string?> resolveModuleWebRoot, UiToken token,
    HostThemes themes, Func<string> themeName, ILogger logger) : ISchemeHandlerFactory
{
    public const string SchemeName = "spoitable";

    public const string ShellHost = "ui";

    private const string SharedPrefix = "_host/";

    /// <summary>シェルの入口。ここにだけ token を差し込む。<c>wrist.html</c> は VR の手首の板 — シェルと同じ origin の、もう 1 つの入口。</summary>
    private static readonly string[] ShellEntryPaths = ["index.html", "wrist.html"];

    /// <summary>配色。ディスクのファイルではなく、選ばれているテーマから組み立てて配る。どの origin から引かれてもここを通るので、シェルもパネルも同じ 1 枚を見る。</summary>
    private const string ThemePath = SharedPrefix + "theme.css";

    /// <summary>token を書き込む目印。配る瞬間に差し替えるので、ディスク上の HTML には token が残らない。</summary>
    private const string TokenPlaceholder = "__STK_TOKEN__";

    public IResourceHandler? Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var url)) return NotFound();

        var path = Uri.UnescapeDataString(url.AbsolutePath).TrimStart('/');
        if (path.Length == 0) path = "index.html";

        var root = path.StartsWith(SharedPrefix, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(url.Host, ShellHost, StringComparison.OrdinalIgnoreCase)
            ? ShellWebRoot
            : resolveModuleWebRoot(url.Host);

        if (string.Equals(path, ThemePath, StringComparison.OrdinalIgnoreCase))
        {
            // MIME は文字列で書く。ドットの有無で結果が変わる引き方があり、外すと Chromium がスタイルシートを黙って捨てる（色が全部消えるだけで、エラーは出ない）。
            return ResourceHandler.FromString(themes.Read(themeName()), mimeType: "text/css");
        }

        if (root is null) return NotFound();
        if (Resolve(root, path) is not { } file) return NotFound();

        // token はシェルの入口に埋め込んで配る。ここで配るのが唯一の経路にしてある。
        if (IsShellEntry(url, path, frame is { IsMain: true })) return ShellEntry(file, path);

        // HTML にはテーマの版を差し込んでから配る。差し込まないと、テーマを変えて読み込み直しても、スタイルシートだけ前の応答が使われる。
        if (file.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            return ResourceHandler.FromString(WithThemeVersion(File.ReadAllText(file)), mimeType: "text/html");

        return ResourceHandler.FromFilePath(file, ResourceHandler.GetMimeType(Path.GetExtension(file)),
            autoDisposeStream: true);
    }

    /// <summary>
    /// シェルの入口そのものか。origin がシェル / パスが入口 / メインフレームからの要求、の 3 つとも要る。
    /// 最後のひとつは、モジュールが <c>&lt;script src="spoitable://ui/index.html"&gt;</c> のような形で自分のフレームへ引き込もうとしたときのため（<c>fetch</c> は別 origin なので CORS で塞がる。このハンドラは <c>Access-Control-Allow-Origin</c> を出さない）。
    /// フレームは「メインか」だけ受ける（CefSharp の型を取らず、判断だけを試験から通すため）。
    /// </summary>
    internal static bool IsShellEntry(Uri url, string path, bool mainFrame) =>
        string.Equals(url.Host, ShellHost, StringComparison.OrdinalIgnoreCase)
        && ShellEntryPaths.Contains(path, StringComparer.OrdinalIgnoreCase)
        && mainFrame;

    /// <summary>
    /// token を差し込んだシェルを返す。配信時に埋め込むのはレースを無くすため。
    /// <c>IRenderProcessMessageHandler.OnContextCreated</c> から <c>ExecuteJavaScriptAsync</c> で流し込む形は、レンダラからの IPC を受けてブラウザ側から投げ返すのでページ自身のスクリプトに負ける。HTML に入って配られれば、最初の 1 行より前に必ず居る。
    /// </summary>
    private IResourceHandler ShellEntry(string file, string path)
    {
        var html = File.ReadAllText(file);

        // 目印が無ければ token はどこにも入らず、ホストを一切呼べないシェルになる。boot() が例外で止まるので画面には出るが、理由はここにしか無い。
        if (!html.Contains(TokenPlaceholder, StringComparison.Ordinal))
            logger.LogError("{Placeholder} is missing from the shell's {File}; the page will not be able to call the host",
                TokenPlaceholder, path);

        return ResourceHandler.FromString(
            WithThemeVersion(html.Replace(TokenPlaceholder, token.Value, StringComparison.Ordinal)),
            mimeType: "text/html");
    }

    /// <summary>ページの中の <c>theme.css</c> の URL に、いまのテーマの版を付ける。キャッシュを外すのが目的で、中身が同じなら版も同じなので取り直しは起きない。</summary>
    private string WithThemeVersion(string html) =>
        html.Replace(ThemeUrl, $"{ThemeUrl}?v={themes.Version(themeName())}", StringComparison.Ordinal);

    private const string ThemeUrl = "/" + ThemePath;

    private static string ShellWebRoot { get; } = Path.Combine(AppContext.BaseDirectory, "web");

    /// <summary>URL のパスを実ファイルへ落とす。<c>..</c> でルートの外へ出るものは弾く（モジュールのページは第三者が書きうるので、配信側で閉じ込める）。</summary>
    private static string? Resolve(string root, string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }

        var boundary = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(boundary, StringComparison.OrdinalIgnoreCase)) return null;

        return File.Exists(full) ? full : null;
    }

    private static IResourceHandler NotFound() =>
        ResourceHandler.ForErrorMessage("not found", System.Net.HttpStatusCode.NotFound);
}
