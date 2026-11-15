using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Modules.Files;
using SpoitableToolKit.Host.Modules.GitHub;
using SpoitableToolKit.Host.Modules.Management.Models;
using SpoitableToolKit.Host.Modules.Packaging;

namespace SpoitableToolKit.Host.Modules.Management;

/// <summary>
/// GitHub から入れたモジュールに<b>新しいものがあるか</b>を確かめる。<b>確かめるだけ</b>で、入れるのは利用者が押したとき。
/// <list type="bullet">
///   <item>見るのは、入れたときに残した入れ元（<c>modules/&lt;Id&gt;/.source.json</c>）の repo。リリースから入れた物は「最新のリリース」のタグ、
///         ソースから Build した物は「既定の branch の先頭の commit」（ソースは落とさない）。入っているものと違えば「更新がある」
///         （commit には順番が無いので、違えば新しいとみなす）</item>
///   <item>自分から確かめるのは <see cref="Interval"/> に 1 回まで（GitHub の API は、名乗らない相手には 1 時間に 60 回まで）。
///         いつ確かめたかと、そのとき見たもの（タグと commit は別の欄）は <c>module-updates.json</c> に残す（起動をまたいで覚える）</item>
///   <item>繋がらなかった repo は前に見たもののまま。<b>1 つも確かめられなかったら「確かめた」ことにしない</b>（次の起動でまた試す）</item>
///   <item>インストールを決めた物は、その repo のいちばん新しいものとして覚え直す（<see cref="Saw"/>）。覚え直さないと、確かめたあとで入れ元が先へ進み、
///         それを「更新」で入れたとき、次に確かめるまで、前に見た物が「更新がある」と出る</item>
/// </list>
/// </summary>
internal sealed class ModuleUpdates(string userRoot, GitHubReleases github, TimeProvider clock, ILogger logger)
{
    public const string FileName = "module-updates.json";

    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly string _modules = Path.Combine(ModulePaths.Normalize(userRoot), "modules");
    private readonly string _file = Path.Combine(ModulePaths.Normalize(userRoot), FileName);
    private readonly Lock _gate = new();
    private ModuleUpdateState? _state;

    private sealed record Source(GitHubRepository Repository, string Kind, List<string> Tags)
    {
        public bool FromSource => Kind == ModuleSource.GitHubSourceKind;
    }

    /// <summary>最後に確かめた時刻（まだなら null）。</summary>
    public DateTimeOffset? CheckedAt
    {
        get
        {
            lock (_gate) return Current().CheckedAt;
        }
    }

    /// <summary>
    /// この入れ元に、入っているものと違う最新のものがあれば、それ（リリースから入れた物はタグ、ソースから Build した物は commit）。
    /// <b>それぞれ自分の相手とだけ比べる</b>（同じ repo にリリースがあっても、Build した物の commit とは比べない）。
    /// </summary>
    public string? LatestFor(ModuleSource installed)
    {
        lock (_gate)
        {
            return Seen(Current(), installed.Kind) is { } seen
                   && seen.TryGetValue(Key(installed.Repository), out var newest)
                   && !string.Equals(newest, installed.Tag, StringComparison.Ordinal)
                ? newest
                : null;
        }
    }

    /// <summary>
    /// インストールを決めた物を、その repo の<b>いちばん新しいもの</b>として覚える（入れる直前に GitHub に聞いて落とした物なので、覚えている物より古くはない）。
    /// </summary>
    public void Saw(ModuleSource installing)
    {
        ModuleUpdateState next;
        lock (_gate)
        {
            var now = Current();
            next = now with
            {
                Latest = new Dictionary<string, string>(now.Latest, StringComparer.Ordinal),
                Heads = new Dictionary<string, string>(now.Heads, StringComparer.Ordinal),
            };
            if (Seen(next, installing.Kind) is not { } seen) return;

            seen[Key(installing.Repository)] = installing.Tag;
            _state = next;
        }

        Save(next);
    }

    /// <summary>
    /// 確かめる。<paramref name="force"/> が false なら、前に確かめてから <see cref="Interval"/> 経っていなければ何もしない。
    /// </summary>
    public async Task<ModuleUpdateCheck> CheckAsync(bool force, CancellationToken cancellationToken)
    {
        ModuleUpdateState before;
        lock (_gate) before = Current();

        if (!force && before.CheckedAt is { } last && clock.GetUtcNow() - last < Interval) return new ModuleUpdateCheck([], 0, 0);

        var installed = Installed();
        var latest = new Dictionary<string, string>(before.Latest, StringComparer.Ordinal);
        var heads = new Dictionary<string, string>(before.Heads, StringComparer.Ordinal);
        List<string> found = [];
        var reached = 0;

        foreach (var source in installed)
        {
            try
            {
                // ソースから Build した物は先頭の commit（id だけを聞く。ソースは落とさない）、リリースから入れた物は最新のリリースのタグ。
                var newest = source.FromSource
                    ? await github.HeadCommitAsync(source.Repository, cancellationToken).ConfigureAwait(false)
                    : (await github.LatestAsync(source.Repository, cancellationToken).ConfigureAwait(false)).Tag;
                reached++;

                var key = Key(source.Repository.ToString());
                var known = Seen(before, source.Kind)!.TryGetValue(key, out var seen) && string.Equals(seen, newest, StringComparison.Ordinal);
                (source.FromSource ? heads : latest)[key] = newest;
                if (!known && source.Tags.Any(tag => !string.Equals(tag, newest, StringComparison.Ordinal)))
                    found.Add(source.FromSource ? $"{source.Repository} commit {newest[..7]}" : $"{source.Repository} {newest}");
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // 繋がらない・時間切れ・リリースが無い・読めない答え。**どの失敗でも**、この repo は「答えが返らなかった」として
                // 前に見たもののままにし、次へ進む（1 つの入れ元の失敗で、ほかの入れ元の確認まで止めない）。
                logger.LogWarning("Could not check {Repository} for a newer {What}: {Reason}",
                    source.Repository, source.FromSource ? "commit" : "release", ex.Message);
            }
        }

        // もう入っていない repo の分は捨てる。**欄ごと**（ソースから Build した物だけの repo の分は、タグの欄に残さない。逆も同じ）。
        Forget(latest, installed, ModuleSource.GitHubKind);
        Forget(heads, installed, ModuleSource.GitHubSourceKind);

        // 入っているのに 1 つも確かめられなかった（繋がっていない）なら、「確かめた」ことにしない。
        var next = new ModuleUpdateState(installed.Count == 0 || reached > 0 ? clock.GetUtcNow() : before.CheckedAt, latest, heads);
        lock (_gate) _state = next;
        Save(next);

        if (reached > 0) logger.LogInformation("Checked {Count} module source(s) for updates; {Found} new", reached, found.Count);
        return new ModuleUpdateCheck(found, installed.Count, reached);
    }

    /// <summary>その Kind の物と比べる欄（GitHub から入れた物でなければ null）。</summary>
    private static Dictionary<string, string>? Seen(ModuleUpdateState state, string kind) => kind switch
    {
        ModuleSource.GitHubKind => state.Latest,
        ModuleSource.GitHubSourceKind => state.Heads,
        _ => null,
    };

    private static void Forget(Dictionary<string, string> seen, List<Source> installed, string kind)
    {
        foreach (var stale in seen.Keys.Where(key => !installed.Any(i => i.Kind == kind && Key(i.Repository.ToString()) == key)).ToArray())
            seen.Remove(stale);
    }

    /// <summary>入っているものの入れ元（repo と Kind ごとに、入っているタグ・commit）。ユーザー側の置き場だけ。</summary>
    private List<Source> Installed()
    {
        List<Source> installed = [];
        if (!Directory.Exists(_modules)) return installed;

        // 作業場（.pending など）は直下に入れ元を持たないので、ここでは分けない（入れかけの分は、入ってから数える）。
        foreach (var directory in Directory.GetDirectories(_modules))
        {
            if (ModuleSource.ReadFrom(directory) is not { Kind: ModuleSource.GitHubKind or ModuleSource.GitHubSourceKind } source) continue;
            if (GitHubRepository.TryParse($"https://github.com/{source.Repository}", out _) is not { } repository) continue;

            var entry = installed.FirstOrDefault(i => i.Kind == source.Kind && Key(i.Repository.ToString()) == Key(repository.ToString()));
            if (entry is null) installed.Add(new Source(repository, source.Kind, [source.Tag]));
            else entry.Tags.Add(source.Tag);
        }

        return installed;
    }

    private static string Key(string repository) => repository.ToLowerInvariant();

    private ModuleUpdateState Current() => _state ??= Load();

    private ModuleUpdateState Load()
    {
        try
        {
            // 古い形のファイルには heads が無い（null で返る）。
            if (File.Exists(_file) && JsonSerializer.Deserialize<ModuleUpdateState>(File.ReadAllText(_file), Json) is { } saved)
                return new ModuleUpdateState(saved.CheckedAt,
                    new Dictionary<string, string>(saved.Latest ?? [], StringComparer.Ordinal),
                    new Dictionary<string, string>(saved.Heads ?? [], StringComparer.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read {File}; starting over", _file);
        }

        return new ModuleUpdateState(null, new Dictionary<string, string>(StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private void Save(ModuleUpdateState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(state, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not write {File}", _file);
        }
    }
}
