namespace SpoitableToolKit.Host.Settings;

/// <summary>閉じるボタン（× / Alt+F4 / タスクバーの「ウィンドウを閉じる」）を押したときの動き。画面の選択肢にそのまま名前が出る。</summary>
internal enum CloseButtonAction
{
    /// <summary>次に閉じるときに聞く。答えを覚えて、ここが答えに変わる。</summary>
    Ask,

    /// <summary>窓を畳んでトレイにしまう（アプリは動いたまま）。</summary>
    Tray,

    Exit,
}
