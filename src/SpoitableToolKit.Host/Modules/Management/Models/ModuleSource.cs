using System.Text.Json;

namespace SpoitableToolKit.Host.Modules.Management.Models;

/// <summary>どこから入れたか（<c>modules/&lt;Id&gt;/.source.json</c>）。更新を確かめるのと、管理画面に出すのに使う。</summary>
internal sealed record ModuleSource(string Kind, string Repository, string Tag, string Asset, string Sha256, DateTimeOffset InstalledAt)
{
    public const string FileName = ".source.json";

    public const string GitHubKind = "github";

    /// <summary>
    /// <see cref="Kind"/>: GitHub の repo のソースを、この PC で build して入れた。<see cref="Tag"/> の欄に commit の id（40 桁）、
    /// <see cref="Asset"/> は空、<see cref="Sha256"/> は落としたソースの zip のもの。
    /// </summary>
    public const string GitHubSourceKind = "github-source";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public void WriteTo(string directory) => File.WriteAllText(Path.Combine(directory, FileName), JsonSerializer.Serialize(this, Json));

    public static ModuleSource? ReadFrom(string directory)
    {
        try
        {
            var file = Path.Combine(directory, FileName);
            if (!File.Exists(file)) return null;

            // 手で書き換えられたものも読む。入れ元とタグが無ければ、入れ元は分からないものとして扱う（null を持ったまま回さない）。
            return JsonSerializer.Deserialize<ModuleSource>(File.ReadAllText(file), Json) is
                { Kind.Length: > 0, Repository.Length: > 0, Tag.Length: > 0 } source
                ? source with { Asset = source.Asset ?? string.Empty, Sha256 = source.Sha256 ?? string.Empty }
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
