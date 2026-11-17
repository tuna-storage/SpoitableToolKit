namespace SpoitableToolKit.E2E.Desktop;

/// <summary>
/// Windows の起動時の起動を、レジストリではなくファイルへ登録させるアプリ（<c>SPOITABLE_RUN_KEY</c>。本物の Run には触らない）。
/// 設定は入、登録は前の置き場を指したまま、で始める（起動のときに今の exe へ書き直されるか）。
/// </summary>
public sealed class RunKeyAppFixture : AppFixture
{
    public const string Elsewhere = "\"D:\\elsewhere\\SpoitableToolKit.Host.exe\" --tray";

    public string RunKey => Path.Combine(DataDirectory, "run-key.txt");

    /// <summary>このアプリが書くはずのもの（e2e では、走っている exe そのもの）。</summary>
    public static string Expected => $"\"{Path.Combine(HostDirectory, "SpoitableToolKit.Host.exe")}\" --tray";

    protected override void Prepare()
    {
        File.WriteAllText(Path.Combine(DataDirectory, "config.json"), @"{ ""Language"": ""Ja"", ""StartWithWindows"": true }");
        File.WriteAllText(RunKey, Elsewhere);
        ExtraEnvironment["SPOITABLE_RUN_KEY"] = RunKey;
    }
}
