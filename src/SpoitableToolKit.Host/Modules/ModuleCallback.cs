using Microsoft.Extensions.Logging;

namespace SpoitableToolKit.Host.Modules;

/// <summary>
/// モジュールが渡してきたコードを走らせるときの決まり。
/// <para>
/// <b>裏のスレッドで走らせ、例外は握ってログに残す。</b>
/// 落とすと CEF から回ってきたスレッドごと道連れになるし、黙って消えると
/// 「押したのに何も起きない」しか手掛かりが残らない。
/// </para>
/// </summary>
internal static class ModuleCallback
{
    public static void Run(ILogger logger, string what, Action action) => _ = Task.Run(() =>
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Threw from {What}", what);
        }
    });
}
