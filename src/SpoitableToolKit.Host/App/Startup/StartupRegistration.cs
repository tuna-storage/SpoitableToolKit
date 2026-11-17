namespace SpoitableToolKit.Host.App.Startup;

/// <summary>Windows の起動時の起動を、何で登録するか・設定とどう合わせるか。登録の置き場を知らない形にしてある（試験はここを見る）。</summary>
internal static class StartupRegistration
{
    /// <summary>Windows の起動時に走らせる文字列。トレイにしまった状態で始める印（<see cref="WindowsStartup.TrayArgument"/>）を付ける。パスは空白を含みうるので囲む。</summary>
    public static string CommandFor(string executable) => $"\"{executable}\" {WindowsStartup.TrayArgument}";

    /// <summary>
    /// 設定を正とする。入なら、無い・違うものを指しているときに書き直す（入れ直して置き場が変わった・前の版が別の名前で書いた）。
    /// 切なら、残っていれば消す。パスの大文字小文字は見ない（Windows では同じ場所）。
    /// </summary>
    /// <param name="registered">いま登録してあるもの（無ければ null）。</param>
    public static StartupChange Plan(bool wanted, string? registered, string command)
    {
        if (!wanted) return registered is null ? StartupChange.None : StartupChange.Remove;

        return string.Equals(registered, command, StringComparison.OrdinalIgnoreCase) ? StartupChange.None : StartupChange.Write;
    }

    /// <summary>
    /// Windows の「スタートアップ アプリ」の印（REG_BINARY、ふつうは 12 バイト）を読む。先頭のバイトが奇数なら切、偶数なら入。
    /// 長さは見ない（12 バイトに足りなくても、先頭のバイトがあればそれで決める）。値はあるのに空なら入として扱う（切のときは、ほかの印と同じく片付ける）。
    /// 値が無ければ <see cref="StartupApproval.Missing"/>（Windows も入として扱う）。
    /// </summary>
    public static StartupApproval ApprovalOf(byte[]? mark) => mark switch
    {
        null => StartupApproval.Missing,
        [var first, ..] when (first & 1) == 1 => StartupApproval.Disabled,
        _ => StartupApproval.Enabled,
    };

    /// <summary>
    /// 設定が入なのに Windows の側で切られている（起動したとき・設定の画面を開いたときに見る）。切られた方を正として、設定を切に写す。
    /// 設定を変えたその場では見ない — 利用者がこちらで入れ直したなら、こちらが新しい（<see cref="ClearsApproval"/>）。
    /// </summary>
    public static bool TurnedOffInWindows(bool wanted, StartupApproval approval) => wanted && approval == StartupApproval.Disabled;

    /// <summary>
    /// Windows の側の印を消すか。入なら、切の印が残っていれば消す（残すと Run を書いても起動しない）。
    /// 切なら、どんな印でも残さない（Run の値と一緒に片付ける。Run が無いのに印だけ残る形にしない）。
    /// </summary>
    public static bool ClearsApproval(bool wanted, StartupApproval approval) =>
        wanted ? approval == StartupApproval.Disabled : approval != StartupApproval.Missing;
}
