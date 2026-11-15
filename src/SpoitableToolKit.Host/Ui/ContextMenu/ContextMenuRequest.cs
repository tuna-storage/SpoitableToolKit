using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Ui;

namespace SpoitableToolKit.Host.Ui.ContextMenu;

/// <summary>
/// JS から届く名乗り。
/// ⚠ <see cref="JsonSerializerDefaults.Web"/> で読むこと。既定は大文字小文字を区別するので、JS の <c>{"key":"log-row"}</c> が <see cref="Key"/> に入らず、既定値のまま黙って動く（<c>LogPageRequest</c> と同じ）。
/// <see cref="Module"/> はモジュールの自己申告ではなく、シェルが <c>postMessage</c> の発信元 origin から決めたもの。
/// </summary>
internal sealed record ContextMenuRequest
{
    public string Id { get; init; } = string.Empty;

    public string Key { get; init; } = string.Empty;

    public string? Module { get; init; }

    public bool Editable { get; init; }

    public bool Secret { get; init; }

    public string SelectionText { get; init; } = string.Empty;

    public Dictionary<string, string> Data { get; init; } = [];

    private static readonly JsonSerializerOptions RequestOptions = new(JsonSerializerDefaults.Web);

    /// <summary>読めない名乗りは空の場所として扱う（項目が出ないので、画面はメニューを開かない）。</summary>
    public static ContextMenuRequest Parse(string requestJson, ILogger logger)
    {
        try
        {
            return JsonSerializer.Deserialize<ContextMenuRequest>(requestJson, RequestOptions) ?? new ContextMenuRequest();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Could not read the context menu request");
            return new ContextMenuRequest();
        }
    }

    public ContextMenuTarget ToTarget() => new()
    {
        Key = Key,
        Data = Data,
        Editable = Editable,
        Secret = Secret,
        SelectionText = SelectionText,
    };
}
