using System.Text.Json;

namespace SpoitableToolKit.Modules.VideoUrl.Resolving;

/// <summary>JSON の欄を、型が違えば無いものとして読む（yt-dlp の出力と、サーバーの断り文句）。</summary>
internal static class JsonFields
{
    /// <summary>文字列の欄。無い・文字列でないなら null。</summary>
    public static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>数の欄（小数は切り捨て）。無い・数でないなら 0。</summary>
    public static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? (int)value.GetDouble()
            : 0;
}
