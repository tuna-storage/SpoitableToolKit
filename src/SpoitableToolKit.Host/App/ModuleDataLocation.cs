using Microsoft.Extensions.Logging;

namespace SpoitableToolKit.Host.App;

/// <summary>
/// モジュールの作業領域の親（<c>module-data/</c>）。<c>module-data/&lt;Id&gt;/</c> が
/// <see cref="Abstractions.IModuleHost.DataDirectory"/> になる。
/// <para>
/// 探索ルートの <c>modules/</c> とは別の場所にする。同じにすると、
/// モジュールが書いたファイルを次回の起動で読み込もうとする羽目になる。
/// </para>
/// <para>
/// 古い名前 <c>plugin-data/</c> だけがあれば、起動の頭で 1 回だけ新しい名前へ移す
/// （心拍の記録・各モジュールの設定が入っている。捨てない）。移せなかったら、その回は古い置き場をそのまま使い、次の起動でまた移す
/// （新しい空の置き場で始めると、心拍は「最初に見つかった心拍計」に繋ぎに行き、設定も既定に戻る）。
/// </para>
/// <para>
/// モジュールを読む前、単一起動の Mutex を取ったあとに呼ぶ。起動し直しのとき、前のアプリが畳み終わる前に呼ぶと、
/// 前のアプリが開いている記録のせいで移せない。
/// </para>
/// </summary>
internal static class ModuleDataLocation
{
    public const string FolderName = "module-data";

    public const string OldFolderName = "plugin-data";

    /// <summary>使う置き場を決める（要れば古い置き場を移す）。返す置き場はある（無ければ作る）。</summary>
    /// <param name="dataDirectory">アプリの置き場（<see cref="AppPaths.DataDirectory"/>）。</param>
    public static string Resolve(string dataDirectory, ILogger logger)
    {
        var current = Path.Combine(dataDirectory, FolderName);
        var old = Path.Combine(dataDirectory, OldFolderName);

        if (Directory.Exists(old))
        {
            try
            {
                // 新しい方にファイルが 1 つも無ければ（空のフォルダだけ。先に作られただけ）、まだ何も書かれていない。消して、古い方を移す
                // （空の module-data/hrs/ が「中身がある」に見えると、hrs が設定の無い所から始まり、最初に見つかった心拍計に繋ぎに行く）。
                if (Directory.Exists(current) && !Directory.EnumerateFiles(current, "*", SearchOption.AllDirectories).Any())
                    Directory.Delete(current, recursive: true);

                if (Directory.Exists(current))
                {
                    logger.LogWarning(
                        "Both {Old} and {Current} have files; using the new one and leaving the old one as it is (move what is still needed by hand)",
                        old, current);
                }
                else
                {
                    Directory.Move(old, current);
                    logger.LogInformation("Moved the modules' data from {Old} to {Current}", old, current);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not move {Old} to {Current}; using the old one this time and trying again at the next start", old, current);
                return old;
            }
        }

        Directory.CreateDirectory(current);
        return current;
    }
}
