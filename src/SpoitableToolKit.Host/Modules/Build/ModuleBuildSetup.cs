namespace SpoitableToolKit.Host.Modules.Build;

/// <summary>
/// アプリが、モジュールをソースから build するのに使うもの（起動のときに 1 回決める）。
/// </summary>
/// <param name="Sdks">.NET SDK を探す・落とす係。</param>
/// <param name="SdkFeed">アプリに同梱した SDK のパッケージの置き場（<c>&lt;アプリ&gt;/sdk</c>）。</param>
/// <param name="PackageFeed">ほかのパッケージの入れ元（null なら無し）。</param>
internal sealed record ModuleBuildSetup(DotnetSdks Sdks, string SdkFeed, string? PackageFeed)
{
    /// <summary>
    /// ほかのパッケージの入れ元。ふだんは nuget.org。<b>GitHub の差し替えが効いているとき（e2e。loopback）は、入れ元に入れない</b> —
    /// 試験のアプリが、build の restore で外へ出ないように（本物の GitHub へ出ないのと同じ決まり）。
    /// </summary>
    public static string? PackageFeedFor(Uri gitHubApi) => gitHubApi.IsLoopback ? null : ModuleBuildTools.NuGetOrg;
}
