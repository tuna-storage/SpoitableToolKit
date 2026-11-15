namespace SpoitableToolKit.Modules.VideoUrl.Resolving;

/// <summary>解決できたストリーム 1 本。</summary>
/// <param name="Label">UI に出す説明。</param>
/// <param name="Kind">hls / muxed / audio。</param>
/// <param name="Url">プレイヤーに渡す URL。</param>
/// <param name="Note">選ぶときの注意。無ければ null。</param>
public sealed record ResolvedStream(string Label, string Kind, string Url, string? Note = null);
