using SpoitableToolKit.Abstractions.VrcLog;

namespace SpoitableToolKit.Host.VrcLog.Store;

/// <summary>
/// 書けた生行 1 件。<c>StoreAllLines</c> が入のときだけこの形で流れる。
/// 切のときは保存していないので id が無く、<c>logBus</c> から直に流れる（そのセッション限り）。
/// 入のときに直の経路を残すと、同じ行が push と遡りの両方から来て二重に出る。
/// <c>log_events</c> とは別の通し番号なので、継ぎ目を弾く印は面ごとに 2 つ要る。
/// </summary>
internal readonly record struct StoredLine(long Id, VrcLogLine Line);
