using SpoitableToolKit.Abstractions;

namespace SpoitableToolKit.Host.Modules;

/// <summary>
/// モジュール 1 つ分の「登録を受ける間」（OnInit の間）。<c>host.Ui</c>（<see cref="ModuleUi"/>）と <c>host.Shortcuts</c>（<see cref="Shortcuts.ModuleShortcuts"/>）が分け合う。
/// <see cref="ModuleManager"/> が OnInit の直前に開け、返った直後に締める（投げても false でも）。
/// <para>
/// 登録する側は <see cref="Gate"/> を持ったまま <see cref="Admit"/> を呼び、そのまま覚える — 締めるのと競っても、
/// 締める前に覚えたか、断られたかのどちらかになる（別のスレッドから登録するモジュールもありうる）。
/// </para>
/// </summary>
/// <param name="moduleId">文に出す名前。</param>
internal sealed class RegistrationWindow(string moduleId)
{
    private bool _open;

    public Lock Gate { get; } = new();

    public string ModuleId => moduleId;

    public void Open()
    {
        lock (Gate) _open = true;
    }

    public void Close()
    {
        lock (Gate) _open = false;
    }

    /// <summary>
    /// 開いているかを見る（<see cref="Gate"/> を持って呼ぶ）。締まっていれば、何をいつ登録すればよいかの文で断る。
    /// </summary>
    /// <param name="call">呼ばれたもの（<c>host.Ui.RegisterPanel</c> など）。</param>
    /// <param name="what">OnInit で登録するものの言い方（<c>the panel, the endpoint, …</c>）。</param>
    public void Admit(string call, string what)
    {
        if (_open) return;

        throw new InvalidOperationException(
            $"{moduleId} called {call} outside OnInit. Register {what} in {nameof(ISpoitableModule.OnInit)}: " +
            "the app decides what to show for a module right after OnInit returns, so a later registration would never be shown.");
    }
}
