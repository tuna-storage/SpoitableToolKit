using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Events.Config;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Updates;
using Velopack.Locators;

namespace SpoitableToolKit.Host.App.Startup;

/// <summary>
/// Windows の起動時の起動（<see cref="HostConfig.StartWithWindows"/>）を、登録（HKCU の Run）に合わせる。<b>設定が正</b>で、起動したときと設定を変えたときに合わせ直す。
/// <para>
/// Windows の「スタートアップ アプリ」で切られていたら（<see cref="StartupApproval.Disabled"/>）、そちらを設定に写す（切にする。<see cref="Reconcile"/>）。
/// 見るのは起動したときと、設定の画面が値を取りに来たとき。写したあとは設定が正の決まりのまま、Run の値も印も片付ける。
/// こちらで入れ直したら、Run を書き直して切の印も消す（消さないと Run があっても起動しない）。
/// </para>
/// <para>
/// 登録するのはインストール版だけ（<see cref="Create"/>）。開発の bin・持ち運ぶ版・e2e は、レジストリに一切触らない —
/// 開発版も <c>SPOITABLE_DATA_DIR</c> が無ければ同じ置き場（同じ設定）で動き、HKCU も同じなので、合わせ直すと手元のインストール版の登録を書き換えたり消したりする。
/// e2e は登録と印をファイルへ向け替える（<see cref="FileVariable"/>）。そのときは登録できるものとして扱う（行も出す）。
/// </para>
/// <para>
/// 指す exe は <c>current\SpoitableToolKit.Host.exe</c>。Velopack が作るスタートメニューとデスクトップのショートカットと同じ先で、更新を当てても場所は変わらない
/// （Velopack は <c>current</c> の中身を入れ替える）。アンインストールのときに消すのは <see cref="RemoveOnUninstall"/>（Program の頭の Velopack の hook）。
/// </para>
/// </summary>
/// <param name="setWanted">設定を書く口（設定の画面と同じ適用の道: <see cref="SettingsApplier.SetHost"/>）。書けたら true。</param>
internal sealed class WindowsStartup(IRunKey? key, string command, HostConfig config, Func<bool, bool> setWanted, ILogger logger)
    : IConfigEventListener<HostConfig>
{
    /// <summary>トレイにしまった状態で始める印。Windows の起動時の起動に付ける。「再起動」では引き継がない（<see cref="AppRestart.ArgumentsFor"/>）。</summary>
    public const string TrayArgument = "--tray";

    /// <summary>Run の値の名前（Windows の「スタートアップ アプリ」にこの名前で並ぶ。印も同じ名前）。</summary>
    public const string ValueName = "SpoitableToolKit";

    /// <summary>e2e の印: 登録と印をこのファイル（と隣のファイル）へ向け替える（<see cref="RunKeyFile"/>）。</summary>
    public const string FileVariable = "SPOITABLE_RUN_KEY";

    /// <summary>画面を開いた面ごとに別のスレッドから来るので、合わせ直しは 1 つずつ。</summary>
    private readonly Lock _gate = new();

    /// <summary>登録できる形か。違えば設定の行も出さない（<see cref="HostConfig.StartWithWindowsAvailable"/>）。</summary>
    public bool Available => key is not null;

    /// <summary>
    /// 起動したとき・設定の画面が値を取りに来たとき。Windows の側で切られていれば設定に写してから（ログに 1 行）、登録を設定に合わせる。
    /// 写せなかったら（断られた）何もしない — そのまま合わせると、Windows で切った起動をこちらが入れ直してしまう。
    /// </summary>
    public void Reconcile()
    {
        if (key is null) return;

        StartupApproval approval;
        try
        {
            lock (_gate) approval = key.ReadApproval();
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            logger.LogWarning(ex, "Could not read whether Windows' startup apps turned off {Name}", ValueName);
            approval = StartupApproval.Missing;
        }

        if (StartupRegistration.TurnedOffInWindows(config.StartWithWindows, approval))
        {
            if (!setWanted(false))
            {
                logger.LogWarning("Starting with Windows was turned off in Windows' startup apps, but the setting could not follow it");
                return;
            }

            logger.LogInformation("Starting with Windows was turned off in Windows' startup apps; turned the setting off to match");
        }

        Sync();
    }

    /// <summary>登録と印を設定に合わせる。登録できない形なら何もしない。読めない・書けないときはログに残して進む（起動を止めない）。</summary>
    public void Sync()
    {
        if (key is null) return;

        try
        {
            lock (_gate)
            {
                var wanted = config.StartWithWindows;
                var registered = key.Read();
                switch (StartupRegistration.Plan(wanted, registered, command))
                {
                    case StartupChange.Write:
                        key.Write(command);
                        logger.LogInformation("The app starts with Windows: wrote {Command} to {Where} (it was {Before})", command, key.Where, registered ?? "not registered");
                        break;

                    case StartupChange.Remove:
                        key.Remove();
                        logger.LogInformation("The app no longer starts with Windows: removed {Where} ({Before})", key.Where, registered);
                        break;
                }

                var approval = key.ReadApproval();
                if (StartupRegistration.ClearsApproval(wanted, approval))
                {
                    key.RemoveApproval();
                    logger.LogInformation("Removed the {Approval} mark that Windows' startup apps kept for {Name}", approval, ValueName);
                }
            }
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            logger.LogWarning(ex, "Could not match {Where} to the setting to start with Windows", key.Where);
        }
    }

    /// <summary>設定を変えたその場では Windows の印を写さない（こちらで入れ直したなら、こちらが新しい）。合わせるだけ。</summary>
    public void OnConfigChanged(ConfigChangedEventParams<HostConfig> e)
    {
        if (e.Changed(c => c.StartWithWindows)) Sync();
    }

    private static bool IsRegistryFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    /// <summary>登録の置き場を決める。e2e の向け先 → インストール版のレジストリ → どちらでもなければ登録しない（<see cref="Available"/> が偽）。</summary>
    public static WindowsStartup Create(HostConfig config, Func<bool, bool> setWanted, ILogger logger)
    {
        if (Environment.GetEnvironmentVariable(FileVariable) is { Length: > 0 } file)
        {
            logger.LogInformation("{Variable} is set; starting with Windows is registered in {File} instead of the registry", FileVariable, file);
            return new WindowsStartup(new RunKeyFile(file), StartupRegistration.CommandFor(Environment.ProcessPath ?? ""), config, setWanted, logger);
        }

        if (InstalledExecutable() is { } exe)
            return new WindowsStartup(new RegistryRunKey(ValueName), StartupRegistration.CommandFor(exe), config, setWanted, logger);

        return new WindowsStartup(null, "", config, setWanted, logger);
    }

    /// <summary>
    /// インストール版の exe（<c>current\</c> の下）。Velopack が入れた形でない・持ち運ぶ版・見つからないときは null。
    /// 持ち運ぶ版はアンインストールが無い（フォルダを消すだけ）ので、登録すると消えた先を指したまま残る。
    /// </summary>
    private static string? InstalledExecutable()
    {
        if (!VelopackUpdateSource.IsVelopackLayout(AppContext.BaseDirectory) || !VelopackLocator.IsCurrentSet) return null;

        var locator = VelopackLocator.Current;
        if (locator.IsPortable || locator.AppContentDir is not { Length: > 0 } content || Environment.ProcessPath is not { } running) return null;

        var exe = Path.Combine(content, Path.GetFileName(running));
        return File.Exists(exe) ? exe : null;
    }

    /// <summary>
    /// アンインストールのとき（Velopack の <c>OnBeforeUninstallFastCallback</c>。このあと Velopack がプロセスを閉じる）。登録と印を消す。
    /// ログはまだ無いので残さない。消せなくても止めない（アンインストールを失敗させない）。
    /// </summary>
    public static void RemoveOnUninstall()
    {
        try
        {
            var key = new RegistryRunKey(ValueName);
            key.Remove();
            key.RemoveApproval();
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            // 消せなかった。残った登録は、消えた exe を指すだけ（Windows は黙って飛ばす）。
        }
    }
}
