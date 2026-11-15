namespace SpoitableToolKit.Host.App;

internal static class AppPaths
{
    /// <summary>
    /// %LOCALAPPDATA%\SpoitableToolKit。
    /// <c>SPOITABLE_DATA_DIR</c> で差し替えられる（ポータブル運用・動作確認用）。
    /// </summary>
    public static string DataDirectory { get; } = EnsureDirectory(
        Environment.GetEnvironmentVariable("SPOITABLE_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SpoitableToolKit"));

    /// <summary>CEF のキャッシュ。プロセス間で共有されるので固定パスであること。</summary>
    public static string CefCacheDirectory { get; } = EnsureDirectory(Path.Combine(DataDirectory, "cef"));

    /// <summary>
    /// exe と一緒に配られるモジュールの探索ルート。
    /// ここは Program Files に入りうる（＝書き込めない）ので、ユーザーが後から入れるものは
    /// <see cref="DataDirectory"/> 側の同じ構造（modules/ と shared/）に置く。
    /// </summary>
    public static string BundledRoot { get; } = AppContext.BaseDirectory;

    /// <summary>
    /// モジュールの探索ルート。ユーザー側を先に見る（同じ名前は先勝ち＝ユーザー側が勝つ）。
    /// <para>
    /// 同梱版を差し替えたいときに、ユーザーが書ける場所へ置くだけで済むようにするため。
    /// 無効化リスト（本体の設定 <c>DisabledModules</c>）は Id 単位なので両方に効く。
    /// 「その Id を止める」と「同梱版をユーザー版で上書きする」を別の操作にしておく。
    /// 影になった方は警告に出す（更新して同梱版が新しくなっても、古いユーザー版が黙って勝ち続けるのを防ぐ）。
    /// </para>
    /// SPOITABLE_DATA_DIR を exe の隣に向けたときに二重に見ないよう、重複は畳む。
    /// </summary>
    public static IReadOnlyList<string> ModuleRoots { get; } =
        new[] { DataDirectory, BundledRoot }
            .Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// ソフトウェア自身のログ。起動ごとに 1 本で、古いものは間引かれる。
    /// <para>
    /// <c>vrclog.db</c>（VRChat のログ）とは別建て。持ち主も寿命も消し方も違うものを同居させない。
    /// </para>
    /// </summary>
    public static string AppLogDirectory { get; } = EnsureDirectory(Path.Combine(DataDirectory, "applog"));

    /// <summary>yt-dlp / deno など外部バイナリの置き場。</summary>
    public static string ToolsDirectory { get; } = EnsureDirectory(Path.Combine(DataDirectory, "tools"));

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
