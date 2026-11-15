using SpoitableToolKit.Abstractions.Events.VrcLog;
using SpoitableToolKit.Abstractions.VrcLog;
using SpoitableToolKit.Host.Events;
using SpoitableToolKit.Host.Events.VrcLog;
using SpoitableToolKit.Host.VrcLog.Store;

namespace SpoitableToolKit.Host.Ui.Shell;

/// <summary>
/// ログを記録する側と画面へ出す側へ繋ぐ規則。<see cref="Program"/> から出してあるのは、起動の組み立てに埋めると実際にアプリを起こさないと確かめられないため。
/// 1. イベントは常に保存し、書けてから画面へ流す（見えているのに記録に無い、を防ぐ）。例外は API の呼び出し（<see cref="ApiRequest"/>）で、<c>StoreApiRequests</c> が切なら保存も表示もしない。
/// 2. 生行は、保存する / しないで道が変わる。保存するなら書けてから id 付きで流し、しないなら直に流す（そのセッション限り、id なし）。両方通してはいけない — 入にした瞬間に、同じ行が push と遡りの両方から来て 2 回出る。
/// 例外は <c>[API]</c> の行で、<c>StoreApiRequests</c> が切ならどちらの道も通さない（出来事の側と揃える）。
/// </summary>
internal static class LogRouting
{
    /// <summary>VRChat の API の行に付くタグ（<c>[API]</c>）。<see cref="ApiRequest"/> もこのタグの行から作られる。</summary>
    private const string ApiTag = "API";

    public static void Connect(
        IVrcLogBus bus, SqliteVrcLogStore store, UiPump pump, Func<bool> storeAllLines, Func<bool> storeApiRequests)
    {
        bus.AddListener<IVrcLogEventListener>(HostOwner.Instance, new Router(store, pump, storeAllLines, storeApiRequests));
        store.Stored.AddListener<IStoredLogEventListener>(HostOwner.Instance, pump);
    }

    private sealed class Router(SqliteVrcLogStore store, UiPump pump, Func<bool> storeAllLines, Func<bool> storeApiRequests)
        : IVrcLogEventListener
    {
        // 構造化できたイベントは常に保存する（VRChat 自身のログは old_logs へ回されて最後は消えるため）。
        // ただし API の呼び出しは設定が入のときだけ（既定 切。出来事の 95% を占めて記録を膨らませる）。
        // 保存しないものは画面にも流さない。捨てるのはここだけで、バスの購読者（モジュール）には今までどおり届く。
        public void OnLogEvent(VrcLogEventParams e)
        {
            if (e is ApiRequest && !storeApiRequests()) return;
            store.Append(e);
        }

        // 生行は既定では保存しない（全件ログだと 1 セッション 15,000 行を超える）。
        // API を残さないあいだは [API] の生行も保存も表示もしない。出来事にならない「[N] Sending Get …」やエラーの行も同じタグなので、正規表現ではなくタグで見る。
        // 容量のためではなく（生行の大半は VRCTrackingManager）、API を切ったのに生行に API が出る、を無くすため。
        public void OnLogLine(VrcLogLine line)
        {
            if (line.Tag == ApiTag && !storeApiRequests()) return;
            if (storeAllLines()) store.Append(line);
            else pump.Enqueue(line);
        }
    }
}
