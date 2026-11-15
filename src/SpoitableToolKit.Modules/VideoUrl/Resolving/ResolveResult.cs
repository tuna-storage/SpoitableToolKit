namespace SpoitableToolKit.Modules.VideoUrl.Resolving;

/// <summary>解決の結果。<paramref name="Streams"/> が空になることはない（空なら例外にする）。</summary>
/// <param name="Notice">
/// 結果そのものは正常だが、知らせておくべきこと（高画質が存在しない等）。無ければ null。
/// </param>
public sealed record ResolveResult(string? Title, IReadOnlyList<ResolvedStream> Streams, string? Notice = null);
