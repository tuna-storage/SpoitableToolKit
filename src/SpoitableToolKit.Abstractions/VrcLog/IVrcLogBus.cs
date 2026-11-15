using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Abstractions.Events.VrcLog;

namespace SpoitableToolKit.Abstractions.VrcLog;

/// <summary>
/// VRChat の output_log を tail して構造化する横断サービス（<see cref="IModuleHost.VrcLog"/>）。
/// <para>
/// モジュールではなく本体に置く（どのモジュールも同じログを読むため）。
/// </para>
/// <para>
/// 行を受け取るのは <see cref="IVrcLogEventListener"/>。型を書いて足す:
/// <code>
/// host.VrcLog.AddListener&lt;IVrcLogEventListener&gt;(this, this);
/// </code>
/// 1 行ごとに、全部の行（<see cref="IVrcLogEventListener.OnLogLine"/>。意味の分からない行も）→ 意味の分かった行だけ（<see cref="IVrcLogEventListener.OnLogEvent"/>）の順に届く。
/// 配るのはホストだけ（この口からは配れない）。
/// </para>
/// </summary>
public interface IVrcLogBus : IEventSource
{
    /// <summary>いま tail しているログファイル。まだ見つかっていなければ null。</summary>
    string? CurrentLogFile { get; }
}
