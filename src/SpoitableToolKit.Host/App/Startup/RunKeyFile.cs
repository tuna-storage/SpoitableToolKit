namespace SpoitableToolKit.Host.App.Startup;

/// <summary>
/// e2e の登録（<c>SPOITABLE_RUN_KEY=&lt;ファイル&gt;</c>）。<b>レジストリには触らない。</b> ファイルがあれば登録あり（中身が起動の文字列）、無ければ登録なし。
/// Windows の「スタートアップ アプリ」の印は隣のファイル（<see cref="ApprovalPath"/>）で、中身はレジストリの値と同じバイト列を 16 進で並べたもの（例 <c>03 00 00 00 …</c>）。
/// e2e・開発版・変異の走りは開発の bin から動き、HKCU は手元のインストール版と同じものなので、本物に書くとそちらの登録を書き換えてしまう。
/// </summary>
internal sealed class RunKeyFile(string path) : IRunKey
{
    public string Where => path;

    public string ApprovalPath => path + ".approved";

    public string? Read() => File.Exists(path) ? File.ReadAllText(path).Trim() : null;

    public void Write(string command) => File.WriteAllText(path, command);

    public void Remove()
    {
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>16 進として読めない中身は、読めないものとして例外にする（レジストリが読めないときと同じ扱い）。</summary>
    public StartupApproval ReadApproval()
    {
        if (!File.Exists(ApprovalPath)) return StartupApproval.Missing;

        try
        {
            return StartupRegistration.ApprovalOf(Convert.FromHexString(string.Concat(File.ReadAllText(ApprovalPath).Where(c => !char.IsWhiteSpace(c)))));
        }
        catch (FormatException ex)
        {
            throw new IOException($"{ApprovalPath} is not a hex dump of the mark", ex);
        }
    }

    public void RemoveApproval()
    {
        if (File.Exists(ApprovalPath)) File.Delete(ApprovalPath);
    }
}
