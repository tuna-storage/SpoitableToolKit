using SpoitableToolKit.Abstractions.VrcLog;

namespace SpoitableToolKit.Host.VrcLog.Store;

/// <summary>
/// 書けたイベント 1 件。<paramref name="Id"/> は DB が採番したもの。
/// Id には意味がある。過去を遡るときの手掛かり（<c>WHERE id &lt; …</c>）と、起動直後の追いつきで二重に出さないための鍵を兼ねる。
/// </summary>
internal readonly record struct StoredEvent(long Id, VrcLogEventParams Event);
