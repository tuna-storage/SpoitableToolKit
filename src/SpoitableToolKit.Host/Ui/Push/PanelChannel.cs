using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Ui.Shell;

namespace SpoitableToolKit.Host.Ui.Push;

/// <summary>
/// Id をここで刻むのが役目で、モジュールは自分の宛先を指定できない（UI からの呼び出しを発信元 origin で決めるのと同じ原則）。
/// 出し先は <see cref="UiBroadcast"/> なので、繋がっている全サーフェスのシェルに同じものが届く。
/// どの iframe へ渡すかは各シェルが自分の持っているパネルを見て決める（開いていなければそのシェルでは捨てられる）。
/// </summary>
/// <param name="vr">空間の窓へ頼む口の差し込み口（VR が動いていなければ空）。</param>
internal sealed class PanelChannel(string moduleId, UiBroadcast broadcast, VrControlsSlot vr, ILogger logger) : IPanelSink
{
    public void Push(string channel, string payloadJson)
    {
        JsonElement payload;
        try
        {
            // ここで一度 JSON として読む。読めないものを素通しすると、シェル側の JSON.parse で
            // どのモジュールの何が壊れているか分からない失敗になる。出どころが分かるうちに落とす。
            payload = JsonSerializer.Deserialize<JsonElement>(payloadJson);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Dropping '{Channel}' pushed by {Id}: payload is not readable as JSON",
                channel, moduleId);
            return;
        }

        // JsonElement はそのままの形で再度書き出されるので、モジュール側の命名規則が保たれる。
        broadcast.Push("modulePush", new { module = moduleId, channel, payload });
    }

    /// <summary>全サーフェスへ「このモジュールの画面を出して」と伝える。受け取ったシェルが自分の持ち物を見て決めるので、サイドバーの無い面では何も起きない。</summary>
    public void Show() => broadcast.Push("showPanel", new { module = moduleId });

    /// <summary>頼みは VR の周が片付けるので、どのスレッドから呼んでもよい。VR が動いていなければ何もしない。</summary>
    public void ToggleVrPanel(string view) => vr.Current?.ToggleWindow(view);
}
