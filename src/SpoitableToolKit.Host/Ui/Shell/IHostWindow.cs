using SpoitableToolKit.Host.Settings;

namespace SpoitableToolKit.Host.Ui.Shell;

/// <summary>
/// 枠を自前で描いているので、最小化・最大化・閉じる・掴んで動かすを画面側から頼む。
/// UiBridge は窓より先に作られ、VR のサーフェスには窓が無いので、窓の実体を知らずに済むよう挟んでいる。
/// 窓はトレイにしまうたびに作り直すので、受けるのは窓そのものではなく、いまの窓へ渡す係（<see cref="Desktop.DesktopApp"/>）。
/// どのスレッドから呼んでもよい。窓が無い間に来た窓の操作は何もしない。
/// </summary>
internal interface IHostWindow
{
    bool IsMaximized { get; }

    void Minimize();

    void ToggleMaximize();

    /// <summary>しまうか、終了するか、聞くかは設定しだい（<see cref="HostConfig.CloseButton"/>）。</summary>
    void CloseWindow();

    /// <summary>閉じるときの問いの答え（<see cref="CloseButtonAction.Tray"/> か <see cref="CloseButtonAction.Exit"/>）。設定に書いてから、その動きをする。</summary>
    void AnswerClose(CloseButtonAction choice);

    /// <summary>押した瞬間に呼ぶ。以降の追従は Windows がやるので、こちらでマウスを追いかけない。</summary>
    void BeginDrag();

    /// <summary>窓はデスクトップに出る（VR の窓から押しても）。開いていれば前に出す。</summary>
    void OpenChromiumCredits(string title);
}
