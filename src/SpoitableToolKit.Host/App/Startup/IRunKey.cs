namespace SpoitableToolKit.Host.App.Startup;

/// <summary>
/// Windows の起動時に走らせるものの登録 1 つ（HKCU の Run の、このアプリの値）と、Windows の「スタートアップ アプリ」での入/切の印（<see cref="StartupApproval"/>）。
/// 本物は <see cref="RegistryRunKey"/>、e2e はファイル（<see cref="RunKeyFile"/>）。読めない・書けないときは例外（<see cref="WindowsStartup"/> が受けてログに残す）。
/// 印は読むのと消すのだけ（書かない。形を決めているのは Windows）。
/// </summary>
internal interface IRunKey
{
    /// <summary>ログに出す置き場。</summary>
    string Where { get; }

    /// <summary>登録してある起動の文字列（exe と引数）。無ければ null。</summary>
    string? Read();

    void Write(string command);

    /// <summary>無ければ何もしない。</summary>
    void Remove();

    StartupApproval ReadApproval();

    /// <summary>印を消す（Windows は印の無いものを入として扱う）。無ければ何もしない。</summary>
    void RemoveApproval();
}
