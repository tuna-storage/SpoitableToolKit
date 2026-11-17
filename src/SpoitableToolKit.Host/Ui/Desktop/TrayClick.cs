namespace SpoitableToolKit.Host.Ui.Desktop;

/// <summary>トレイのアイコンを左クリックしたときにすること（<see cref="TrayToggle"/>）。</summary>
internal enum TrayClick
{
    Open,

    /// <summary>窓を畳んでトレイにしまう（× で「トレイにしまう」を選んだときと同じ）。</summary>
    PutAway,
}
