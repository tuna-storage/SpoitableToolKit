using SpoitableToolKit.Host.Ui.Desktop;

namespace SpoitableToolKit.Host.Ui.Tray;

/// <summary>
/// タスクトレイのアイコン。左クリックで開く、右クリックで Windows 標準のメニュー（<see cref="TrayPopupMenu"/>。開くたびに組み立て直す）。
/// UI のスレッドで作って片付ける。押されたときの動きも UI のスレッドから呼ぶ。
/// Explorer が落ちて立ち直ったときに出し直すのは NotifyIcon 自身（タスクバーができ直した知らせを受けている）。
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    public const string Tooltip = "SpoitableToolKit";

    private readonly NotifyIcon _icon;
    private readonly Icon _small;
    private readonly Func<IReadOnlyList<TrayMenuItem>> _build;
    private readonly Action<string> _pressed;
    private readonly Func<IntPtr> _owner;
    private bool _showing;
    private bool _disposed;

    /// <param name="build">メニューの中身。開くたびに呼ぶ。</param>
    /// <param name="pressed">押された項目の名前（<see cref="TrayMenuItem.Id"/>）。</param>
    /// <param name="open">左クリック。</param>
    /// <param name="owner">メニューの持ち主の窓（アプリの見えない窓）。出すたびに引く。</param>
    public TrayIcon(Func<IReadOnlyList<TrayMenuItem>> build, Action<string> pressed, Action open, Func<IntPtr> owner)
    {
        _build = build;
        _pressed = pressed;
        _owner = owner;

        // トレイの大きさで取り直す（.ico の中から近いものを選ぶ）。大きいまま渡すと Windows が縮めて滲む。
        _small = new Icon(AppIcon.Window, SystemInformation.SmallIconSize);

        _icon = new NotifyIcon { Icon = _small, Text = Tooltip };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) open();
            else if (e.Button == MouseButtons.Right) ShowMenu(Cursor.Position);
        };
        _icon.Visible = true;
    }

    /// <summary>メニューを出す。開いている間にもう一度来たら何もしない。押された項目は、メニューが閉じてから渡す。</summary>
    /// <param name="at">画面の座標。</param>
    public void ShowMenu(Point at)
    {
        if (_disposed || _showing) return;

        _showing = true;
        string? chosen;
        try
        {
            chosen = TrayPopupMenu.Show(_build(), _owner(), at);
        }
        finally
        {
            _showing = false;
        }

        // 開いている間に終了した（taskkill・ログオフ）なら、押されたものは無かったことにする。
        if (chosen is not null && !_disposed) _pressed(chosen);
    }

    /// <summary>何度呼んでもよい（終了の道と、Application.Run が返ったあとの片付けの両方から来る）。メニューが開いていれば閉じる。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_showing) TrayPopupMenu.Close();

        // 先に隠す。片付けを待たずにトレイから消えないと、終了したあとも押せるアイコンが数秒残る。
        _icon.Visible = false;
        _icon.Dispose();
        _small.Dispose();
    }
}
