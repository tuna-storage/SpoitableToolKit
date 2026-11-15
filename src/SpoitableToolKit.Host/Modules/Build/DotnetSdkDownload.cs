namespace SpoitableToolKit.Host.Modules.Build;

/// <summary>
/// 落とすことになる .NET SDK（落とす前に利用者へ見せる）。<paramref name="Bytes"/> は落とす zip の大きさ（分からなければ 0）。
/// </summary>
internal sealed record DotnetSdkDownload(string Version, Uri Url, string Sha512, long Bytes);
