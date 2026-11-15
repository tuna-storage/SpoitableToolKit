using SpoitableToolKit.Abstractions.Config;
using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.Events.Config;
using SpoitableToolKit.Host.Config.Models;

namespace SpoitableToolKit.Host.Config.Interfaces;

/// <summary>
/// 設定の型は実行時にしか分からないので、型ごとの入れ物（<see cref="ConfigChannel{TConfig}"/>）を迎え入れるときに 1 回だけ作り、
/// あとはこの型の付かない口越しに呼ぶ（配るたびに反射しない）。
/// </summary>
internal interface IConfigChannel
{
    /// <summary>listener を足す・外す口（モジュールには owner を照合する包みを被せて渡す）。</summary>
    IEventSource Source { get; }

    int ListenerCount { get; }

    ConfigAnswer Ask(IModuleConfig proposed, IReadOnlyList<string> changed, IReadOnlyCollection<string> settings);

    void Tell(IModuleConfig config, IReadOnlyList<string> entered, IReadOnlyCollection<string> settings);

    void Close();
}
