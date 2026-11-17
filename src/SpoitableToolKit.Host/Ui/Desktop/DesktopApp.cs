using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Abstractions.Localization;
using SpoitableToolKit.Host.App.Startup;
using SpoitableToolKit.Host.Settings;
using SpoitableToolKit.Host.Ui.Desktop.Closing;
using SpoitableToolKit.Host.Ui.Licenses;
using SpoitableToolKit.Host.Ui.Push;
using SpoitableToolKit.Host.Ui.Shell;
using SpoitableToolKit.Host.Ui.Tray;

namespace SpoitableToolKit.Host.Ui.Desktop;

/// <summary>
/// デスクトップの側の寿命（<c>Application.Run</c> が回すもの）。窓・トレイのアイコン・見えない窓（<see cref="AppMessageWindow"/>）を持ち、アプリを終わらせるのもここ。
/// <list type="bullet">
///   <item>窓はトレイにしまうたびに閉じて破棄し（windowed の browser ごと落ちる）、開くたびに作り直す。
///         <see cref="ApplicationContext.MainForm"/> は使わない（使うと、窓を閉じた時点で context ごと終わる）</item>
///   <item>終わるのは <see cref="Exit"/> を通ったときだけ（トレイの「終了」・閉じるボタン・再起動・更新の適用・ログオフ・taskkill）。
///         終わると <c>Application.Run</c> が返り、Program の finally がいつもどおり畳む</item>
///   <item>画面からの窓の操作（<see cref="IHostWindow"/>）は、UI のスレッドで「いまの窓」を引き直してから渡す。窓が無ければ何もしない（破棄した Form には触らない）</item>
/// </list>
/// どのスレッドから呼んでもよいのは <see cref="IHostWindow"/> と <see cref="Exit"/>。ほかは UI のスレッドで走る。
/// </summary>
internal sealed class DesktopApp : ApplicationContext, IHostWindow
{
    private readonly UiBridge _bridge;
    private readonly UiBroadcast _broadcast;
    private readonly HostConfig _config;
    private readonly SettingsApplier _settings;
    private readonly ILogger _logger;
    private readonly bool _offscreen;
    private readonly WindowsFormsSynchronizationContext _ui;
    private readonly AppMessageWindow _messages;
    private readonly TrayIcon? _tray;
    private readonly ChromiumCreditsSlot _credits;
    private readonly RegisteredWaitHandle _showWait;

    /// <summary>いまの窓。書くのは UI のスレッドだけ（別のスレッドから読むのは <see cref="IsMaximized"/> だけ）。</summary>
    private volatile DesktopWindow? _window;

    /// <summary>いまの窓が閉じ終わったら終了するか。閉じかけたときに決める。</summary>
    private bool _exitWhenClosed;

    /// <summary>こちらから閉じている最中（しまう・終了）。閉じかけの知らせで、決め直さない。</summary>
    private bool _closingOurselves;

    private bool _exiting;

    /// <param name="showRequests">「前に出て」の印（<see cref="App.SingleInstance.ShowRequests"/>。2 つ目に起動されたアプリが立てる）。待ち始める前に立った印も、立ったまま残っているので受け取れる。</param>
    /// <param name="startInTray">窓を開かずに始める（<see cref="StartsInTray"/>）。</param>
    public DesktopApp(
        UiBridge bridge, UiBroadcast broadcast, HostConfig config, SettingsApplier settings, ILocalizer text,
        WaitHandle showRequests, bool startInTray, ILogger logger)
    {
        _bridge = bridge;
        _broadcast = broadcast;
        _config = config;
        _settings = settings;
        _logger = logger;
        _offscreen = DesktopWindow.OffscreenRequested;
        _credits = new ChromiumCreditsSlot(_offscreen, logger);

        // UI のスレッドへ渡す先。窓より先に要る（しまった状態で起動すると、窓は 1 つも無い）。VR の周（VrLoop）もこれを読んで UI のスレッドで回る。
        if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        _ui = (WindowsFormsSynchronizationContext)SynchronizationContext.Current!;

        _messages = new AppMessageWindow(Exit);

        if (_offscreen)
            logger.LogInformation("SPOITABLE_UI_OFFSCREEN is set; no tray icon (a second start with the same data folder opens the window)");
        else
            _tray = new TrayIcon(() => TrayMenu.Build(text), Pressed, Open);

        _showWait = ThreadPool.RegisterWaitForSingleObject(showRequests, (_, _) => Post(ShowRequested), null, Timeout.Infinite, executeOnlyOnce: false);

        if (startInTray) logger.LogInformation("Starting in the tray; the window opens from the tray icon");
        else Open();
    }

    /// <summary>窓を開かずに始めるか。Windows の起動時の起動（<see cref="WindowsStartup.TrayArgument"/>）は、設定に関係なくしまった状態で始まる。</summary>
    public static bool StartsInTray(IReadOnlyList<string> args, HostConfig config) =>
        args.Contains(WindowsStartup.TrayArgument) || config.StartInTray;

    /// <summary>アプリを終了する（どのスレッドからでも）。窓・トレイのアイコンを片付けてから <c>Application.Run</c> を返す。何度呼んでもよい。</summary>
    public void Exit() => Post(ExitNow);

    public bool IsMaximized => _window?.IsMaximized ?? false;

    public void Minimize() => Post(() => _window?.Minimize());

    public void ToggleMaximize() => Post(() => _window?.ToggleMaximize());

    /// <summary>× と同じ道（<see cref="OnWindowClosing"/> が設定を見て決める）。</summary>
    public void CloseWindow() => Post(() => _window?.Close());

    public void BeginDrag() => Post(() => _window?.BeginDrag());

    public void OpenChromiumCredits(string title) => Post(() =>
    {
        if (!_exiting) _credits.Open(title, _window);
    });

    /// <summary>答えは先に覚える（設定の画面と同じ道。開いている設定の画面もこの値に変わる）。そのあとで、しまうか終了する。</summary>
    public void AnswerClose(CloseButtonAction choice)
    {
        _settings.SetHost(nameof(HostConfig.CloseButton), JsonValue.Create(choice.ToString()));
        _logger.LogInformation("The close button now does {Choice} (asked the first time; it can be changed in the settings)", choice);
        Post(() => Do(choice == CloseButtonAction.Exit ? CloseStep.Exit : CloseStep.Tray));
    }

    /// <summary>トレイのメニューで押された。</summary>
    private void Pressed(string id)
    {
        switch (id)
        {
            case TrayMenu.Open:
                Open();
                break;

            case TrayMenu.Exit:
                ExitNow();
                break;
        }
    }

    /// <summary>窓を開く。無ければ作り、あれば前に出す。</summary>
    private void Open()
    {
        if (_exiting) return;

        if (_window is { } open)
        {
            open.ComeToFront();
            return;
        }

        var window = new DesktopWindow(_bridge, _broadcast);
        window.FormClosing += OnWindowClosing;
        window.FormClosed += OnWindowClosed;
        window.HandleDestroyed += OnWindowDestroyed;
        _window = window;
        _exitWhenClosed = false;

        window.Show();
        window.ComeToFront();
    }

    /// <summary>2 つ目に起動されたアプリに「前に出て」と言われた。窓が無ければ作って開く。どの道でもアプリログに 1 行残す。</summary>
    private void ShowRequested()
    {
        if (_exiting) return;

        if (_window is { } window)
        {
            _logger.LogInformation(window.Offscreen
                ? "The app was started again with the same data folder; staying where it is (the window is off screen)"
                : "The app was started again with the same data folder; bringing this window to the front instead");
            window.ComeToFront();
            return;
        }

        _logger.LogInformation("The app was started again with the same data folder; opening the window from the tray");
        Open();
    }

    /// <summary>
    /// 窓が閉じられかけた（×・Alt+F4・タスクバーの「ウィンドウを閉じる」・ログオフ・タスクマネージャー）。何をするかは <see cref="CloseDecision"/>。
    /// しまうのも閉じることなので、しまう・終了は閉じるのをそのまま通し、閉じ終わってから決める（<see cref="OnWindowClosed"/>）。止めるのは聞くときだけ。
    /// </summary>
    private void OnWindowClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closingOurselves || sender is not DesktopWindow window) return;

        var step = CloseDecision.For(e.CloseReason, _config.CloseButton);
        _logger.LogInformation("The window is closing ({Reason}): {Step}", e.CloseReason, step);

        if (step == CloseStep.Ask)
        {
            e.Cancel = true;
            AskHowToClose(window);
            return;
        }

        _exitWhenClosed = step == CloseStep.Exit;
    }

    private void OnWindowClosed(object? sender, FormClosedEventArgs e)
    {
        if (!ReferenceEquals(sender, _window)) return;
        _window = null;

        if (_exitWhenClosed)
        {
            ExitNow();
            return;
        }

        if (!_exiting) _logger.LogInformation("The window is in the tray; its browser is closed until the window is opened again");
    }

    /// <summary>
    /// 閉じる知らせを通らずに窓が壊された。画面の外に置いた窓（タスクバーに出さない）は WinForms が作った見えない持ち主の窓にぶら下がっていて、
    /// 持ち主が <c>WM_CLOSE</c> で壊されると、窓も閉じる知らせ無しに一緒に壊れる。外から「終わって」と言われたのと同じなので、終了する
    /// （残すと、壊れた窓を持ったまま動き続ける）。閉じ終わったあと・作り直している最中は、ここへ来ても何もしない。
    /// </summary>
    private void OnWindowDestroyed(object? sender, EventArgs e)
    {
        if (sender is not DesktopWindow window || window.RecreatingHandle || !ReferenceEquals(window, _window)) return;

        _window = null;
        _logger.LogInformation("The window was destroyed without being closed (its owner window was closed); exiting");
        ExitNow();
    }

    /// <summary>
    /// シェルのモーダルで聞く。答えは <see cref="AnswerClose"/> へ来る。閉じただけ（Esc・外を押した）なら何もしない（設定は聞くのまま、窓も開いたまま）。
    /// 出せなかったら（<see cref="CloseDecision.WhenTheShellCannotAsk"/>）しまう。
    /// </summary>
    private async void AskHowToClose(DesktopWindow window)
    {
        // 最小化のままだと、出したモーダルが見えない（タスクバーの「ウィンドウを閉じる」は最小化のままでも押せる）。
        window.ComeToFront();

        var asked = false;
        try
        {
            asked = await window.AskToCloseAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not ask how to close the window");
        }

        if (asked || !ReferenceEquals(window, _window)) return;

        _logger.LogInformation("Could not ask how to close the window (the screen is not ready yet); putting it in the tray, and asking again next time");
        Do(CloseDecision.WhenTheShellCannotAsk);
    }

    private void Do(CloseStep step)
    {
        if (step == CloseStep.Exit) ExitNow();
        else Collapse();
    }

    /// <summary>窓を閉じて破棄する（browser ごと落ちる）。アプリは動いたまま。</summary>
    private void Collapse()
    {
        if (_window is not { } window) return;

        _exitWhenClosed = false;
        CloseOurselves(window);
    }

    /// <summary>
    /// 閉じる。<c>Close</c> を通す（<c>Dispose</c> だけだと閉じた知らせが出ず、窓が push の宛先から外れない）。
    /// こちらから呼んだ <c>Close</c> も <see cref="CloseReason.UserClosing"/> になるので、決め直さないよう印を立てる。
    /// </summary>
    private void CloseOurselves(DesktopWindow window)
    {
        _closingOurselves = true;
        try
        {
            window.Close();
        }
        finally
        {
            _closingOurselves = false;
        }
    }

    private void ExitNow()
    {
        if (_exiting) return;
        _exiting = true;
        _logger.LogInformation("Exiting the app");

        _showWait.Unregister(null);
        if (_window is { } window) CloseOurselves(window);
        _credits.Close();

        // 先にトレイから消す。このあとの片付け（Program の finally）は数秒かかる。
        _tray?.Dispose();
        _messages.Dispose();

        ExitThread();
    }

    /// <summary>UI のスレッドへ渡す。抜けたあと（終了の後）に来たものは捨てる。</summary>
    private void Post(Action action)
    {
        try
        {
            _ui.Post(_ => action(), null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // もう抜けている。
        }
    }

    /// <summary><see cref="ExitNow"/> を通らずに抜けた（例外で Run が返った）ときの片付け。通ったあとなら何もしない。</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_exiting)
        {
            _exiting = true;
            _showWait.Unregister(null);
            _credits.Close();
            _tray?.Dispose();
            _messages.Dispose();
        }

        base.Dispose(disposing);
    }
}
