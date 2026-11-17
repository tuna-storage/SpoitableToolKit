namespace SpoitableToolKit.Host.App.Startup;

/// <summary>
/// e2e の登録（<c>SPOITABLE_RUN_KEY=&lt;ファイル&gt;</c>）。<b>レジストリには触らない。</b> ファイルがあれば登録あり（中身が起動の文字列）、無ければ登録なし。
/// e2e・開発版・変異の走りは開発の bin から動き、HKCU は手元のインストール版と同じものなので、本物に書くとそちらの登録を書き換えてしまう。
/// </summary>
internal sealed class RunKeyFile(string path) : IRunKey
{
    public string Where => path;

    public string? Read() => File.Exists(path) ? File.ReadAllText(path).Trim() : null;

    public void Write(string command) => File.WriteAllText(path, command);

    public void Remove()
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
