namespace SpoitableToolKit.Host.Modules.Build;

/// <summary>ソースを build するのに使う道具の場所。</summary>
/// <param name="Dotnet"><c>dotnet</c> の実行ファイル（.NET SDK）。</param>
/// <param name="SdkFeed">
/// パッケージ <c>SpoitableToolKit.Sdk</c> の置き場（アプリに同梱したもの）。<b><c>SpoitableToolKit.*</c> はここからしか取らせない</b>
/// （<see cref="ModuleSourceBuild.NuGetConfig"/>）。
/// </param>
/// <param name="PackageFeed">ほかのパッケージの入れ元（<see cref="NuGetOrg"/>）。null なら無し（試験と e2e は外へ出ない）。</param>
internal sealed record ModuleBuildTools(string Dotnet, string SdkFeed, string? PackageFeed)
{
    public const string NuGetOrg = "https://api.nuget.org/v3/index.json";
}
