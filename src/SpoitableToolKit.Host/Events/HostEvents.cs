using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions;
using SpoitableToolKit.Abstractions.Events.VRChat;
using SpoitableToolKit.Abstractions.Events.Video;
using SpoitableToolKit.Abstractions.Events.VrcLog;
using SpoitableToolKit.Host.VRChat;
using SpoitableToolKit.Host.Video;
using SpoitableToolKit.Host.VrcLog;

namespace SpoitableToolKit.Host.Events;

/// <summary>
/// owner として受けるモジュールの名簿もここが持つ: <see cref="Modules.ModuleManager"/> が <see cref="Admit"/>（OnInit の前）と
/// <see cref="Retire"/>（畳んだとき）で知らせる。名簿はモジュールの裏の thread からも読まれる（listener を足すとき）ので、並行に読み書きできる形にしてある。
/// </summary>
internal sealed class HostEvents
{
    private readonly ConcurrentDictionary<ISpoitableModule, byte> _admitted = new(ReferenceEqualityComparer.Instance);

    public HostEvents(ILoggerFactory loggerFactory)
    {
        Hubs = new EventHubs(module => _admitted.ContainsKey(module), loggerFactory);
        VRChat = Hubs.CreateHub(HostOwner.Instance, [typeof(IVRChatEventListener)]);
        Videos = Hubs.CreateHub(HostOwner.Instance, [typeof(IVideoEventListener)]);
        VrcLog = Hubs.CreateHub(HostOwner.Instance, [typeof(IVrcLogEventListener)]);
    }

    public EventHubs Hubs { get; }

    /// <summary>VRChat が起動した・終わった（<see cref="VRChatPresence"/> の見張りの thread から配る）。</summary>
    public EventHub VRChat { get; }

    /// <summary>動画の再生が始まった・失敗した（<see cref="VideoEventProvider"/> がログを読む thread から配る）。</summary>
    public EventHub Videos { get; }

    /// <summary>VRChat のログの行（<see cref="VrcLogBus"/> がログを読む thread から配る。行 → 出来事 の順）。</summary>
    public EventHub VrcLog { get; }

    /// <summary>OnInit の前に呼ぶ（OnInit の中で口を作り、listener を足すので）。</summary>
    public void Admit(ISpoitableModule module) => _admitted[module] = 0;

    /// <summary>モジュールを畳んだ（失敗した・終わった）。入れていないものに呼んでもよい。</summary>
    public void Retire(ISpoitableModule module)
    {
        Hubs.Forget(module);
        _admitted.TryRemove(module, out _);
    }

    /// <summary>試験用: いま owner として受けているモジュールの数（畳んだものが名簿に残っていないか — 残ると畳んだモジュールを生かし続ける）。</summary>
    internal int AdmittedCount => _admitted.Count;

    public ModuleEvents For(ISpoitableModule module) => new(module, this);
}
