namespace SpoitableToolKit.Modules.Hrs.Devices.Ble;

/// <summary>
/// GATT の線が落ちたまま戻らないとき、<b>アプリから繋ぎ直すか</b>の判断。BLE そのものからは切り離してあり、時刻を渡して試験できる。
/// <para>
/// ふつうは掴んだまま Windows の繋ぎ直しを待つ（<c>MaintainConnection</c>。戻れば通知もそのまま戻る）。ただ Windows が繋ぎ直さないことがあり、
/// そのままだと周回は「掴んでいる」ので探しに行かず、心拍計が戻っていても止まったままになる。
/// </para>
/// <para>
/// 手放して繋ぎ直すのは、<b>線が落ちてから <see cref="WaitForWindows"/> 経っても戻らず、その心拍計の広告がいま見えている</b>とき（落ちたあとに見えて、
/// しかも最近見えた）。広告が見えないうちは手放さない — 居ない相手に GATT を開きに行くと、「繋がった / 切れた」がぱたぱたする。
/// 落ちたあとに 1 通だけ見えてまた消えた相手も、古い広告のままなら手放さない（目撃は消えないので、「落ちたあと」だけで見ると同じことになる）。
/// 手で切った心拍計は繋ぎ直さない。
/// </para>
/// </summary>
internal static class LinkTakeover
{
    /// <summary>
    /// Windows の繋ぎ直しを待つ長さ。実機では Windows が 9 秒ほどで繋ぎ直した。周回の間隔（<c>ReconnectSeconds</c>）には結ばない —
    /// 利用者が間隔を 1 秒にすると 2 秒で手放し、30 秒にすると 1 分待つことになる。判断は周回のたびなので、実際に動くのはこれに周回 1 つぶんを足した間。
    /// </summary>
    internal static readonly TimeSpan WaitForWindows = TimeSpan.FromSeconds(10);

    /// <summary>広告を「いま見えている」とみなす古さ。能動スキャンでは見えている相手から 1 秒に何通も来る。</summary>
    internal static readonly TimeSpan RecentlySeen = TimeSpan.FromSeconds(10);

    /// <param name="downSince">掴んでいる相手の線が落ちた時刻。落ちていなければ null。</param>
    /// <param name="lastSeen">その心拍計の広告を最後に受けた時刻。見ていなければ null。</param>
    /// <param name="disconnectedByHand">手で切ってあるか（パネルの「切断」）。</param>
    public static bool ShouldTakeOver(DateTimeOffset now, DateTimeOffset? downSince, DateTimeOffset? lastSeen, bool disconnectedByHand) =>
        !disconnectedByHand
        && downSince is { } down
        && now - down >= WaitForWindows
        && lastSeen is { } seen
        && seen > down
        && now - seen <= RecentlySeen;
}
