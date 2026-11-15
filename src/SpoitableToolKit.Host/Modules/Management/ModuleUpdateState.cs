namespace SpoitableToolKit.Host.Modules.Management;

/// <summary>
/// 覚えている中身。repo（<c>owner/name</c>、小文字）→ <see cref="Latest"/> は最新のリリースのタグ、<see cref="Heads"/> は既定の branch の先頭の commit。
/// <see cref="Heads"/> は古い形のファイルには無い（読むときに空にする）。
/// </summary>
internal sealed record ModuleUpdateState(DateTimeOffset? CheckedAt, Dictionary<string, string> Latest, Dictionary<string, string> Heads);
