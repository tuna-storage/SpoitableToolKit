namespace SpoitableToolKit.Host.Ui.Desktop.Closing;

/// <summary>窓が閉じられかけたときにすること（<see cref="CloseDecision"/>）。</summary>
internal enum CloseStep
{
    /// <summary>閉じるのを止めて、シェルのモーダルで聞く。</summary>
    Ask,

    /// <summary>窓を畳んでトレイにしまう（アプリは動いたまま）。</summary>
    Tray,

    /// <summary>窓を閉じて、アプリを終了する。</summary>
    Exit,
}
