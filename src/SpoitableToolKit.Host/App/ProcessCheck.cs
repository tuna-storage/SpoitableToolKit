using System.Diagnostics;

namespace SpoitableToolKit.Host.App;

internal static class ProcessCheck
{
    /// <summary>名前（拡張子なし）で探す。見つけたものは閉じる（数秒おきに呼ぶので、握ったままにしない — アプリは何日も動く）。</summary>
    public static bool IsRunning(string name)
    {
        var found = Process.GetProcessesByName(name);

        foreach (var process in found) process.Dispose();

        return found.Length > 0;
    }
}
