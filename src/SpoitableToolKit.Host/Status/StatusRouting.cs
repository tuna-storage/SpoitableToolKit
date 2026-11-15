using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Events.Localization;
using SpoitableToolKit.Host.Events.VrcLog;
using SpoitableToolKit.Host.Localization;
using SpoitableToolKit.Host.Ui.Shell;
using SpoitableToolKit.Host.VRChat;
using SpoitableToolKit.Host.VrcLog;

namespace SpoitableToolKit.Host.Status;

/// <summary>
/// 本体が文字にして持っている状態（タイトルバーの「VRChat のログを読んでいます」・ワールドと人数と動画の札）を、書き直すきっかけに繋ぐ。
/// <para>
/// <see cref="Program"/> から出してある（<see cref="LogRouting"/> と同じ理由: 起動の組み立ての中に埋めると、アプリを起こさないと確かめられない）。
/// </para>
/// <para>
/// 読むファイルが変わった → タイトルバー（行ではなくこちらで書く。VRChat が止まっていると行が 1 本も来ない）。
/// 言語を読み直した → タイトルバー → 札（次の出来事まで古い言語のまま、を作らない。モジュールの分はそれぞれが次に書いたときに変わる）。
/// VRChat の起動・終了は <see cref="VRChatPresence.Connect"/>。
/// </para>
/// </summary>
internal static class StatusRouting
{
    public static void Connect(IEventSource logFiles, IEventSource languages, LogReadingStatus reading, InstanceTracker instance)
    {
        var title = new Title(reading);
        logFiles.AddListener<ILogFileEventListener>(HostOwner.Instance, title);
        languages.AddListener<ILanguageEventListener>(HostOwner.Instance, title);
        languages.AddListener<ILanguageEventListener>(HostOwner.Instance, new Chips(instance));
    }

    private sealed class Title(LogReadingStatus reading) : ILogFileEventListener, ILanguageEventListener
    {
        public void OnLogFileChanged(LogFileChangedEventParams e) => reading.Show();

        public void OnLanguageLoaded(LanguageLoadedEventParams e) => reading.Show();
    }

    private sealed class Chips(InstanceTracker instance) : ILanguageEventListener
    {
        public void OnLanguageLoaded(LanguageLoadedEventParams e) => instance.Publish();
    }
}
