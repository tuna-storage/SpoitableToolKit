namespace SpoitableToolKit.Host.Ui.Shell;

/// <summary>
/// 枠を自前で描いているので、最小化・最大化・閉じる・掴んで動かすを画面側から頼む。
/// UiBridge は窓より先に作られ、VR のサーフェスには窓が無いので、窓の実体を知らずに済むよう挟んでいる。
/// </summary>
internal interface IHostWindow
{
    bool IsMaximized { get; }

    void Minimize();

    void ToggleMaximize();

    /// <summary>トレイに残るかどうかは窓側の判断。</summary>
    void CloseWindow();

    /// <summary>押した瞬間に呼ぶ。以降の追従は Windows がやるので、こちらでマウスを追いかけない。</summary>
    void BeginDrag();

    /// <summary>窓はデスクトップに出る（VR の窓から押しても）。開いていれば前に出す。</summary>
    void OpenChromiumCredits(string title);
}
