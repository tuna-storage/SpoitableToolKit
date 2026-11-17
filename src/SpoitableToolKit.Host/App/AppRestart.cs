using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SpoitableToolKit.Host.App;

/// <summary>
/// アプリを起動し直す（モジュールの ON/OFF・外す・入れ替えは、次の起動で効く）。
/// <para>
/// 新しいアプリを先に立て、このアプリが抜けるまで待たせてから進ませる（<see cref="WaitArgument"/>）。
/// 同時に動くと、CEF の置き場・SQLite・心拍計（BLE）・デバッグのポートを取り合う。
/// </para>
/// <para>
/// e2e は本当には起動し直さない。<c>SPOITABLE_RESTART_RECORD=&lt;ファイル&gt;</c> があれば、起動する代わりに
/// 渡す引数をそのファイルへ書いて、このアプリも閉じない（試験が知らないアプリが残らないように）。
/// </para>
/// </summary>
/// <param name="closeApp">このアプリを終了する（<see cref="Ui.Desktop.DesktopApp.Exit"/>）。</param>
internal sealed class AppRestart(IReadOnlyList<string> arguments, Action closeApp, ILogger logger)
{
    /// <summary>新しいアプリに渡す「この PID が抜けるまで待つ」の印。</summary>
    public const string WaitArgument = "--after-pid";

    /// <summary>前のアプリを待つ長さ。抜けなければ、そのまま進む（待ち続けて何も出ないよりよい）。</summary>
    public static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    /// <summary>「再起動」で立ったアプリか（<see cref="WaitArgument"/> がある）。1 つだけの印（<see cref="SingleInstance"/>）を、前のアプリが離すまで待つ。</summary>
    public static bool IsRestart(IReadOnlyList<string> args) => args.Contains(WaitArgument);

    /// <summary>
    /// 新しいアプリに渡す引数。今の引数をそのまま（前の <see cref="WaitArgument"/> は外す）に、待つ印を足す。
    /// トレイにしまって始める印（<see cref="Startup.WindowsStartup.TrayArgument"/>。Windows の起動時の起動が付ける）も外す —
    /// 自動で起動した回に窓から「再起動」すると、窓が消えてトレイに隠れたまま戻ってくる（しまって始めたい人は設定で選べる）。
    /// </summary>
    public static string[] ArgumentsFor(IReadOnlyList<string> current, int processId)
    {
        List<string> next = [];
        for (var i = 0; i < current.Count; i++)
        {
            if (current[i] == WaitArgument)
            {
                i++;
                continue;
            }

            if (current[i] == Startup.WindowsStartup.TrayArgument) continue;

            next.Add(current[i]);
        }

        next.Add(WaitArgument);
        next.Add(processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return [.. next];
    }

    /// <summary>
    /// 起動し直す。新しいアプリを立てられたら、このアプリを終了する（窓を閉じるのではない — 閉じるボタンがトレイにしまう設定でも、終わる）。
    /// 立てられなければ終了しない（何も動いていない状態にしない）。
    /// </summary>
    public bool Request()
    {
        var next = ArgumentsFor(arguments, Environment.ProcessId);

        if (Environment.GetEnvironmentVariable("SPOITABLE_RESTART_RECORD") is { Length: > 0 } record)
        {
            File.WriteAllLines(record, next);
            logger.LogInformation("SPOITABLE_RESTART_RECORD is set; wrote the restart arguments to {File} instead of restarting", record);
            return true;
        }

        if (Environment.ProcessPath is not { } exe)
        {
            logger.LogError("Cannot restart: the path of this program is unknown");
            return false;
        }

        try
        {
            var start = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory,
            };
            foreach (var argument in next) start.ArgumentList.Add(argument);

            using var started = Process.Start(start);
            if (started is null)
            {
                logger.LogError("Cannot restart: the new process did not start");
                return false;
            }

            logger.LogInformation("Restarting: started {Pid}, which waits for this one ({Self}) to exit", started.Id, Environment.ProcessId);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogError(ex, "Cannot restart");
            return false;
        }

        closeApp();
        return true;
    }

    /// <summary>
    /// 起動の頭で呼ぶ。<see cref="WaitArgument"/> があれば、その PID のアプリが抜けるまで待つ（最大 <see cref="WaitLimit"/>）。
    /// 別のプログラムの PID なら待たない（番号は使い回されるので、前のアプリがもう抜けていればありうる）。
    /// </summary>
    public static void WaitForPrevious(IReadOnlyList<string> args)
    {
        using var self = Process.GetCurrentProcess();
        WaitForPrevious(args, self.ProcessName, WaitLimit);
    }

    /// <summary>
    /// <paramref name="processName"/> の名前のプログラムだけを、<paramref name="limit"/> まで待つ。
    /// 待てなくても起動は止めない（調べられない・もう居ない・途中で抜けた、のどれでも進む）。
    /// </summary>
    internal static void WaitForPrevious(IReadOnlyList<string> args, string processName, TimeSpan limit)
    {
        var index = args.ToList().IndexOf(WaitArgument);
        if (index < 0 || index + 1 >= args.Count || !int.TryParse(args[index + 1], out var pid)) return;

        try
        {
            using var previous = Process.GetProcessById(pid);
            if (!string.Equals(previous.ProcessName, processName, StringComparison.OrdinalIgnoreCase)) return;
            previous.WaitForExit(limit);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // もう居ない・調べている間に抜けた・調べさせてもらえない（別の利用者のプログラム）。どれも待たずに進む。
        }
    }
}
