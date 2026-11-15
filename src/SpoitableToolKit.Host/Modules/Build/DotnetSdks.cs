using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Modules.Build.Sdk;

namespace SpoitableToolKit.Host.Modules.Build;

/// <summary>
/// モジュールをソースから build するための .NET SDK を<b>探す</b>・<b>落とす</b>。
/// <list type="bullet">
///   <item>探す順: <c>PATH</c> の <c>dotnet</c> → <c>%ProgramFiles%\dotnet</c> → 自分で落としたもの（<c>tools/dotnet</c>）。
///         <c>dotnet --list-sdks</c> に聞いて、版が <see cref="Major"/> 以上の SDK があるものを使う</item>
///   <item>落とすのは、<b>利用者が同意したあと</b>（呼ぶ側の仕事）。
///         公式の一覧（release-metadata の JSON）から zip の URL と SHA-512 を読み、落として突き合わせ、<c>tools/dotnet</c> に展開する。
///         <b>インストーラもスクリプトも走らせない</b>（zip を展開するだけ。PATH も registry も触らない）</item>
///   <item>URL は https だけで、一覧と同じホストだけ（一覧に書いてあっても、よそへは取りに行かない）</item>
/// </list>
/// <para>
/// 係に渡す: 探す（<see cref="DotnetSdkLocator"/>）・何を落とすかを一覧から選ぶ（<see cref="DotnetReleaseList"/>）・落として入れる（<see cref="DotnetSdkInstaller"/>）。
/// 取りに行ってよい場所の決まりは <see cref="DotnetDownloadSites"/>。
/// </para>
/// </summary>
internal sealed class DotnetSdks
{
    /// <summary>モジュールが狙う .NET の版（<c>net10.0</c>）。これ以上の SDK なら build できる。</summary>
    public const int Major = 10;

    public static readonly Uri DefaultMetadata = new("https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json");

    /// <summary>
    /// 一覧の場所。差し替え（<c>SPOITABLE_DOTNET_RELEASES</c>）を受けるのは、<b>自分の機械（loopback）を指すときだけ</b>（e2e 用。GitHub の差し替えと同じ決まり）。
    /// よその場所を指せると、Microsoft 以外から <c>dotnet.exe</c> を落とさせる口になる。
    /// </summary>
    internal static Uri MetadataFrom(string? custom) =>
        custom is { Length: > 0 } && Uri.TryCreate(custom, UriKind.Absolute, out var uri) && uri.IsLoopback ? uri : DefaultMetadata;

    /// <summary>
    /// 探す <c>dotnet</c> の差し替え（<c>SPOITABLE_DOTNET</c>）。書いてあれば、<c>PATH</c> と既定の入れ先の代わりに、それだけを見る
    /// （自分で落としたものは、いつも見る）。e2e が「この PC には SDK が無い」を作るのに使う。無ければ null（ふだんの探し方）。
    /// </summary>
    internal static IReadOnlyList<string>? CandidatesOverride(string? custom) =>
        custom is { Length: > 0 } ? [custom] : null;

    private readonly DotnetSdkLocator _locator;
    private readonly DotnetReleaseList _releases;
    private readonly DotnetSdkInstaller _installer;

    /// <param name="http">一覧を読み、SDK を落とす口。</param>
    /// <param name="metadata">一覧の場所（<see cref="MetadataFrom"/>）。</param>
    /// <param name="toolsRoot">道具の置き場（<c>tools</c>）。自分で落とした SDK はその下の <c>dotnet</c>。</param>
    /// <param name="logger">SDK の logger（カテゴリは呼ぶ側が決める）。係もこれで書く。</param>
    /// <param name="candidates">探す <c>dotnet</c> の差し替え（<see cref="CandidatesOverride"/>）。</param>
    /// <param name="listSdks"><c>dotnet --list-sdks</c> の答えの差し替え（試験用）。</param>
    public DotnetSdks(
        HttpClient http,
        Uri metadata,
        string toolsRoot,
        ILogger logger,
        IReadOnlyList<string>? candidates = null,
        Func<string, CancellationToken, Task<string?>>? listSdks = null)
    {
        // 係はこのクラスの logger で書く。入れる係は、展開したものを探す係で確かめる。
        _locator = new DotnetSdkLocator(logger, candidates, listSdks);
        _releases = new DotnetReleaseList(http, metadata);
        _installer = new DotnetSdkInstaller(http, metadata, toolsRoot, logger, _locator);
    }

    public string Directory => _installer.Directory;

    /// <summary>使える SDK を探す。無ければ null（落とすかを聞く）。自分で落としたものも見る。</summary>
    public Task<DotnetSdk?> FindAsync(CancellationToken cancellationToken) =>
        _locator.FindAsync(_installer.OwnDotnet, cancellationToken);

    /// <summary>いま落とすことになる SDK。<b>ここでは落とさない</b>（<see cref="DotnetReleaseList.LatestAsync"/>）。</summary>
    public Task<DotnetSdkDownload> LatestAsync(CancellationToken cancellationToken) => _releases.LatestAsync(cancellationToken);

    /// <summary>落として、突き合わせ、展開する。<b>利用者が同意したあとに呼ぶ</b>（<see cref="DotnetSdkInstaller.InstallAsync"/>）。</summary>
    public Task<DotnetSdk> InstallAsync(
        DotnetSdkDownload download, CancellationToken cancellationToken, IProgress<long>? progress = null, Action? unpacking = null) =>
        _installer.InstallAsync(download, cancellationToken, progress, unpacking);

    /// <summary>前の回の残りを消す。<b>アプリの起動のときに 1 回</b>、裏で（<see cref="DotnetSdkInstaller.ClearLeftoversAsync"/>）。</summary>
    public Task ClearLeftoversAsync() => _installer.ClearLeftoversAsync();
}
