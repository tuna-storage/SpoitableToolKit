using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SpoitableToolKit.Host.Theming;

/// <summary>
/// テーマ（配色）の置き場と配り方。<b>1 ディレクトリ = 1 テーマ</b>で、
/// <c>%LOCALAPPDATA%\SpoitableToolKit\themes\&lt;名前&gt;\theme.css</c> に置く。
/// <para>
/// <b>同梱のテーマも同じ形で置く。</b> 既定だけを特別扱いすると、真似して作れない —
/// 「ディレクトリごとコピーして名前を変える」が新しいテーマの作り方になる。
/// </para>
/// </summary>
internal sealed class HostThemes(string dataDirectory, ILogger logger)
{
    /// <summary>何も選ばれていないときのテーマ。</summary>
    public const string Default = "dark";

    /// <summary>アセンブリに埋め込んで配るテーマ。<b>この名前のディレクトリだけを管理する。</b></summary>
    private static readonly string[] Bundled = [Default, "light"];

    private const string FileName = "theme.css";

    /// <summary>
    /// 同梱テーマの先頭に書く刻印。<b>中身のハッシュ</b>で、更新の判定に使う。
    /// <para>
    /// 番号を手で書く形にしない。<b>上げ忘れると「直したのに配られない」が起きる</b>し、
    /// それは後から追いかけるのが難しい種類の不具合になる。中身から出せば忘れようがない。
    /// </para>
    /// </summary>
    private const string StampPrefix = "/* stk-theme: bundled ";

    private string Root => Path.Combine(dataDirectory, "themes");

    /// <summary>
    /// 同梱テーマを置き直す。<b>刻印が今配るものと違うときだけ</b>書き換える。
    /// <para>
    /// 毎起動で無条件に上書きすると、<b>同梱テーマを直接いじって試した人の変更が、
    /// 再起動のたびに黙って消える</b>。テーマを作る人は、コピーする前にまず既定を
    /// 直接いじって様子を見るので、そこを踏み抜く。
    /// </para>
    /// <para>
    /// 逆に 1 回だけ展開する形にすると、<b>更新しても古い同梱テーマが残る</b>。
    /// 刻印はその両方を避けるためにある。
    /// </para>
    /// <para>
    /// <b>見るのは同梱の名前のディレクトリだけ。</b> 利用者が作ったテーマは名前が違うので、
    /// ここを通らない。
    /// </para>
    /// </summary>
    public void Ensure()
    {
        foreach (var name in Bundled)
        {
            var content = Embedded(name);
            var stamp = Stamp(content);
            var file = Path.Combine(Root, name, FileName);

            try
            {
                if (File.Exists(file) && StampOf(file) == stamp) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, $"{StampPrefix}{stamp} */{Environment.NewLine}{content}");
                logger.LogInformation("Wrote bundled theme: {Name} ({Stamp})", name, stamp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 置けなくても止めない。配信は埋め込みへ落ちるので、画面は無スタイルにならない。
                logger.LogWarning(ex, "Could not write bundled theme: {Name}", name);
            }
        }
    }

    /// <summary>
    /// 設定画面に出す候補。同梱を先に、そのあと利用者が作ったものを名前順で。
    /// <b>候補であって選択肢ではない</b>ので、ここに無い名前も打てる（打った先が無ければ既定に落ちる）。
    /// </summary>
    public IReadOnlyList<string> Names()
    {
        List<string> names = [.. Bundled];

        try
        {
            names.AddRange(Directory.EnumerateDirectories(Root)
                .Where(d => File.Exists(Path.Combine(d, FileName)))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(n => !Bundled.Contains(n, StringComparer.OrdinalIgnoreCase))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not enumerate themes/");
        }

        return names;
    }

    /// <summary>
    /// 配る CSS。<b>読めないときは必ず埋め込みの既定へ落とす</b> —
    /// ここで空を返すと、変数が未定義になって<b>色が全部消えた画面</b>が出る
    /// （エラーは出ないので、見た人は理由に辿り着けない）。
    /// </summary>
    public string Read(string? name)
    {
        var wanted = string.IsNullOrWhiteSpace(name) ? Default : name.Trim();

        // ディレクトリ名として通らないものは、そもそも探しに行かない
        // （設定は手で書けるので、`..\..\` のようなものが来うる）。
        if (wanted.AsSpan().ContainsAny(Path.GetInvalidFileNameChars()))
        {
            logger.LogWarning("Theme name is not usable: {Name}; serving {Default}", wanted, Default);
            return Fallback(wanted);
        }

        var file = Path.Combine(Root, wanted, FileName);

        try
        {
            if (File.Exists(file)) return File.ReadAllText(file);

            // 消されたテーマが選ばれたまま、はふつうに起きる（ディレクトリを消しただけ）。
            logger.LogWarning("Theme {Name} not found; serving {Default}", wanted, Default);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read theme {Name}; serving {Default}", wanted, Default);
        }

        return Fallback(wanted);
    }

    /// <summary>
    /// 読めなかったときの落ちどころ。<b>「既定を選んだのと同じ結果」にする</b> —
    /// 置いてある既定を人が編集していれば、その編集が出る方が筋が通る
    /// （落ちた瞬間だけ別の見た目になる方が驚く）。
    /// <para>
    /// 既定そのものが読めないときだけ埋め込みへ。<b>ここは絶対に空を返さない</b>。
    /// </para>
    /// </summary>
    private string Fallback(string wanted) =>
        string.Equals(wanted, Default, StringComparison.OrdinalIgnoreCase)
            ? Embedded(Default)
            : Read(Default);

    /// <summary>
    /// 埋め込んだ同梱テーマ。<b>無ければ起動を止める</b> — 落ちどころそのものが欠けている状態で、
    /// 黙って先へ進むと「色が消えた画面」の理由がどこにも残らない。
    /// </summary>
    private static string Embedded(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = $"SpoitableToolKit.Host.themes.{name}.{FileName}";

        using var stream = assembly.GetManifestResourceStream(resource)
                           ?? throw new InvalidOperationException($"Bundled theme is not embedded: {resource}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// いま配る CSS の版。<b>ページの中の URL に差し込んで、キャッシュを外すために使う</b>。
    /// <para>
    /// ヘッダ（<c>Cache-Control</c>）でも狙えるが、テーマを変えてもパネルだけ前の配色のままになるなど、<b>効かなかった</b>。
    /// URL が変われば、キャッシュの実装がどうであれ取り直しになる。
    /// </para>
    /// </summary>
    public string Version(string? name) => Stamp(Read(name));

    /// <summary>中身から出す刻印。短くて目で追えればよいので先頭 8 桁。</summary>
    private static string Stamp(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..8];

    /// <summary>置いてあるファイルの刻印。無ければ null（＝こちらが置いたものではない）。</summary>
    private static string? StampOf(string file)
    {
        try
        {
            using var reader = new StreamReader(file);

            return reader.ReadLine() is { } head && head.StartsWith(StampPrefix, StringComparison.Ordinal)
                ? head[StampPrefix.Length..].Split(' ')[0]
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
