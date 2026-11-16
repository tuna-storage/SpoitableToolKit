namespace SpoitableToolKit.Modules.Hrs.Panel;

/// <summary>パネルへ流す現状（push の <c>status</c> と <c>getStatus</c> の答え）。JSON の名前と並びは、この宣言の順。</summary>
internal sealed record HrsPanelState
{
    /// <summary>繋ぎ先の指定。<b>繋がっていないときに何を探しているのかを出す</b>ために要る（設定を開かないと分からない、という状態を作らない）。</summary>
    public required string Configured { get; init; }

    public required bool Connected { get; init; }

    /// <summary>心拍が届いているか。繋がっているのに false なら「接続済み・心拍なし」（充電器の上・繋がった直後）— 「受信中」は届いている間だけ。</summary>
    public required bool Receiving { get; init; }

    /// <summary>手で切ってある心拍計（「接続」を押すまで繋ぎ直さない）。無ければ null。</summary>
    public required string? HeldOff { get; init; }

    public required string Connection { get; init; }

    public required string? Device { get; init; }

    /// <summary>HDS には BLE のアドレスが無い。0 を書式化すると 00:00:... という嘘になる。</summary>
    public required string? Address { get; init; }

    /// <summary>電池（%）。GATT で繋がっているときだけ分かる（繋いだときと 1 分ごとに読む）。</summary>
    public required int? Battery { get; init; }

    public required int HeartRate { get; init; }

    public required bool Running { get; init; }

    public required bool HdsEnabled { get; init; }

    public required int HdsPort { get; init; }

    public required string Firewall { get; init; }

    public required string FirewallCommand { get; init; }

    /// <summary>見えている心拍計（「心拍計」タブ）。心拍サービスを名乗る広告を出しているものだけ。</summary>
    public required HrsPanelMonitor[] Discovered { get; init; }

    public required HrsPanelSample[] Samples { get; init; }

    /// <summary>
    /// この繋がりの最小 / 平均 / 最大。<b>グラフ（直近 3 分）からは数えない</b> — オーバーレイと札と同じ数字にする。まだ 1 拍も無ければ出さない。
    /// </summary>
    public required HrsPanelStats? Stats { get; init; }

    public required bool RecordingEnabled { get; init; }

    public required long RecordCount { get; init; }

    public required string? RecordFirst { get; init; }

    public required string? RecordLast { get; init; }

    public required string? LastExport { get; init; }

    public required bool WsEnabled { get; init; }

    /// <summary>待ち受けているポートは<b>開いた側が正</b>。設定を変えても開き直すまでは古い方が生きている。</summary>
    public required int WsPort { get; init; }

    public required string WsStatus { get; init; }

    /// <summary>OS の言葉なので翻訳しない（「ポートが埋まっている」の原文が一番役に立つ）。</summary>
    public required string? WsError { get; init; }

    public required int WsClients { get; init; }

    public required string[] Overlays { get; init; }

    /// <summary>
    /// 配信タブのプレビューへ中継する、最後に配信した 1 通。<b>配信を切っていても作っている</b>（OBS が開いたら見えるものと同じ）。まだ 1 通も無ければ null。
    /// </summary>
    public required HrsPanelOverlayFeed? OverlayFeed { get; init; }
}
