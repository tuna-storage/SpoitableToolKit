using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace SpoitableToolKit.Host.Modules;

/// <summary>
/// モジュールのページを決める（サイドバーに出すページと、VR の空間の窓で出すページ）。
/// <b>登録だけで決めず、ファイルがあるかを見る</b>。登録は OnInit の中（<see cref="ModuleUi"/>）なので、決めるのは OnInit の後。
/// </summary>
internal static partial class ModulePanelEntries
{
    /// <summary>
    /// パネルの入口を決める。<b>実体が無ければ「パネル無し」に倒す</b>。
    /// 登録だけ信じると、サイドバーに項目が出るのに中身が 404 という一番分かりにくい壊れ方になる。
    /// </summary>
    /// <param name="moduleId">ログに出す名前。</param>
    /// <param name="entry">登録されたページ（<see cref="ModuleUi.Panel"/>。字は検めてある）。null ならパネル無し。</param>
    /// <param name="webRoot">モジュールのページの置き場。</param>
    /// <param name="logger">モジュールの読み込みの logger（<see cref="ModuleManager"/> のもの）。</param>
    public static string? Panel(string moduleId, string? entry, string webRoot, ILogger logger)
    {
        if (entry is null) return null;

        var file = Path.Combine(webRoot, entry);
        if (File.Exists(file)) return entry;

        logger.LogWarning("{Id}: panel entry {File} is missing; not listing it in the sidebar", moduleId, file);
        return null;
    }

    /// <summary>
    /// VR の空間の窓のページを決める。<b>実体が無ければ「VR 専用のページ無し」に倒す</b>（空間の窓でも普段のパネルを出す）。
    /// パネルそのものが無いモジュールには付けない（サイドバーに出ないので、開く口が無い）。
    /// </summary>
    /// <param name="moduleId">ログに出す名前。</param>
    /// <param name="entry">登録された VR のページ（<see cref="ModuleUi.VrPanel"/>）。null なら無し。</param>
    /// <param name="webRoot">モジュールのページの置き場。</param>
    /// <param name="panelEntry">パネルの入口（<see cref="Panel"/> が決めたもの。null なら VR 専用のページも付けない）。</param>
    /// <param name="logger">モジュールの読み込みの logger（<see cref="ModuleManager"/> のもの）。</param>
    public static string? VrPanel(string moduleId, string? entry, string webRoot, string? panelEntry, ILogger logger)
    {
        if (panelEntry is null || entry is null) return null;

        var file = Path.Combine(webRoot, entry);
        if (File.Exists(file)) return entry;

        logger.LogWarning("{Id}: VR panel entry {File} is missing; the VR window shows the usual panel", moduleId, file);
        return null;
    }

    /// <summary>
    /// モジュールのページとして受ける字か（<c>web/</c> からの相対パスで <c>.html</c> で終わる。英数字・<c>-</c>・<c>_</c> と区切りの <c>/</c>）。
    /// 画面はこの字で <c>spoitable://&lt;Id&gt;/&lt;ページ&gt;</c> を組むので、<c>?</c>・<c>#</c>・<c>..</c>・<c>\</c> を入れさせない。
    /// パネル・VR のページ・手首のタイルのページで同じ決まり。
    /// </summary>
    public static bool IsPagePath(string? path) => path is not null && PagePath().IsMatch(path);

    [GeneratedRegex(@"^(?:[A-Za-z0-9_-]+/)*[A-Za-z0-9_-]+\.html\z")]
    private static partial Regex PagePath();
}
