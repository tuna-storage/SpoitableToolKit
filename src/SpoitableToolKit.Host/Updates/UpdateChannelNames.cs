using SpoitableToolKit.Host.Settings;

namespace SpoitableToolKit.Host.Updates;

/// <summary><c>.github/scripts/pack.ps1</c> が <c>vpk pack --channel</c> に渡す名前と同じにする。</summary>
internal static class UpdateChannelNames
{
    public static string ToVelopack(UpdateChannel channel) => channel switch
    {
        UpdateChannel.Nightly => "nightly",
        _ => "stable",
    };

    /// <summary>入っている版のチャンネル。知らない名前（<c>--channel</c> を付けずに詰めた物の <c>win</c> など）は null。</summary>
    public static UpdateChannel? FromVelopack(string? name) => name?.ToLowerInvariant() switch
    {
        "stable" => UpdateChannel.Stable,
        "nightly" => UpdateChannel.Nightly,
        _ => null,
    };
}
