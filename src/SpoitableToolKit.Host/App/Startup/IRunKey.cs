namespace SpoitableToolKit.Host.App.Startup;

/// <summary>
/// Windows の起動時に走らせるものの登録 1 つ（HKCU の Run の、このアプリの値）。
/// 本物は <see cref="RegistryRunKey"/>、e2e はファイル（<see cref="RunKeyFile"/>）。読めない・書けないときは例外（<see cref="WindowsStartup"/> が受けてログに残す）。
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
}
