using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpoitableToolKit.Modules.Hrs.Panel;

/// <summary>パネル（とタイル）へ返す JSON の書き方。名前は camelCase・null は書かない。</summary>
internal static class HrsPanelJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
