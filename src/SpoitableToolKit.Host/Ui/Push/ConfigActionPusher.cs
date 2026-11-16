using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Host.Config;
using SpoitableToolKit.Host.Config.Interfaces;

namespace SpoitableToolKit.Host.Ui.Push;

/// <summary>
/// 動きの行が変わったら、<b>その 1 行の姿だけ</b>を全部の面へ流す（<see cref="Channel"/>）。設定画面を丸ごと描き直すと、溜めてある変更が消える。
/// 文言はいまの言語で引く（言語を変えたあとは、画面が丸ごと読み直すので追いつく）。
/// </summary>
internal sealed class ConfigActionPusher(UiBroadcast broadcast, ModuleConfigStore configs, ILocalizer text) : IConfigActionSink
{
    public const string Channel = "configAction";

    public void Changed(string moduleId, string key)
    {
        if (configs.DescribeAction(moduleId, key, text) is { } state)
            broadcast.Push(Channel, new ConfigActionPush(moduleId, key, state));
    }
}
