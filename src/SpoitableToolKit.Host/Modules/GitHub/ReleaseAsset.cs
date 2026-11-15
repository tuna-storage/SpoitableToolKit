namespace SpoitableToolKit.Host.Modules.GitHub;

/// <summary>リリースの添付ファイル 1 つ。<paramref name="Sha256"/> は GitHub が出していれば（<c>digest</c>）。</summary>
internal sealed record ReleaseAsset(string Name, Uri Download, long Size, string? Sha256);
