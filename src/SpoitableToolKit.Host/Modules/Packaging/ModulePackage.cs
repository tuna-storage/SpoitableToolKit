using System.IO.Compression;
using SpoitableToolKit.Host.Modules.Files;

namespace SpoitableToolKit.Host.Modules.Packaging;

/// <summary>
/// 落としてきた zip を<b>仮置きの場所</b>へ展開する。外から来たものなので、展開の前に全部の項目を確かめる。
/// <list type="bullet">
///   <item>項目のパスが仮置きの外を指したら止める（<c>..</c>・絶対パス。zip-slip）</item>
///   <item>項目の数と、展開したあとの大きさの合計に上限（zip bomb）。合計は項目が名乗る大きさで数える。
///         名乗りより大きく膨らむ項目を作っても、.NET は<b>名乗った大きさで読むのをやめる</b>ので、書かれる量は名乗りを超えない
///         （.NET 10 で確かめた。試験 <c>AnEntryIsNeverWrittenBeyondWhatItDeclares</c> が見張る）</item>
///   <item><c>module.json</c> は一番上か、一番上のフォルダ 1 つの中。そこをモジュールのフォルダとして返す</item>
/// </list>
/// </summary>
internal static class ModulePackage
{
    public const int MaxEntries = 5000;
    public const long MaxExpandedBytes = 512L * 1024 * 1024;

    /// <summary>ソース（repo の zip）の項目の数の上限。モジュールの zip より多い（試験・資料・画像が入る）。</summary>
    public const int MaxSourceEntries = 20000;

    /// <summary>断る文での呼び名: モジュールの zip。</summary>
    public const string Package = "package";

    /// <summary>断る文での呼び名: repo のソースの zip。</summary>
    public const string Source = "source";

    /// <returns>展開したモジュールのフォルダ（<paramref name="staging"/> の中）と宣言。</returns>
    public static (string Directory, ModuleManifest Manifest) Extract(Stream zip, string staging, long maxExpandedBytes = MaxExpandedBytes)
    {
        var root = Expand(zip, staging, MaxEntries, maxExpandedBytes, Package);

        var directory = FindModuleDirectory(root);
        var file = Path.Combine(directory, ModuleManifest.FileName);
        if (new FileInfo(file).Length > ModuleManifest.MaxBytes)
            throw new InvalidDataException(
                $"{ModuleManifest.FileName} is larger than {ModuleManifest.MaxBytes / 1024} KB; it only holds the module's id, name, version and sdk");

        var manifest = ModuleManifest.Parse(File.ReadAllText(file));
        return (directory, manifest);
    }

    /// <summary>
    /// zip を<b>丸ごと</b> <paramref name="directory"/> に展開する（モジュールの zip も、repo のソースの zip も、ここを通る）。
    /// 外を指す項目・項目の数・展開したあとの大きさの決まりは同じ。<paramref name="what"/> は断る文での呼び名（<see cref="Package"/> か <see cref="Source"/>）。
    /// </summary>
    /// <returns>展開した先（正規化した <paramref name="directory"/>）。</returns>
    public static string Expand(Stream zip, string directory, int maxEntries, long maxExpandedBytes, string what)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);

        if (archive.Entries.Count > maxEntries)
            throw new InvalidDataException($"The {what} has {archive.Entries.Count} entries; at most {maxEntries} are accepted");

        var root = ModulePaths.Normalize(directory);
        var remaining = maxExpandedBytes;
        foreach (var entry in archive.Entries)
        {
            if (!Fits(entry.Length, ref remaining))
                throw new InvalidDataException($"The {what} expands to more than {maxExpandedBytes / 1024 / 1024} MB ({maxExpandedBytes} bytes)");

            Target(root, entry.FullName, what);
        }

        Directory.CreateDirectory(root);
        foreach (var entry in archive.Entries)
        {
            var target = Target(root, entry.FullName, what);
            try
            {
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                // 同じ名前が 2 つ（大文字小文字だけ違う名前は、Windows では同じ）・ファイルとフォルダが同じ名前・Windows で使えない名前（? * など）。
                // ほかの OS で作った repo にはよくある。.NET の文には、作った側が好きに書ける名前の入った path がそのまま入るので、こちらの文にする。
                throw new InvalidDataException(
                    $"The {what} has an entry that cannot be written on this PC: {ModuleManifest.Shown(entry.FullName)} " +
                    "(a name Windows does not allow, the same name twice — names that differ only in upper and lower case are the same here — " +
                    "or a file and a folder with one name)");
            }
        }

        return root;
    }

    /// <summary>
    /// 項目 1 つ分を、残りの予算から引く。入らなければ false。<b>足して上限と比べない</b> — 項目が名乗る大きさは、zip64 なら long の上限まで書ける。
    /// 足すと桁あふれして負になり、上限をすり抜ける（確認の板より前に disk を埋められる）。
    /// </summary>
    internal static bool Fits(long length, ref long remaining)
    {
        if (length < 0 || length > remaining) return false;

        remaining -= length;
        return true;
    }

    /// <summary>
    /// 項目の行き先。<b>仮置きの中でなければ投げる</b>。断る文に入れる項目の名前は、zip を作った側が好きに書ける字なので絞る
    /// （<see cref="ModuleManifest.Shown"/>）。
    /// </summary>
    private static string Target(string root, string entryName, string what)
    {
        if (string.IsNullOrEmpty(entryName) || Path.IsPathRooted(entryName) || entryName.Contains(':'))
            throw new InvalidDataException($"The {what} has an entry with an absolute path: {ModuleManifest.Shown(entryName ?? string.Empty)}");

        string target;
        try
        {
            target = Path.GetFullPath(Path.Combine(root, entryName));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // 道として読めない名前（NUL が入っている・長すぎる）。
            throw new InvalidDataException($"The {what} has an entry whose name is not a usable path: {ModuleManifest.Shown(entryName)}");
        }

        if (!ModulePaths.IsUnder(target, root))
            throw new InvalidDataException($"The {what} has an entry that points outside its folder: {ModuleManifest.Shown(entryName)}");

        return target;
    }

    private static string FindModuleDirectory(string root)
    {
        if (File.Exists(Path.Combine(root, ModuleManifest.FileName))) return root;

        var folders = Directory.GetDirectories(root);
        if (folders.Length == 1 && Directory.GetFiles(root).Length == 0 && File.Exists(Path.Combine(folders[0], ModuleManifest.FileName)))
            return folders[0];

        throw new InvalidDataException($"The package has no {ModuleManifest.FileName} at its top (or inside its only top folder)");
    }
}
