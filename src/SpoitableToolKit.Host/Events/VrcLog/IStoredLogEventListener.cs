using SpoitableToolKit.Abstractions.Events;
using SpoitableToolKit.Host.VrcLog.Store;

namespace SpoitableToolKit.Host.Events.VrcLog;

/// <summary>
/// Host の中だけ（足せるのはアプリ自身 — 画面へ流す <see cref="SpoitableToolKit.Host.Ui.Shell.UiPump"/>）。
/// 書き込みのスレッドから、コミットが返ったあとで呼ぶ。既定の実装は置かない（実装し忘れを build で見つける）。
/// </summary>
internal interface IStoredLogEventListener : IEventListener
{
    void OnEventStored(StoredEvent stored);

    /// <summary>生行が書けた（<c>StoreAllLines</c> が入のときだけ。id は生行の表の通し番号 — 出来事とは別）。</summary>
    void OnLineStored(StoredLine stored);
}
