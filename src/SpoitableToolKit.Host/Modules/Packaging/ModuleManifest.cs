using System.Text.Json;
using System.Text.RegularExpressions;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Host.Modules.Loading.Models;

namespace SpoitableToolKit.Host.Modules.Packaging;

/// <summary>
/// モジュールの宣言（<c>module.json</c>）。zip の一番上（または一番上のフォルダ 1 つの中）に置く。<b>SDK の build が作る</b>。
/// <para>
/// <b>入れる前に Id を知るため</b>にある（フォルダの名前 = Id）。依存は書かない（dll の参照から読む）。
/// </para>
/// <para>
/// <paramref name="Sdk"/> は作ったときの SDK の版（<c>"sdk"</c>）。本体のものより新しければ、<b>入れる前に</b>「アプリを更新して」と断れる
/// （<see cref="NewerSdkProblem"/>）。無くても受ける — 入ったあとの読み込みで、dll の参照の版から同じことが分かる
/// （<see cref="DependencyProblemKind.HostTooOld"/>）。
/// </para>
/// </summary>
internal sealed record ModuleManifest(string Id, string Name, string Version, string? Sdk = null)
{
    public const string FileName = "module.json";

    /// <summary>
    /// <c>module.json</c> の大きさの上限。入っているのは Id・名前・版・SDK の版だけ。
    /// 外から来たファイルなので、読む前に大きさを見る（中の字は確認の画面と失敗の文に出る）。
    /// </summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary><c>"sdk"</c> の長さの上限。版（<c>2026.11.0-beta.1+abc</c>）にこれより長いものは無い。</summary>
    public const int MaxSdkLength = 64;

    /// <summary><c>"name"</c> の長さの上限。確認の板と一覧にそのまま出る。build の決まり（STK016）と同じ数。</summary>
    public const int MaxNameLength = 128;

    /// <summary>
    /// 版に使える字（<c>"version"</c> と <c>"sdk"</c>）。build の決まり（STK015）と同じ: 英数字と <c>.</c> <c>-</c> <c>+</c>、64 字まで。
    /// 確認の板・一覧・ログにそのまま出るので、改行や、字の向きを変える字を通さない。
    /// </summary>
    private static readonly Regex VersionShape = new($@"^[0-9A-Za-z.+-]{{1,{MaxSdkLength}}}\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// 名前に入れられない字: 制御文字・<b>書式の字</b>（字の向きを変える U+202E など・幅の無い字）・行と段落の区切り。
    /// build の決まり（STK014 の <c>SpoitableNameForbiddenPattern</c>）と同じ式で、試験が突き合わせる。
    /// <para>
    /// 書式の字は見えないのに、後ろの字の並びを変える。確認の板は「名前・版・?」を 1 行に並べるので、名前に U+202E があると版が違って読める。
    /// 塞ぐのは BMP の字（向きを変える字は全部ここに入る）。見た目が同じ別の字（а と a）までは見ない —
    /// どこから入れるかは、板に出る入れ元（repo・tag・ファイル名）で見分ける。
    /// </para>
    /// </summary>
    internal const string NameForbiddenPattern = @"[\p{Cc}\p{Cf}\p{Zl}\p{Zp}]";

    private static readonly Regex ControlCharacter = new(NameForbiddenPattern, RegexOptions.CultureInvariant);

    /// <summary>この本体が持っている SDK の版（<c>SpoitableToolKit.Abstractions</c> の版）。</summary>
    public static Version AppSdk { get; } = Normalize(typeof(ISpoitableModule).Assembly.GetName().Version ?? new Version(0, 0));

    /// <summary>
    /// この本体が読める、<b>いちばん古い SDK の版</b>。それより古い SDK で作ったものは、読み込むと型の例外で落ち、何を直せばいいか分からない
    /// （SDK の形を壊した — 型を消した・変えた — ときに起きる）ので、入れる前（<see cref="OlderSdkProblem"/>）と読み込む前
    /// （<see cref="DependencyProblemKind.SdkTooOld"/>）に、分かる文で断る。
    /// <para>
    /// <b>上げるのは SDK の形を壊したときだけ</b>で、本体の版（<see cref="AppSdk"/>）も一緒に上げる（版は commit ごとには変わらないので、
    /// 上げないと壊す前と後に作ったものを見分けられない）。今は本体の版と同じ — 外に出た SDK がまだ無い。
    /// </para>
    /// </summary>
    public static Version MinimumSdk { get; } = Normalize(new Version(2026, 11, 0));

    /// <summary>
    /// Id に使える字。フォルダ名と <c>spoitable://&lt;Id&gt;/</c> に入るので絞る。<b>頭も終わりも英数字</b>
    /// （Windows はフォルダ名の終わりの <c>.</c> を黙って落とすので、<c>x.</c> は別のモジュール <c>x</c> のフォルダになる）。
    /// 終わりは <c>\z</c> で見る（<c>$</c> は最後の改行の手前でも合う）。
    /// </summary>
    private static readonly Regex IdPattern = new(@"^[a-z0-9]([a-z0-9._-]{0,62}[a-z0-9])?\z", RegexOptions.CultureInvariant);

    /// <summary>Windows が装置として扱う名前（<c>con</c>・<c>nul.x</c> など）。フォルダにできない。</summary>
    private static readonly Regex DeviceName = new(@"^(con|prn|aux|nul|com[0-9]|lpt[0-9])(\.|\z)", RegexOptions.CultureInvariant);

    /// <summary>
    /// 本体が自分のために使っている名前。設定の置き場・翻訳キーの頭（<c>host.…</c>・<c>common.…</c>）・<c>spoitable://host/</c> に出るので、
    /// モジュールの Id にはできない（<c>host.x</c> のように、その名前と <c>.</c> で始まる Id も）。
    /// </summary>
    private static readonly string[] ReservedIds = ["host", "common"];

    public static bool IsValidId(string id) =>
        IdPattern.IsMatch(id) && !id.Contains("..", StringComparison.Ordinal) && !DeviceName.IsMatch(id) && !IsReservedId(id);

    public static bool IsReservedId(string id) =>
        ReservedIds.Any(name => id.Equals(name, StringComparison.OrdinalIgnoreCase)
                                || id.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase));

    /// <summary>予約された Id だったときの文（何が予約されているかと、直し方）。</summary>
    public static string ReservedIdProblem(string id) =>
        $"The Id '{id}' is reserved for the app itself ('host' and 'common', and any Id that starts with 'host.' or 'common.'). " +
        "Give the module an Id of its own, for example its name in lower case.";

    /// <summary>
    /// 作ったときの SDK が <paramref name="app"/>（本体の SDK）より新しければ、その文。そうでなければ（書いていないときも）null。
    /// </summary>
    public string? NewerSdkProblem(Version app) =>
        Sdk is not null && ParseSdk(Sdk) is { } built && built > Normalize(app)
            ? $"The module was built with SpoitableToolKit SDK {Sdk}, which is newer than this app's ({Normalize(app).ToString(3)}). " +
              "Update the app first, then add the module again."
            : null;

    /// <summary>
    /// 作ったときの SDK が <paramref name="minimum"/>（この本体が読めるいちばん古い SDK）より古ければ、その文。そうでなければ（書いていないときも）null。
    /// </summary>
    public string? OlderSdkProblem(Version minimum) =>
        Sdk is not null && ParseSdk(Sdk) is { } built && built < Normalize(minimum)
            ? $"The module was built with SpoitableToolKit SDK {Sdk}, which is older than this app can load (SDK {Normalize(minimum).ToString(3)} or newer): " +
              "the SDK has changed in a way that modules built with older versions cannot load. Ask its developer to build it again with a newer SDK."
            : null;

    /// <summary><c>2026.11.0</c>・<c>2026.11.0-beta.1</c>・<c>2026.11.0+abc</c> の形。読めなければ null。</summary>
    private static Version? ParseSdk(string text)
    {
        var end = text.IndexOfAny(['-', '+']);
        return System.Version.TryParse(end < 0 ? text : text[..end], out var version) ? Normalize(version) : null;
    }

    /// <summary>書いていない桁を 0 にそろえる（<c>2026.11.0</c> と <c>2026.11.0.0</c> を同じ版として比べる）。</summary>
    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    /// <summary>
    /// 外から来た字を文に入れるときは、頭の 60 字まで。制御文字と書式の字は <c>?</c> に置き換える（文はログと画面に出る）。
    /// 60 字目が 2 つ組の字（絵文字など）の前半なら、その手前で切る（半分だけ残すと、読めない字が 1 つ出る）。
    /// </summary>
    internal static string Shown(string text)
    {
        if (text.Length > 60) text = text[..(char.IsHighSurrogate(text[59]) ? 59 : 60)] + "…";
        return ControlCharacter.Replace(text, "?");
    }

    /// <summary>
    /// 外から来た字を、そのまま 1 行として出してよいか: 1 字以上 <paramref name="max"/> 字まで、<see cref="NameForbiddenPattern"/> の字が無い。
    /// module.json の外から来るもの（release の tag・添付ファイルの名前）に使う。
    /// </summary>
    internal static bool IsPlainLine(string text, int max) => text.Length > 0 && text.Length <= max && !ControlCharacter.IsMatch(text);

    /// <summary>読めない・足りない・字が外れていたら、何が悪いかを投げる（AI がそのまま直せる文で）。</summary>
    public static ModuleManifest Parse(string json)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(json).RootElement;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{FileName} is not valid JSON: {ex.Message}");
        }

        string Need(string name) =>
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } text
                ? text
                : throw new InvalidDataException($"{FileName} needs a string \"{name}\" (for example {{ \"id\": \"mymodule\", \"name\": \"My module\", \"version\": \"1.0.0\" }})");

        var id = Need("id");
        if (IsReservedId(id))
            throw new InvalidDataException($"{FileName}: {ReservedIdProblem(Shown(id))}");

        if (!IsValidId(id))
            throw new InvalidDataException($"{FileName}: \"id\" must be 1-64 characters of a-z, 0-9, '.', '_' or '-', starting and ending with a letter or digit, and not a Windows device name such as con or nul (got \"{Shown(id)}\")");

        // 名前と版は、確認の板・一覧・ログにそのまま出る。build が通すのと同じ決まりを、ここでも見る
        // （手で作った zip は build の決まりを通っていない）。
        var name = Need("name");
        if (name.Length > MaxNameLength || ControlCharacter.IsMatch(name))
            throw new InvalidDataException($"{FileName}: \"name\" must be one line of at most {MaxNameLength} characters, with no control or invisible formatting characters (got \"{Shown(name)}\")");

        var version = Need("version");
        if (!VersionShape.IsMatch(version))
            throw new InvalidDataException(
                $"{FileName}: \"version\" must be 1-{MaxSdkLength} characters of letters, digits, '.', '-' and '+', such as \"1.0.0\" (got \"{Shown(version)}\")");

        string? sdk = null;
        if (root.TryGetProperty("sdk", out var written))
        {
            sdk = written.ValueKind == JsonValueKind.String ? written.GetString() : null;
            // 字と長さも見る。版の後ろ（- や + のあと）は版としては読まないので、見ないと、そこに書いた字がそのまま「アプリを更新して」の文に入る。
            if (sdk is null || !VersionShape.IsMatch(sdk) || ParseSdk(sdk) is null)
                throw new InvalidDataException(
                    $"{FileName}: \"sdk\" must be the version of the SpoitableToolKit SDK the module was built with, such as \"2026.11.0\" " +
                    $"(the SDK's build writes it; got {Shown(written.GetRawText())})");
        }

        return new ModuleManifest(id, name, version, sdk);
    }
}
