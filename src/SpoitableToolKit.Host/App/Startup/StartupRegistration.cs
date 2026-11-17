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
}
