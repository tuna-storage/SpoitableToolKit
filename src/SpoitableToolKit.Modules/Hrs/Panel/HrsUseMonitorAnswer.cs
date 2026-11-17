namespace SpoitableToolKit.Modules.Hrs.Panel;

/// <summary>心拍計タブの行の「接続」の答え。</summary>
/// <param name="Accepted">設定の「心拍計」をその 1 台にしたか。アドレスが読めなければ false（何も書かない）。</param>
/// <param name="Device">設定に入れた値（<c>名前 [アドレス]</c>）。断ったら null。</param>
internal sealed record HrsUseMonitorAnswer(bool Accepted, string? Device);
