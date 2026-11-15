using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Config;

namespace SpoitableToolKit.Host.Config;

/// <summary>
/// 出す / 出さないの判断はここだけが持つ。画面は結果（<c>shown</c>）だけを受け取る —
/// 条件はモジュールが書いたふつうの C# の式で、画面側では評価できない。
/// </summary>
internal sealed class ConfigSection(ConfigMember member, object instance, ConfigSection? parent)
{
    public ConfigMember Member { get; } = member;

    public object Instance { get; } = instance;

    public ConfigSection? Parent { get; } = parent;

    public string Path => Member.Path;

    public int Depth => (Parent?.Depth ?? -1) + 1;

    /// <summary>
    /// 自分と先祖が全部入か。1 つでも切なら下は出ない。
    /// 投げたら「出す」に倒す。<c>IsEnabled</c> はモジュールのコードで、設定画面を描くたびに呼ばれる。
    /// 隠す側に倒すと触れない設定が黙って生まれるので、出しておいて記録に残す方がまだ直せる。
    /// </summary>
    public bool IsShown(ILogger logger)
    {
        if (Parent is { } parent && !parent.IsShown(logger)) return false;

        try
        {
            // 何を実装しているかで決まる。ただの項目分けは常に出る。
            return Instance switch
            {
                ITogglableSettings toggle => toggle.Enabled,
                IConditionalSettings conditional => conditional.IsEnabled,
                _ => true,
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Path}.IsEnabled threw; showing its settings anyway", Path);
            return true;
        }
    }
}
