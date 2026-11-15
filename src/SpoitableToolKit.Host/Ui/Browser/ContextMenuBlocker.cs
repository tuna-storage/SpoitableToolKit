using CefSharp;

namespace SpoitableToolKit.Host.Ui.Browser;

/// <summary>
/// CEF の既定のコンテキストメニュー（戻る / 進む / 再読み込み / 印刷 / ソースを表示）を止める。アプリの画面としては場違いで、自前のメニューを出すと二重になる。
/// ハンドラはブラウザ単位なので、ここ 1 枚でモジュールの iframe まで一括で止まる。
/// ⚠ この抑止は DOM から観測できない（ネイティブのメニューは CEF が OS 側に描く）。e2e で押さえられるのは <see cref="OnBeforeContextMenu"/> が model を空にするところまでで、実際に出ないことは人の目でしか確かめられない。
/// 自前のメニューは CEF の外（シェルの overlay）に描くので <see cref="RunContextMenu"/> は使わない（<c>true</c> を返すとコールバックを呼ぶまで CEF が待つ）。
/// </summary>
internal sealed class ContextMenuBlocker : IContextMenuHandler
{
    public void OnBeforeContextMenu(
        IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IContextMenuParams parameters, IMenuModel model)
        => model.Clear();

    /// <summary>空にした以上ここへは来ない。既定の実装に落とす意味も無いので <c>false</c>。</summary>
    public bool OnContextMenuCommand(
        IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IContextMenuParams parameters,
        CefMenuCommand commandId, CefEventFlags eventFlags)
        => false;

    public void OnContextMenuDismissed(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame)
    {
    }

    /// <summary><c>false</c> = CEF の既定の出し方に任せる。ただし model は空なので何も出ない。</summary>
    public bool RunContextMenu(
        IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IContextMenuParams parameters,
        IMenuModel model, IRunContextMenuCallback callback)
        => false;
}
