using System.ComponentModel;
using SpoitableToolKit.Host.Ui.Desktop;

namespace SpoitableToolKit.Host.Ui.Tray;

/// <summary>
/// タスクトレイのアイコン。左クリックで開く、右クリックでメニュー（開くたびに組み立て直す）。
/// UI のスレッドで作って片付ける。押されたときの動きも UI のスレッドから呼ぶ。
/// Explorer が落ちて立ち直ったときに出し直すのは NotifyIcon 自身（タスクバーができ直した知らせを受けている）。
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    public const string Tooltip = "SpoitableToolKit";

    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly Icon _small;
    private readonly Font _bold;
    private readonly Func<IReadOnlyList<TrayMenuItem>> _build;
    private readonly Action<string> _pressed;
    private bool _disposed;

    /// <param name="build">メニューの中身。開くたびに呼ぶ。</param>
    /// <param name="pressed">押された項目の名前（<see cref="TrayMenuItem.Id"/>）。</param>
    /// <param name="open">左クリック。</param>
    public TrayIcon(Func<IReadOnlyList<TrayMenuItem>> build, Action<string> pressed, Action open)
    {
        _build = build;
        _pressed = pressed;

        _menu = new ContextMenuStrip();
        _menu.Opening += OnOpening;
        _bold = new Font(_menu.Font, FontStyle.Bold);

        // トレイの大きさで取り直す（.ico の中から近いものを選ぶ）。大きいまま渡すと Windows が縮めて滲む。
        _small = new Icon(AppIcon.Window, SystemInformation.SmallIconSize);

        _icon = new NotifyIcon { Icon = _small, Text = Tooltip, ContextMenuStrip = _menu };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) open();
        };
        _icon.Visible = true;
    }

    /// <summary>項目が 0 のとき、ContextMenuStrip は開く前の Cancel を真にして渡してくる。組み立て直したあとで戻さないと、メニューが開かない。</summary>
    private void OnOpening(object? sender, CancelEventArgs e)
    {
        Fill(_menu.Items, _build());
        e.Cancel = _menu.Items.Count == 0;
    }

    private void Fill(ToolStripItemCollection items, IReadOnlyList<TrayMenuItem> entries)
    {
        foreach (var old in items.Cast<ToolStripItem>().ToArray()) old.Dispose();
        items.Clear();

        foreach (var entry in entries)
        {
            if (entry.Separator)
            {
                items.Add(new ToolStripSeparator());
                continue;
            }

            var item = new ToolStripMenuItem(entry.Label) { Checked = entry.Checked, Enabled = entry.Enabled };
            if (entry.Default) item.Font = _bold;

            if (entry.Children.Count > 0)
            {
                Fill(item.DropDownItems, entry.Children);
            }
            else
            {
                var id = entry.Id;
                item.Click += (_, _) => _pressed(id);
            }

            items.Add(item);
        }
    }

    /// <summary>何度呼んでもよい（終了の道と、Application.Run が返ったあとの片付けの両方から来る）。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 先に隠す。片付けを待たずにトレイから消えないと、終了したあとも押せるアイコンが数秒残る。
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _bold.Dispose();
        _small.Dispose();
    }
}
