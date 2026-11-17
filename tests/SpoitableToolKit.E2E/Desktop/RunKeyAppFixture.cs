using Xunit;

namespace SpoitableToolKit.E2E.Desktop;

/// <summary>
/// Windows の起動時の起動を、レジストリではなくファイルへ登録させるアプリ（<c>SPOITABLE_RUN_KEY</c>。本物の Run にも、Windows の「スタートアップ アプリ」の印にも触らない）。
/// 印は隣のファイル（<see cref="Approval"/>）に、レジストリの値と同じバイト列を 16 進で置く。
/// 既定は、設定は入・登録は前の置き場を指したまま、で始める（起動のときに今の exe へ書き直されるか）。
/// </summary>
public class RunKeyAppFixture : AppFixture
{
    public const string Elsewhere = "\"D:\\elsewhere\\SpoitableToolKit.Host.exe\" --tray";

    /// <summary>Windows の側で切った印（先頭が 03、続く 8 バイトが切った時刻）。</summary>
    public const string DisabledMark = "03 00 00 00 00 40 2B 6A 12 34 DB 01";

    public string RunKey => Path.Combine(DataDirectory, "run-key.txt");

    public string Approval => RunKey + ".approved";

    /// <summary>このアプリが書くはずのもの（e2e では、走っている exe そのもの）。</summary>
    public static string Expected => $"\"{Path.Combine(HostDirectory, "SpoitableToolKit.Host.exe")}\" --tray";

    public string? Registered() => File.Exists(RunKey) ? File.ReadAllText(RunKey).Trim() : null;

    protected virtual string StartingRegistration => Elsewhere;

    protected virtual string? StartingApproval => null;

    protected override void Prepare()
    {
        File.WriteAllText(Path.Combine(DataDirectory, "config.json"), @"{ ""Language"": ""Ja"", ""StartWithWindows"": true }");
        File.WriteAllText(RunKey, StartingRegistration);
        if (StartingApproval is { } mark) File.WriteAllText(Approval, mark);
        ExtraEnvironment["SPOITABLE_RUN_KEY"] = RunKey;
    }

    /// <summary>config.json に入っている StartWithWindows（無ければ null）。</summary>
    public bool? SavedStartWithWindows() =>
        System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(DataDirectory, "config.json")))?["StartWithWindows"]?.GetValue<bool>();

    /// <summary>ファイルの状態がそうなるまで待つ（書くのはアプリの裏）。</summary>
    public static async Task WaitForAsync(Func<bool> done, string what)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (done()) return;
            await Task.Delay(250);
        }

        Assert.True(done(), what);
    }
}
