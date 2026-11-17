using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Events.Localization;

namespace SpoitableToolKit.Host.Localization;

/// <summary>
/// 画面の文言を辞書から引く。
/// <para>
/// 辞書は <c>locales/&lt;言語&gt;.json</c> の平らな key-value。ホストの分と、モジュールが
/// 同梱する分を<b>1 つに合成する</b>。<b>モジュールのキーは <c>&lt;Id&gt;.…</c> で始まるものだけ入れる</b>
/// （<see cref="LocaleSource.Owns"/>）ので、モジュールが本体やほかのモジュールの文言を足したり書き換えたりはできない。
/// それでも重なったら（Id に <c>.</c> が入る場合）<b>先に読んだ方を残して警告</b>する。
/// </para>
/// <para>
/// <b>見つからないキーはそのまま返す。</b> 既定の言語へ落とすと画面は読めるが、
/// 訳の抜けに誰も気付かない。代わりに、抜けたキーは<b>起動後に 1 回だけ</b>
/// まとめてログへ出す。
/// </para>
/// </summary>
internal sealed class Localizer(ILogger<Localizer> logger) : ILocalizer
{
    private readonly EventHub _reloaded = EventHubs.HostOnly<ILanguageEventListener>(logger);

    /// <summary>用意してある言語。ここに無い OS の言語は <see cref="Fallback"/> になる。</summary>
    private static readonly string[] Supported = ["ja", "en"];

    private const string Fallback = "en";

    /// <summary>
    /// いまの辞書。<b>読み直すときは作り直してから丸ごと差し替える</b>（空にしてから足し直すと、その間にほかのスレッドが引いた文言が
    /// キーのまま出る。VRChat の起動・終了の通知が、起動直後の読み直しと重なるときなど）。
    /// </summary>
    private volatile ConcurrentDictionary<string, string> _strings = [];

    /// <summary>引かれたのに無かったキー。<b>まとめて 1 回だけ</b>出すために溜める。</summary>
    private readonly ConcurrentDictionary<string, byte> _missing = [];

    /// <summary>持ち主でないキーを知らせ終わった辞書（同じ辞書を、足すときと読み直すときの 2 回読むので）。</summary>
    private readonly ConcurrentDictionary<string, byte> _reported = new(StringComparer.OrdinalIgnoreCase);

    public string Language { get; private set; } = Fallback;

    public string this[string key] => _strings.TryGetValue(key, out var text) ? text : Miss(key);

    public string Format(string key, params object?[] arguments)
    {
        var text = this[key];

        try
        {
            return string.Format(CultureInfo.CurrentCulture, text, arguments);
        }
        catch (FormatException ex)
        {
            // 翻訳側の書き間違い（{0} の数が合わない等）で画面が消えるのは割に合わない。
            logger.LogWarning(ex, "Failed to format {Key}", key);
            return text;
        }
    }

    /// <summary>
    /// 言語を決めて読み込み直す。<c>null</c> / <c>auto</c> なら OS の表示言語に合わせる。
    /// <para>
    /// <b>設定を変えたらこれを呼ぶ。</b> 画面は開くたびに文言を引き直すので、これだけで切り替わる
    /// （トレイのメニューも開くたびに組み立て直すので、次に開いたときから）。
    /// </para>
    /// </summary>
    public void Load(string? requested, IEnumerable<LocaleSource> sources)
    {
        Language = Resolve(requested);
        _missing.Clear();

        ConcurrentDictionary<string, string> fresh = [];
        foreach (var source in sources) LoadFrom(source, fresh);
        _strings = fresh;

        logger.LogInformation("UI language is {Language} ({Count} strings)", Language, fresh.Count);

        var reloaded = new LanguageLoadedEventParams(Language);
        _reloaded.Fire<ILanguageEventListener>(l => l.OnLanguageLoaded(reloaded));
    }

    /// <summary>
    /// 読み直した（<see cref="Load"/> のたび。<see cref="ILanguageEventListener"/>）。<b>C# の側で文字にして持っているもの</b>（状態の口の「ログを読んでいます」・人数など）は、
    /// 画面と違って引き直されないので、これを聞いて書き直す。足せるのはアプリ自身だけ。読み直したスレッドから呼ぶ。
    /// </summary>
    public IEventSource Reloaded => _reloaded.Source;

    /// <summary>画面へまとめて渡すための写し。JS 側は 1 回受け取って自分で引く。</summary>
    public IReadOnlyDictionary<string, string> Snapshot() => _strings.ToDictionary();

    /// <summary>
    /// 引かれたのに無かったキーを 1 回だけ知らせる。<b>翻訳の抜けを見つける唯一の手段</b>なので、
    /// 画面を一通り開いたあとに見ると効く。
    /// </summary>
    public void ReportMissing()
    {
        if (_missing.IsEmpty) return;

        logger.LogWarning("{Count} key(s) have no translation: {Keys}",
            _missing.Count, string.Join(", ", _missing.Keys.Order()));
    }

    private string Miss(string key)
    {
        _missing.TryAdd(key, 0);
        return key;
    }

    private static string Resolve(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested) && !requested.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return Supported.Contains(requested, StringComparer.OrdinalIgnoreCase) ? requested.ToLowerInvariant() : Fallback;

        var os = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        return Supported.Contains(os, StringComparer.OrdinalIgnoreCase) ? os.ToLowerInvariant() : Fallback;
    }

    /// <summary>
    /// 1 つの辞書を<b>足す</b>（今の言語で。読み直しはしない）。
    /// <para>
    /// モジュールの <c>OnInit</c> より前に呼ぶ。モジュールの辞書をまとめて読むのは全部を読み込んだ後なので、
    /// 呼ばないと、それまでに引いた文言（<c>OnInit</c> で状態の口に書いた字など）が<b>キーのまま</b>残る。
    /// </para>
    /// </summary>
    public void Merge(LocaleSource source) => LoadFrom(source, _strings);

    /// <summary>
    /// 辞書を 1 つ読んで入れる。<b>足すとき（<see cref="Merge"/>）も読み直すとき（<see cref="Load"/>）もここを通る</b> —
    /// 持ち主の検査を片方にだけ置くと、読み直しで外したキーが戻る。
    /// </summary>
    private void LoadFrom(LocaleSource source, ConcurrentDictionary<string, string> into)
    {
        var path = Path.Combine(source.Directory, "locales", $"{Language}.json");

        if (!File.Exists(path)) return;

        try
        {
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (entries is null) return;

            List<string> foreign = [];
            foreach (var (key, text) in entries)
            {
                if (!source.Owns(key))
                {
                    foreign.Add(key);
                    continue;
                }

                if (!into.TryAdd(key, text))
                    logger.LogWarning("{Key} in {Path} already exists; keeping the one loaded first", key, path);
            }

            if (foreign.Count > 0 && _reported.TryAdd(path, 0))
                logger.LogWarning("{Message}", Foreign(source.Owner!, path, foreign));
        }
        catch (Exception ex)
        {
            // 辞書が壊れていても起動は続ける。文言がキーのまま出るだけで、機能は動く。
            logger.LogWarning(ex, "Could not read {Path}", path);
        }
    }

    /// <summary>
    /// 持ち主でないキーを入れなかったときの文。<b>読んだ側（モジュールを書くのは AI）がそのまま直せる形</b>にする:
    /// どのキーを・どう直すか。本体のキー（<c>host.…</c>・<c>common.…</c>）は直し方が違うので言い分ける。
    /// </summary>
    internal static string Foreign(string owner, string path, IReadOnlyList<string> keys)
    {
        var app = keys.Where(IsAppKey).ToArray();
        var loose = keys.Where(key => !IsAppKey(key)).ToArray();

        var text = $"{keys.Count} key(s) in {path} were left out because they do not start with '{owner}.': {string.Join(", ", keys)}. " +
                   $"A module's translation keys start with the module's Id and a dot.";

        if (loose.Length > 0)
            text += $" Rename '{loose[0]}' to '{owner}.{loose[0]}' in every language file, and look it up under the new name.";

        if (app.Length > 0)
            text += " Keys under 'host.' and 'common.' belong to the app, so a module can neither add nor replace them: " +
                    "remove them from the module's files. The shared phrases (Close, Copy, Back…) are already there — " +
                    "look them up as they are (CommonPhrases.Ui.Close in C#, 'common.ui.close' in a panel), " +
                    $"and keep any other wording under '{owner}.'.";

        return text;
    }

    private static bool IsAppKey(string key) =>
        key.StartsWith("host.", StringComparison.Ordinal) || key.StartsWith("common.", StringComparison.Ordinal);
}
