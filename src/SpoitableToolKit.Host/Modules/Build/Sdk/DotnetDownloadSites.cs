namespace SpoitableToolKit.Host.Modules.Build.Sdk;

/// <summary>
/// .NET SDK を取りに行ってよい場所の決まり。<b>https だけで、一覧と同じホストだけ</b>（一覧に書いてあっても、よそへは取りに行かない）。
/// 一覧を読むとき（<see cref="DotnetReleaseList"/>）も、落とすとき（<see cref="DotnetSdkInstaller"/>）も、同じ決まりで見る。
/// </summary>
internal static class DotnetDownloadSites
{
    /// <summary>https だけ。http は、一覧の場所が loopback（試験の差し替え）で、行き先も loopback のときだけ。</summary>
    public static void EnsureAllowed(Uri url, Uri metadata)
    {
        if (!IsAllowed(url, metadata)) throw new InvalidOperationException($"Refusing to fetch {url}: only https is allowed");
    }

    public static bool IsAllowed(Uri url, Uri metadata) =>
        url.Scheme == Uri.UriSchemeHttps || (url.Scheme == Uri.UriSchemeHttp && metadata.IsLoopback && url.IsLoopback);

    /// <summary>一覧と同じ場所か: ホストと port が同じで、URL に名前や合言葉（userinfo）が付いていない。</summary>
    public static bool IsSameSite(Uri url, Uri metadata) =>
        url.UserInfo.Length == 0
        && string.Equals(url.Host, metadata.Host, StringComparison.OrdinalIgnoreCase)
        && url.Port == metadata.Port;
}
